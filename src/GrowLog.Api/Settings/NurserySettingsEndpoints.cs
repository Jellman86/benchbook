using GrowLog.Api.Accounts;
using GrowLog.Api.Data;
using GrowLog.Api.Labels;
using Microsoft.EntityFrameworkCore;

namespace GrowLog.Api.Settings;

/// <summary>The nursery's own details, logo and label styles, set by the owner and used on labels and invoices.</summary>
internal static class NurserySettingsEndpoints
{
    private const int LargestLogo = 2 * 1024 * 1024;
    private const int LongestField = 500;
    private const int MostPaymentDays = 365;

    public static void MapNurserySettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var settings = app.MapGroup("/api/settings");
        settings.MapGet("/nursery", GetDetailsAsync);
        settings.MapPut("/nursery", SaveDetailsAsync).RequireAuthorization(AccountServices.OwnerPolicy);
        settings.MapGet("/nursery/logo", GetLogoAsync);
        settings.MapPut("/nursery/logo", SaveLogoAsync).RequireAuthorization(AccountServices.OwnerPolicy);
        settings.MapDelete("/nursery/logo", RemoveLogoAsync).RequireAuthorization(AccountServices.OwnerPolicy);
        settings.MapGet("/labels", GetLabelStylesAsync);
        settings.MapPut("/labels/{kind}", SaveLabelStyleAsync).RequireAuthorization(AccountServices.OwnerPolicy);
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, LongestField)];

    /// <summary>The single settings row, made when first needed.</summary>
    private static async Task<NurserySettings> RowAsync(GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (await db.NurserySettings.FirstOrDefaultAsync(cancellationToken) is { } settings)
        {
            return settings;
        }

        settings = new NurserySettings();
        db.NurserySettings.Add(settings);
        return settings;
    }

    private static async Task<NurseryDetails> GetDetailsAsync(GrowLogDbContext db, CancellationToken cancellationToken)
    {
        var settings = await db.GetNurserySettingsAsync(cancellationToken);
        return new NurseryDetails(
            settings.NurseryName,
            settings.Address,
            settings.Phone,
            settings.Email,
            settings.Website,
            settings.RegistrationNumber,
            settings.VatNumber,
            settings.Logo is { Length: > 0 },
            settings.VatRegistered,
            settings.PaymentTermsDays);
    }

    private static async Task<IResult> SaveDetailsAsync(NurseryDetails request, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        var settings = await RowAsync(db, cancellationToken);
        settings.NurseryName = Clean(request.Name);
        settings.Address = Clean(request.Address);
        settings.Phone = Clean(request.Phone);
        settings.Email = Clean(request.Email);
        settings.Website = Clean(request.Website);
        settings.RegistrationNumber = Clean(request.RegistrationNumber);
        settings.VatNumber = Clean(request.VatNumber);
        settings.VatRegistered = request.VatRegistered;
        settings.PaymentTermsDays = Math.Clamp(request.PaymentTermsDays, 0, MostPaymentDays);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetLogoAsync(GrowLogDbContext db, CancellationToken cancellationToken)
    {
        var settings = await db.GetNurserySettingsAsync(cancellationToken);
        return settings is { Logo: { Length: > 0 } logo, LogoContentType: { } type }
            ? TypedResults.File(logo, type)
            : TypedResults.NotFound();
    }

    /// <summary>Takes a PNG or JPEG, recognised by its contents rather than what it says it is.</summary>
    private static async Task<IResult> SaveLogoAsync(HttpRequest request, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);

        if (buffer.Length is 0 or > LargestLogo)
        {
            return TypedResults.BadRequest();
        }

        var content = buffer.ToArray();
        var type = content.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 })
            ? "image/png"
            : content.AsSpan().StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }) ? "image/jpeg" : null;

        if (type is null)
        {
            return TypedResults.BadRequest();
        }

        var settings = await RowAsync(db, cancellationToken);
        settings.Logo = content;
        settings.LogoContentType = type;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> RemoveLogoAsync(GrowLogDbContext db, CancellationToken cancellationToken)
    {
        var settings = await RowAsync(db, cancellationToken);
        settings.Logo = null;
        settings.LogoContentType = null;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Dictionary<LabelKind, LabelStyle>> GetLabelStylesAsync(GrowLogDbContext db, CancellationToken cancellationToken) =>
        LabelSettings.Read((await db.GetNurserySettingsAsync(cancellationToken)).LabelStyles);

    private static async Task<IResult> SaveLabelStyleAsync(LabelKind kind, LabelStyle style, GrowLogDbContext db, CancellationToken cancellationToken)
    {
        if (!style.IsSensible)
        {
            return TypedResults.BadRequest();
        }

        var settings = await RowAsync(db, cancellationToken);
        var styles = LabelSettings.Read(settings.LabelStyles);
        styles[kind] = style;
        settings.LabelStyles = LabelSettings.Write(styles);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    internal sealed record NurseryDetails(
        string? Name,
        string? Address,
        string? Phone,
        string? Email,
        string? Website,
        string? RegistrationNumber,
        string? VatNumber,
        bool HasLogo,
        bool VatRegistered,
        int PaymentTermsDays);
}
