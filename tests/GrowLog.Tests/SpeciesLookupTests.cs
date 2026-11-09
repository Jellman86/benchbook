using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;
using static GrowLog.Tests.AccountHarness;

namespace GrowLog.Tests;

public sealed class SpeciesLookupTests(PostgresFixture database)
{
    private const string PhotoHost = "https://inaturalist-open-data.s3.amazonaws.com/photos/";

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

    /// <summary>Stands in for iNaturalist and GBIF, and counts what was asked of them.</summary>
    private sealed class OutsideWorld : HttpMessageHandler
    {
        private readonly List<string> requests = [];

        public string Autocomplete { get; set; } = """{"results":[]}""";

        public string Taxa { get; set; } = """{"results":[]}""";

        public string Children { get; set; } = """{"results":[]}""";

        public string GbifSearch { get; set; } = """{"results":[]}""";

        public string GbifMatch { get; set; } = """{"matchType":"NONE"}""";

        public bool INaturalistDown { get; set; }

        public bool Down { get; set; }

        public int Count(string part)
        {
            lock (requests)
            {
                return requests.Count(request => request.Contains(part, StringComparison.Ordinal));
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var address = request.RequestUri!.ToString();

            lock (requests)
            {
                requests.Add(address);
            }

            if (Down || (INaturalistDown && address.Contains("inaturalist.org", StringComparison.Ordinal)))
            {
                throw new HttpRequestException("Unreachable.");
            }

            if (address.Contains("/photos/", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Jpeg) });
            }

            var body = address switch
            {
                _ when address.Contains("taxa/autocomplete", StringComparison.Ordinal) => Autocomplete,
                _ when address.Contains("/taxa/", StringComparison.Ordinal) => Taxa,
                _ when address.Contains("taxa?taxon_id=", StringComparison.Ordinal) => Children,
                _ when address.Contains("species/search", StringComparison.Ordinal) => GbifSearch,
                _ => GbifMatch,
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static string Taxon(long id, string name, string? common, string licence, string rank = "species", string? matched = null, int observations = 0) => $$"""
        {
          "id": {{id}}, "name": "{{name}}", "rank": "{{rank}}", "observations_count": {{observations}},
          "preferred_common_name": {{(common is null ? "null" : $"\"{common}\"")}},
          "matched_term": "{{matched ?? name}}",
          "default_photo": { "license_code": {{(string.Equals(licence, "none", StringComparison.Ordinal) ? "null" : $"\"{licence}\"")}}, "attribution": "(c) A Photographer", "medium_url": "{{PhotoHost}}{{id}}/medium.jpeg" }
        }
        """;

    private static async Task<TestApp> CreateAsync(PostgresFixture database, OutsideWorld world)
    {
        var connectionString = await database.CreateDatabaseAsync();

        return new TestApp(
            ("ConnectionStrings:GrowLog", connectionString),
            ("Passkeys:ServerDomain", "localhost"),
            ("Setup:Token", SetupToken),
            ("Demo:Enabled", "false"))
        {
            OutsideWorld = world,
        };
    }

    private static async Task<JsonElement> SearchAsync(HttpClient client, string query) =>
        await client.GetFromJsonAsync<JsonElement>(Route($"/api/species/search?q={Uri.EscapeDataString(query)}"), Cancellation);

    private static List<JsonElement> Species(JsonElement response) => [.. response.GetProperty("species").EnumerateArray()];

    private static string? Name(JsonElement species) => species.GetProperty("scientificName").GetString();

    [Fact]
    public async Task A_search_shows_common_and_scientific_names_with_a_credited_picture()
    {
        var world = new OutsideWorld { Autocomplete = $$"""{"results":[{{Taxon(1, "Lavandula angustifolia", "Common Lavender", "cc-by")}}]}""" };
        await using var app = await CreateAsync(database, world);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);

        var found = Assert.Single(Species(await SearchAsync(owner, "lavender")));

        Assert.Equal("Common Lavender", found.GetProperty("commonName").GetString());
        Assert.Equal("Lavandula angustifolia", Name(found));
        Assert.True(found.GetProperty("hasPhoto").GetBoolean());
        Assert.Equal("(c) A Photographer", found.GetProperty("photoCredit").GetString());
    }

    [Fact]
    public async Task A_genus_brings_its_best_known_species_and_the_most_recorded_plants_come_first()
    {
        var world = new OutsideWorld
        {
            Autocomplete = $$"""{"results":[{{Taxon(1, "Croton gratissimus", "Lavender Feverberry", "cc-by", observations: 50)}},{{Taxon(2, "Lavandula", "Lavenders", "cc-by", rank: "genus", observations: 900)}}]}""",
            Children = $$"""{"results":[{{Taxon(3, "Lavandula angustifolia", "Common Lavender", "cc-by", observations: 500)}},{{Taxon(4, "Lavandula stoechas", "Topped Spanish Bells", "cc-by", observations: 400)}}]}""",
        };
        await using var app = await CreateAsync(database, world);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);

        var names = Species(await SearchAsync(owner, "lavender")).Select(Name);

        // The last is found through its genus, so it follows the plants actually called lavender.
        Assert.Equal(["Lavandula", "Lavandula angustifolia", "Croton gratissimus", "Lavandula stoechas"], names, StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_repeated_search_is_answered_without_going_online()
    {
        var world = new OutsideWorld { Autocomplete = $$"""{"results":[{{Taxon(1, "Lavandula angustifolia", "Common Lavender", "cc-by")}}]}""" };
        await using var app = await CreateAsync(database, world);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);

        await SearchAsync(owner, "lavender");
        var second = await SearchAsync(owner, "Lavender ");

        Assert.Single(Species(second));
        Assert.Equal(1, world.Count("taxa/autocomplete"));
    }

    [Fact]
    public async Task Saved_species_are_found_by_a_later_search_when_nothing_online_answers()
    {
        var world = new OutsideWorld { Autocomplete = $$"""{"results":[{{Taxon(1, "Lavandula angustifolia", "Common Lavender", "cc-by", matched: "English lavender")}}]}""" };
        await using var app = await CreateAsync(database, world);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        await SearchAsync(owner, "lavender");

        world.Down = true;
        var response = await SearchAsync(owner, "english lav");

        Assert.Equal("Lavandula angustifolia", Name(Assert.Single(Species(response))));
        Assert.True(response.GetProperty("onlineUnavailable").GetBoolean());
    }

    [Fact]
    public async Task A_restricted_picture_is_never_kept_and_another_open_one_is_used_instead()
    {
        var world = new OutsideWorld
        {
            Autocomplete = $$"""{"results":[{{Taxon(1, "Digitalis purpurea", "Purple Foxglove", "cc-by-nc")}},{{Taxon(2, "Lathyrus odoratus", "Sweet Pea", "none", matched: "Purple pea")}}]}""",
            Taxa = $$$"""
                {"results":[{"id":1,"taxon_photos":[
                  {"photo":{"license_code":"cc-by-nc","attribution":"restricted","medium_url":"{{{PhotoHost}}}9/medium.jpeg"}},
                  {"photo":{"license_code":"cc0","attribution":"no rights reserved","medium_url":"{{{PhotoHost}}}8/medium.jpeg"}}
                ]}]}
                """,
        };
        await using var app = await CreateAsync(database, world);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);

        var found = Species(await SearchAsync(owner, "purple")).ToDictionary(item => Name(item)!, StringComparer.Ordinal);

        Assert.Equal("no rights reserved", found["Digitalis purpurea"].GetProperty("photoCredit").GetString());
        Assert.False(found["Lathyrus odoratus"].GetProperty("hasPhoto").GetBoolean());
    }

    [Fact]
    public async Task Ranks_that_are_not_plants_someone_grows_are_left_out()
    {
        var world = new OutsideWorld { Autocomplete = $$"""{"results":[{{Taxon(1, "Primulaceae", "primrose family", "cc-by", rank: "family")}},{{Taxon(2, "Primula veris", "Cowslip", "cc-by")}}]}""" };
        await using var app = await CreateAsync(database, world);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);

        Assert.Equal("Primula veris", Name(Assert.Single(Species(await SearchAsync(owner, "primula")))));
    }

    [Fact]
    public async Task GBIF_answers_when_iNaturalist_is_down()
    {
        var world = new OutsideWorld
        {
            INaturalistDown = true,
            GbifSearch = """{"results":[{"key":5,"canonicalName":"Digitalis purpurea","rank":"SPECIES","vernacularNames":[{"language":"deu","vernacularName":"Fingerhut"},{"language":"eng","vernacularName":"Foxglove"}]}]}""",
        };
        await using var app = await CreateAsync(database, world);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);

        var response = await SearchAsync(owner, "foxglove");
        var found = Assert.Single(Species(response));

        Assert.Equal("Foxglove", found.GetProperty("commonName").GetString());
        Assert.False(found.GetProperty("hasPhoto").GetBoolean());
        Assert.False(response.GetProperty("onlineUnavailable").GetBoolean());
    }

    [Fact]
    public async Task Search_still_finds_the_nurserys_own_plants_with_no_connection_and_treats_wildcards_literally()
    {
        var world = new OutsideWorld { Down = true };
        await using var app = await CreateAsync(database, world);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        await PostStatusAsync(owner, "/api/plants", new { commonName = "Lavender", variety = "Hidcote", xylellaHost = false, protectedVariety = false });

        var response = await SearchAsync(owner, "hidc");

        Assert.Equal("Lavender", Assert.Single(response.GetProperty("plants").EnumerateArray()).GetProperty("commonName").GetString());
        Assert.True(response.GetProperty("onlineUnavailable").GetBoolean());
        Assert.Empty((await SearchAsync(owner, "%%%")).GetProperty("plants").EnumerateArray());
    }

    [Fact]
    public async Task A_short_search_goes_nowhere()
    {
        var world = new OutsideWorld();
        await using var app = await CreateAsync(database, world);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);

        await SearchAsync(owner, "la");

        Assert.Equal(0, world.Count("taxa/autocomplete"));
    }

    [Fact]
    public async Task A_picture_is_downloaded_once()
    {
        var world = new OutsideWorld { Autocomplete = $$"""{"results":[{{Taxon(1, "Lavandula angustifolia", "Common Lavender", "cc-by")}}]}""" };
        await using var app = await CreateAsync(database, world);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        var id = Species(await SearchAsync(owner, "lavender")).Single().GetProperty("id").GetGuid();

        using var first = await owner.GetAsync(Route($"/api/species/{id}/photo"), Cancellation);
        using var second = await owner.GetAsync(Route($"/api/species/{id}/photo"), Cancellation);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("image/jpeg", second.Content.Headers.ContentType!.MediaType);
        Assert.Equal(1, world.Count("/photos/"));
    }

    [Fact]
    public async Task A_picture_address_outside_the_known_hosts_is_not_followed()
    {
        var world = new OutsideWorld
        {
            Autocomplete = """
                {"results":[{"id":1,"name":"Lavandula angustifolia","rank":"species","preferred_common_name":"Common Lavender",
                "default_photo":{"license_code":"cc-by","attribution":"x","medium_url":"https://intranet.example/photos/secret.jpeg"}}]}
                """,
        };
        await using var app = await CreateAsync(database, world);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        var id = Species(await SearchAsync(owner, "lavender")).Single().GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, await GetStatusAsync(owner, $"/api/species/{id}/photo"));
        Assert.Equal(0, world.Count("intranet"));
    }

    [Fact]
    public async Task Picking_a_species_adds_the_plant_with_the_accepted_name_and_reuses_it_after()
    {
        var world = new OutsideWorld
        {
            Autocomplete = $$"""{"results":[{{Taxon(1, "Lavandula officinalis", "common lavender", "cc-by")}}]}""",
            GbifMatch = """{"matchType":"EXACT","status":"SYNONYM","rank":"SPECIES","species":"Lavandula angustifolia","usageKey":1,"acceptedUsageKey":2}""",
        };
        await using var app = await CreateAsync(database, world);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        var speciesId = Species(await SearchAsync(owner, "lavender")).Single().GetProperty("id").GetGuid();

        var first = Json(await PostForTextAsync(owner, "/api/plants/from-species", new { speciesId, variety = "Hidcote" })).GetProperty("id").GetGuid();
        var again = Json(await PostForTextAsync(owner, "/api/plants/from-species", new { speciesId, variety = "Hidcote" })).GetProperty("id").GetGuid();

        Assert.Equal(first, again);
        Assert.Equal(1, world.Count("species/match"));

        var plant = Assert.Single((await owner.GetFromJsonAsync<JsonElement>(Route("/api/plants"), Cancellation)).EnumerateArray());
        Assert.Equal("Common lavender", plant.GetProperty("commonName").GetString());
        Assert.Equal("Hidcote", plant.GetProperty("variety").GetString());
        Assert.Equal("Lavandula angustifolia", plant.GetProperty("botanicalName").GetString());
        Assert.True(plant.GetProperty("hasPhoto").GetBoolean());
    }

    [Fact]
    public async Task A_plant_is_still_added_when_the_name_check_cannot_be_done()
    {
        var world = new OutsideWorld { Autocomplete = $$"""{"results":[{{Taxon(1, "Lavandula angustifolia", "Common Lavender", "cc-by")}}]}""" };
        await using var app = await CreateAsync(database, world);
        using var owner = Client(app);
        using var key = new TestAuthenticator();
        await SetUpOwnerAsync(owner, key);
        var speciesId = Species(await SearchAsync(owner, "lavender")).Single().GetProperty("id").GetGuid();

        world.Down = true;

        Assert.Equal(HttpStatusCode.OK, await PostStatusAsync(owner, "/api/plants/from-species", new { speciesId, variety = (string?)null }));
    }

    [Fact]
    public async Task Search_needs_someone_signed_in()
    {
        var world = new OutsideWorld();
        await using var app = await CreateAsync(database, world);
        using var client = Client(app);

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(client, "/api/species/search?q=lavender"));
        Assert.Equal(0, world.Count("taxa"));
    }
}
