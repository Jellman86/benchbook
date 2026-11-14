using Hortiplanner.Api.Accounts;
using Hortiplanner.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hortiplanner.Api.Selling;

/// <summary>Trade customers, kept by the owner. Their contact details can be forgotten on request.</summary>
internal static class CustomerEndpoints
{
    public static void MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        var customers = app.MapGroup("/api/customers").RequireAuthorization(AccountServices.OwnerPolicy);
        customers.MapGet("/", ListAsync);
        customers.MapPost("/", AddAsync).Produces<CustomerCreated>(StatusCodes.Status201Created);
        customers.MapPut("/{id:guid}", ChangeAsync);
        customers.MapPost("/{id:guid}/forget", ForgetAsync);
        customers.MapPost("/{id:guid}/archive", (Guid id, HortiplannerDbContext db, CancellationToken cancellationToken) => SetArchivedAsync(id, DateTimeOffset.UtcNow, db, cancellationToken));
        customers.MapPost("/{id:guid}/restore", (Guid id, HortiplannerDbContext db, CancellationToken cancellationToken) => SetArchivedAsync(id, null, db, cancellationToken));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task<List<CustomerResponse>> ListAsync(HortiplannerDbContext db, bool includeArchived = false, CancellationToken cancellationToken = default) =>
        await db.Customers
            .AsNoTracking()
            .Where(customer => includeArchived || customer.ArchivedAt == null)
            .OrderBy(customer => customer.Name)
            .Select(customer => new CustomerResponse(customer.Id, customer.Name, customer.Address, customer.Email, customer.Phone, customer.VatNumber, customer.ArchivedAt != null))
            .ToListAsync(cancellationToken);

    private static async Task<IResult> AddAsync(CustomerRequest request, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        if (Clean(request.Name) is not { } name)
        {
            return TypedResults.BadRequest();
        }

        var customer = new Customer
        {
            Name = name,
            Address = Clean(request.Address),
            Email = Clean(request.Email),
            Phone = Clean(request.Phone),
            VatNumber = Clean(request.VatNumber),
        };

        db.Customers.Add(customer);
        return await SaveAsync(db, TypedResults.Created($"/api/customers/{customer.Id}", new CustomerCreated(customer.Id)), cancellationToken);
    }

    private static async Task<IResult> ChangeAsync(Guid id, CustomerRequest request, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        if (Clean(request.Name) is not { } name)
        {
            return TypedResults.BadRequest();
        }

        if (await db.Customers.FindAsync([id], cancellationToken) is not { } customer)
        {
            return TypedResults.NotFound();
        }

        customer.Name = name;
        customer.Address = Clean(request.Address);
        customer.Email = Clean(request.Email);
        customer.Phone = Clean(request.Phone);
        customer.VatNumber = Clean(request.VatNumber);
        return await SaveAsync(db, TypedResults.NoContent(), cancellationToken);
    }

    /// <summary>Erases contact details under UK GDPR. Invoices already issued keep what they were issued with, as the law requires.</summary>
    private static async Task<IResult> ForgetAsync(Guid id, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Customers.FindAsync([id], cancellationToken) is not { } customer)
        {
            return TypedResults.NotFound();
        }

        customer.Address = null;
        customer.Email = null;
        customer.Phone = null;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> SetArchivedAsync(Guid id, DateTimeOffset? archivedAt, HortiplannerDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Customers.FindAsync([id], cancellationToken) is not { } customer)
        {
            return TypedResults.NotFound();
        }

        customer.ArchivedAt = archivedAt;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> SaveAsync(HortiplannerDbContext db, IResult success, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return success;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return TypedResults.Conflict();
        }
    }

    internal sealed record CustomerRequest(string Name, string? Address, string? Email, string? Phone, string? VatNumber);

    internal sealed record CustomerResponse(Guid Id, string Name, string? Address, string? Email, string? Phone, string? VatNumber, bool Archived);

    internal sealed record CustomerCreated(Guid Id);
}
