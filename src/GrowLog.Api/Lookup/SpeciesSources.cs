using System.Collections.Frozen;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace GrowLog.Api.Lookup;

/// <summary>
/// Asks iNaturalist for names and pictures, and GBIF when that fails and to check names.
/// Only licences that allow business use are accepted for pictures.
/// </summary>
internal sealed class SpeciesSources(HttpClient http, SpeciesThrottle throttle)
{
    public const string INaturalistName = "iNaturalist";
    public const string GbifName = "GBIF";
    public const string GbifLicence = "CC BY 4.0";

    private const string INaturalist = "https://api.inaturalist.org/v1/";
    private const string Gbif = "https://api.gbif.org/v1/";
    private const string GbifBackbone = "d7dddbf4-2cf0-4f39-9b2a-bb099caae36c";
    private const int INaturalistPlants = 47126;
    private const int GbifPlants = 6;
    private const int MostResults = 30;

    private static readonly FrozenSet<string> Ranks = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "species", "hybrid", "genus", "variety");

    private static readonly FrozenDictionary<string, string> OpenLicences = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["cc0"] = "CC0",
        ["cc-by"] = "CC BY",
        ["cc-by-sa"] = "CC BY-SA",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> PhotoHosts = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "inaturalist-open-data.s3.amazonaws.com",
        "static.inaturalist.org");

    public static bool IsUnavailable(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException;

    /// <summary>Throws if neither source can be reached.</summary>
    public async Task<IReadOnlyList<FoundSpecies>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            var found = await SearchINaturalistAsync(query, cancellationToken);

            if (found.Count > 0)
            {
                return found;
            }
        }
        catch (Exception exception) when (IsUnavailable(exception) && !cancellationToken.IsCancellationRequested)
        {
            // GBIF can still supply names.
        }

        var byCommonName = await SearchGbifAsync(query, "VERNACULAR", cancellationToken);
        return byCommonName.Count > 0 ? byCommonName : await SearchGbifAsync(query, "SCIENTIFIC", cancellationToken);
    }

    /// <summary>Finds the accepted name, or null if GBIF has no exact match or can't be reached.</summary>
    public async Task<CheckedName?> CheckNameAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var match = await http.GetFromJsonAsync<JsonElement>(
                new Uri($"{Gbif}species/match?kingdom=Plantae&name={Uri.EscapeDataString(name)}"),
                cancellationToken);

            if (!string.Equals(Text(match, "matchType"), "EXACT", StringComparison.Ordinal))
            {
                return null;
            }

            var accepted = Text(match, "status") switch
            {
                "ACCEPTED" => Text(match, "canonicalName"),
                "SYNONYM" when string.Equals(Text(match, "rank"), "SPECIES", StringComparison.Ordinal) => Text(match, "species"),
                _ => null,
            };

            return accepted is not null && (Number(match, "acceptedUsageKey") ?? Number(match, "usageKey")) is { } key
                ? new CheckedName(accepted, key)
                : null;
        }
        catch (Exception exception) when (IsUnavailable(exception) && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public async Task<ImageFile?> DownloadPhotoAsync(string url, CancellationToken cancellationToken)
    {
        // The address came from another service, so it is only followed to where photos are known to live.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var address) || !string.Equals(address.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) || !PhotoHosts.Contains(address.Host))
        {
            return null;
        }

        try
        {
            using var response = await http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            return (await ImageContent.ReadAsync(body, cancellationToken)).Image;
        }
        catch (Exception exception) when (IsUnavailable(exception) && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : null;

    private static FoundPhoto? OpenPhoto(JsonElement photo)
    {
        if (photo.ValueKind != JsonValueKind.Object
            || Text(photo, "license_code") is not { } code
            || !OpenLicences.TryGetValue(code, out var licence)
            || (Text(photo, "medium_url") ?? Text(photo, "url")) is not { } url)
        {
            return null;
        }

        return new FoundPhoto(url, Text(photo, "attribution") ?? string.Empty, licence);
    }

    private async Task<JsonElement> GetFromINaturalistAsync(Uri address, CancellationToken cancellationToken)
    {
        using var lease = await throttle.WaitAsync(cancellationToken);

        if (!lease.IsAcquired)
        {
            throw new HttpRequestException("Too many plant searches at once.");
        }

        return await http.GetFromJsonAsync<JsonElement>(address, cancellationToken);
    }

    private async Task<List<FoundSpecies>> SearchINaturalistAsync(string query, CancellationToken cancellationToken)
    {
        var reply = await GetFromINaturalistAsync(
            new Uri(string.Create(
                CultureInfo.InvariantCulture,
                $"{INaturalist}taxa/autocomplete?q={Uri.EscapeDataString(query)}&taxon_id={INaturalistPlants}&locale=en-GB&per_page={MostResults}&is_active=true")),
            cancellationToken);

        var taxa = reply.GetProperty("results").EnumerateArray()
            .Where(IsGrowable)
            .Select(taxon => (Taxon: taxon, Matched: Text(taxon, "matched_term")))
            .ToList();

        var known = taxa.Select(row => Id(row.Taxon)).ToHashSet();
        var genus = taxa.Where(row => IsNamedGenus(row.Taxon, query)).OrderByDescending(row => Observations(row.Taxon)).Select(row => (long?)Id(row.Taxon)).FirstOrDefault();

        if (genus is { } genusId)
        {
            taxa.AddRange((await FindBestKnownSpeciesAsync(genusId, cancellationToken))
                .Where(child => known.Add(Id(child)))
                .Select(child => (Taxon: child, Matched: (string?)query)));
        }

        var open = taxa.ToDictionary(row => Id(row.Taxon), row => row.Taxon.TryGetProperty("default_photo", out var photo) ? OpenPhoto(photo) : null);
        var others = await FindOtherPhotosAsync(open.Where(row => row.Value is null).Select(row => row.Key).ToList(), cancellationToken);

        return [.. taxa.Select(row => new FoundSpecies(
            Text(row.Taxon, "name")!,
            Text(row.Taxon, "preferred_common_name"),
            Text(row.Taxon, "rank")!,
            row.Matched,
            INaturalistName,
            Licence: null,
            Id(row.Taxon),
            GbifKey: null,
            open[Id(row.Taxon)] ?? others.GetValueOrDefault(Id(row.Taxon)),
            Observations(row.Taxon)))];
    }

    private static bool IsGrowable(JsonElement taxon) =>
        Ranks.Contains(Text(taxon, "rank") ?? string.Empty) && Text(taxon, "name") is not null && Number(taxon, "id") is not null;

    private static long Id(JsonElement taxon) => taxon.GetProperty("id").GetInt64();

    private static int Observations(JsonElement taxon) => (int)Math.Min(Number(taxon, "observations_count") ?? 0, int.MaxValue);

    private static bool IsNamedGenus(JsonElement taxon, string query) =>
        string.Equals(Text(taxon, "rank"), "genus", StringComparison.OrdinalIgnoreCase)
        && ((Text(taxon, "preferred_common_name")?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
            || Text(taxon, "name")!.Contains(query, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Searching "lavender" finds the genus, but not garden lavender, whose common name doesn't start that way.
    /// A genus is rarely what's being planted, so its most recorded species are offered too.
    /// </summary>
    private async Task<List<JsonElement>> FindBestKnownSpeciesAsync(long genusId, CancellationToken cancellationToken)
    {
        try
        {
            var reply = await GetFromINaturalistAsync(
                new Uri(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{INaturalist}taxa?taxon_id={genusId}&rank=species,hybrid&is_active=true&locale=en-GB&order_by=observations_count&order=desc&per_page=6")),
                cancellationToken);

            return [.. reply.GetProperty("results").EnumerateArray().Where(IsGrowable)];
        }
        catch (Exception exception) when (IsUnavailable(exception) && !cancellationToken.IsCancellationRequested)
        {
            return [];
        }
    }

    /// <summary>A plant's main picture is often restricted when another of its pictures isn't.</summary>
    private async Task<Dictionary<long, FoundPhoto>> FindOtherPhotosAsync(List<long> ids, CancellationToken cancellationToken)
    {
        var photos = new Dictionary<long, FoundPhoto>();

        if (ids.Count == 0)
        {
            return photos;
        }

        try
        {
            var reply = await GetFromINaturalistAsync(
                new Uri($"{INaturalist}taxa/{string.Join(',', ids)}?locale=en-GB"),
                cancellationToken);

            foreach (var taxon in reply.GetProperty("results").EnumerateArray())
            {
                var open = taxon.TryGetProperty("taxon_photos", out var list)
                    ? list.EnumerateArray().Select(item => item.TryGetProperty("photo", out var photo) ? OpenPhoto(photo) : null).FirstOrDefault(photo => photo is not null)
                    : null;

                if (open is not null)
                {
                    photos[taxon.GetProperty("id").GetInt64()] = open;
                }
            }
        }
        catch (Exception exception) when (IsUnavailable(exception) && !cancellationToken.IsCancellationRequested)
        {
            // The plants still show, just without those pictures.
        }

        return photos;
    }

    private async Task<List<FoundSpecies>> SearchGbifAsync(string query, string field, CancellationToken cancellationToken)
    {
        var reply = await http.GetFromJsonAsync<JsonElement>(
            new Uri(string.Create(
                CultureInfo.InvariantCulture,
                $"{Gbif}species/search?q={Uri.EscapeDataString(query)}&qField={field}&datasetKey={GbifBackbone}&highertaxonKey={GbifPlants}&status=ACCEPTED&limit={MostResults}")),
            cancellationToken);

        return [.. reply.GetProperty("results").EnumerateArray()
            .Where(taxon => Ranks.Contains(Text(taxon, "rank") ?? string.Empty) && Text(taxon, "canonicalName") is not null)
            .Select(taxon => new FoundSpecies(
                Text(taxon, "canonicalName")!,
                EnglishName(taxon),
                Text(taxon, "rank")!.ToLowerInvariant(),
                MatchedTerm: null,
                GbifName,
                GbifLicence,
                InaturalistId: null,
                Number(taxon, "nubKey") ?? Number(taxon, "key"),
                Photo: null,
                Popularity: 0))];
    }

    private static string? EnglishName(JsonElement taxon) =>
        taxon.TryGetProperty("vernacularNames", out var names)
            ? names.EnumerateArray()
                .Where(name => string.Equals(Text(name, "language"), "eng", StringComparison.Ordinal))
                .Select(name => Text(name, "vernacularName"))
                .FirstOrDefault()
            : null;
}
