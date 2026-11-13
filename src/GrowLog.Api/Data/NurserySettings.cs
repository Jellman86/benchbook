namespace GrowLog.Api.Data;

/// <summary>Settings for the whole nursery. There is only ever one row.</summary>
internal sealed class NurserySettings
{
    public const int SingleId = 1;

    public int Id { get; init; } = SingleId;

    /// <summary>How much signing in the nursery wants.</summary>
    public SignInLevel SignInLevel { get; set; } = SignInLevel.Personal;

    public string? NurseryName { get; set; }

    public string? Address { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Website { get; set; }

    /// <summary>The plant health registration number, as printed on plant passports.</summary>
    public string? RegistrationNumber { get; set; }

    public string? VatNumber { get; set; }

    /// <summary>Whether the nursery is registered for VAT, so its invoices charge it.</summary>
    public bool VatRegistered { get; set; }

    /// <summary>Days a customer has to pay an invoice.</summary>
    public int PaymentTermsDays { get; set; } = 30;

    /// <summary>The nursery's logo, for labels and invoices. One colour prints best on a thermal printer.</summary>
    public byte[]? Logo { get; set; }

    public string? LogoContentType { get; set; }

    /// <summary>Each kind of label's chosen style, as JSON keyed by label kind.</summary>
    public string LabelStyles { get; set; } = "{}";

    /// <summary>An ArcGIS Location Platform key for satellite pictures behind the places map. Optional.</summary>
    public string? MapImageryKey { get; set; }

    /// <summary>When the nursery's plant health authorisation is next renewed, so the owner is reminded beforehand.</summary>
    public DateOnly? AuthorisationRenewsOn { get; set; }

    /// <summary>Whether places are drawn on the nursery's own map picture rather than the satellite or street map.</summary>
    public bool MapUsesPicture { get; set; }

    /// <summary>The owner's OpenRouter key for Ask GrowLog, encrypted with the server's data protection keys.</summary>
    public string? AssistantKey { get; set; }

    /// <summary>The OpenRouter model Ask GrowLog uses, such as "anthropic/claude-sonnet-4".</summary>
    public string? AssistantModel { get; set; }

    /// <summary>Whether staff can ask too, or only the owner.</summary>
    public bool AssistantForStaff { get; set; }

    /// <summary>The parts of GrowLog the owner has turned off. Everything else is on.</summary>
    public List<NurseryFeature> FeaturesOff { get; set; } = [];
}
