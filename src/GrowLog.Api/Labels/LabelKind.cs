namespace GrowLog.Api.Labels;

internal enum LabelKind
{
    ContainerLabel,
    PlantLabel,
    BenchCard,
    PlaceLabel,

    /// <summary>An A4 invoice or credit note, printed the same way as labels.</summary>
    Invoice,
}
