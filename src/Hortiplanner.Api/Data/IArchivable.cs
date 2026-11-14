namespace Hortiplanner.Api.Data;

/// <summary>Taken out of use rather than deleted, so older records keep their meaning.</summary>
internal interface IArchivable
{
    DateTimeOffset? ArchivedAt { get; set; }
}
