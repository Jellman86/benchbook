namespace GrowLog.Api.Printing;

/// <summary>What happened when a label was sent to a printer.</summary>
internal enum PrintOutcome
{
    Sent,

    /// <summary>No such network name.</summary>
    NotFound,

    /// <summary>The address is the server itself or a reserved one, never a printer.</summary>
    NotAllowed,

    /// <summary>Nothing answered at that address and port.</summary>
    Unreachable,
}
