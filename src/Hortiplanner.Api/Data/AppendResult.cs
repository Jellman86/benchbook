namespace Hortiplanner.Api.Data;

internal enum AppendResult
{
    Stored,

    /// <summary>An event with this identifier exists, so an offline client can safely resend.</summary>
    AlreadyStored,

    /// <summary>Nothing was stored. The person needs to make a correction or count the stock.</summary>
    BelowZero,

    /// <summary>Nothing was stored: this identifier already belongs to different work.</summary>
    IdInUse,

    /// <summary>Nothing was stored: a stock count named a batch that has never been in that container.</summary>
    NotInContainer,

    /// <summary>Nothing was stored: the work is dated before the container was started.</summary>
    BeforeStart,
}
