using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GrowLog.Api.Data;

/// <summary>Records every catalogue change in the same transaction as the change itself.</summary>
internal sealed class CatalogueAuditInterceptor(IHttpContextAccessor httpContextAccessor) : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Record(eventData.Context!);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Record(eventData.Context!);
        return ValueTask.FromResult(result);
    }

    private void Record(DbContext context)
    {
        context.ChangeTracker.DetectChanges();

        var entries = context.ChangeTracker.Entries<IAudited>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        if (entries.Count == 0)
        {
            return;
        }

        var userId = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("The catalogue can only be changed by a signed-in person.");

        foreach (var entry in entries)
        {
            context.Add(new CatalogueChange
            {
                Entity = entry.Metadata.ClrType.Name,
                EntityKey = string.Join('/', entry.Metadata.FindPrimaryKey()!.Properties.Select(key => entry.Property(key.Name).CurrentValue)),
                Action = entry.State.ToString(),
                ChangedBy = Guid.Parse(userId),
                Changes = Describe(entry),
            });
        }
    }

    private static string Describe(EntityEntry entry)
    {
        var changes = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var property in entry.Properties)
        {
            if (property.Metadata.IsPrimaryKey() || (entry.State == EntityState.Modified && !property.IsModified))
            {
                continue;
            }

            changes[property.Metadata.Name] = property.Metadata.PropertyInfo?.GetCustomAttribute<PersonalDataAttribute>() is not null
                ? "personal data not kept"
                : new
                {
                    before = entry.State == EntityState.Added ? null : property.OriginalValue,
                    after = entry.State == EntityState.Deleted ? null : property.CurrentValue,
                };
        }

        return JsonSerializer.Serialize(changes, JsonOptions);
    }
}
