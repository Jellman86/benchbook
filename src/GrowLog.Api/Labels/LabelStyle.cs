namespace GrowLog.Api.Labels;

/// <summary>
/// How one kind of label looks: its size and what it shows. The layouts are fixed and tested, so a
/// choice here can never make a label that clips or misprints the way free drawing could.
/// </summary>
internal sealed record LabelStyle(
    LabelLayout Layout,
    int WidthMm,
    int HeightMm,
    bool ShowLogo,
    bool ShowNurseryName,
    bool ShowBotanical,
    bool ShowPrice,
    bool ShowCare)
{
    public const int Smallest = 20;
    public const int Largest = 300;

    public bool IsSensible =>
        WidthMm is >= Smallest and <= Largest && HeightMm is >= Smallest and <= Largest;

    /// <summary>What each label looked like before the nursery chose anything.</summary>
    public static LabelStyle DefaultFor(LabelKind kind) => kind switch
    {
        LabelKind.PlantLabel => new(LabelLayout.Standard, 70, 25, ShowLogo: false, ShowNurseryName: false, ShowBotanical: true, ShowPrice: true, ShowCare: false),
        LabelKind.Invoice => new(LabelLayout.Standard, 210, 297, ShowLogo: true, ShowNurseryName: true, ShowBotanical: false, ShowPrice: true, ShowCare: false),
        LabelKind.BenchCard => new(LabelLayout.Standard, 105, 148, ShowLogo: true, ShowNurseryName: true, ShowBotanical: true, ShowPrice: true, ShowCare: true),
        _ => new(LabelLayout.Standard, 70, 40, ShowLogo: false, ShowNurseryName: false, ShowBotanical: true, ShowPrice: false, ShowCare: false),
    };
}
