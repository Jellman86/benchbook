namespace GrowLog.Api.Data;

/// <summary>
/// Parts of GrowLog a nursery may not use. The owner turns them off under Settings, Features, which hides them
/// everywhere and refuses their requests; their records are kept, so turning one back on brings it all back.
/// </summary>
internal enum NurseryFeature
{
    /// <summary>Sensors sending readings in, and each place's climate.</summary>
    Climate,

    /// <summary>The map of the nursery's places and its satellite pictures.</summary>
    PlacesMap,

    /// <summary>Issued and received plant passports, consignments and the authorisation renewal reminder.</summary>
    Passports,

    /// <summary>Mother plants and the cuttings taken from them.</summary>
    MotherPlants,

    /// <summary>Repeating jobs such as watering and spraying rotas.</summary>
    Jobs,

    /// <summary>Trade invoices and credit notes.</summary>
    Invoices,

    /// <summary>Purchases from suppliers and what is owed on them.</summary>
    Purchases,

    /// <summary>The strip of photos on each batch.</summary>
    BatchPhotos,
}
