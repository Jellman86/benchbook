namespace GrowLog.Api.Labels;

internal enum LabelKind
{
    ContainerLabel,
    PlantLabel,
    BenchCard,
    PlaceLabel,

    /// <summary>A UK plant passport the nursery issued: its own label, kept apart from everything else.</summary>
    PlantPassport,

    /// <summary>An A4 invoice or credit note, printed the same way as labels.</summary>
    Invoice,
}
