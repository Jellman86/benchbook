namespace GrowLog.Api.Data;

/// <summary>How the seed that went into a batch was measured. Only a count gives a number of plants from the start.</summary>
internal enum SownMeasure
{
    /// <summary>Counted as they went in, so the tray starts with that many.</summary>
    Counted,

    /// <summary>About so many seeds: a guess, or what the packet says.</summary>
    About,

    /// <summary>A weight of seed, in grams.</summary>
    Grams,

    /// <summary>A whole or part packet.</summary>
    Packet,

    /// <summary>Nobody knows: a pinch from an unlabelled packet is a valid record.</summary>
    Unknown,
}
