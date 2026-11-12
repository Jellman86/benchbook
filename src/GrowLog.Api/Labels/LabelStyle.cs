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

    /// <summary>
    /// What each label looks like before the nursery chooses anything. The sizes are stock that is
    /// easy to buy in weatherproof polypropylene, and need only two rolls: a small one for plant and
    /// tray labels, and a 100mm wide one for bench cards and place signs. A desktop label printer
    /// prints about 104mm wide at most, so no default is wider than 100mm.
    /// </summary>
    public static LabelStyle DefaultFor(LabelKind kind) => kind switch
    {
        LabelKind.PlantLabel => new(LabelLayout.Standard, 50, 25, ShowLogo: false, ShowNurseryName: false, ShowBotanical: true, ShowPrice: true, ShowCare: false),
        LabelKind.Invoice => new(LabelLayout.Standard, 210, 297, ShowLogo: true, ShowNurseryName: true, ShowBotanical: false, ShowPrice: true, ShowCare: false),
        LabelKind.BenchCard => new(LabelLayout.Standard, 100, 150, ShowLogo: true, ShowNurseryName: true, ShowBotanical: true, ShowPrice: true, ShowCare: true),
        LabelKind.PlantPassport => new(LabelLayout.Standard, 50, 25, ShowLogo: false, ShowNurseryName: false, ShowBotanical: true, ShowPrice: false, ShowCare: false),
        LabelKind.PlaceLabel => new(LabelLayout.Standard, 100, 150, ShowLogo: false, ShowNurseryName: false, ShowBotanical: true, ShowPrice: false, ShowCare: false),
        _ => new(LabelLayout.Standard, 50, 25, ShowLogo: false, ShowNurseryName: false, ShowBotanical: true, ShowPrice: false, ShowCare: false),
    };
}
