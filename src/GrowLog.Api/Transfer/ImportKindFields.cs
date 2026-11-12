namespace GrowLog.Api.Transfer;

internal sealed record ImportKindFields(ImportKind Kind, IReadOnlyList<ImportField> Fields);
