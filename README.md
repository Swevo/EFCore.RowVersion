# Swevo.EFCore.RowVersion

[![NuGet](https://img.shields.io/nuget/v/Swevo.EFCore.RowVersion.svg)](https://www.nuget.org/packages/Swevo.EFCore.RowVersion)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Swevo.EFCore.RowVersion.svg)](https://www.nuget.org/packages/Swevo.EFCore.RowVersion)
[![CI](https://github.com/Swevo/EFCore.RowVersion/actions/workflows/build.yml/badge.svg)](https://github.com/Swevo/EFCore.RowVersion/actions)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Compile-time optimistic concurrency for EF Core. Stamp `[Optimistic]` on any partial entity class and the source generator wires up a `RowVersion` byte array property and the `IOptimisticEntity` interface. Two ready-made retry extensions handle concurrent-write conflicts without any Polly dependency.

## Quick-start

```bash
dotnet add package Swevo.EFCore.RowVersion
```

### 1 — Mark your entities

```csharp
using EFCore.RowVersion;

[Optimistic]
public partial class Order
{
    public int Id { get; set; }
    public string Description { get; set; } = "";
    // byte[] RowVersion { get; set; } is generated automatically
}
```

> The class **must** be `partial`. Non-partial classes produce diagnostic `RVRS001`.

### 2 — Configure `DbContext`

```csharp
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.AddOptimisticConcurrencyConfiguration();
}
```

### 3 — Save with retry

```csharp
// Client wins — re-applies your changes over the updated DB values
await db.SaveChangesClientWinsAsync(maxRetries: 3);

// Database wins — discards your in-memory changes, accepts the DB values
await db.SaveChangesDatabaseWinsAsync(maxRetries: 3);
```

## SQL Server setup

On SQL Server the generated `[Timestamp]` attribute on `RowVersion` is recognised by EF Core convention and automatically configures the property as a database-managed `rowversion` column. No additional code is needed.

```csharp
// For SQL Server, also tell EF Core to use the database-managed rowversion:
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.AddOptimisticConcurrencyConfiguration();
    // Optional – [Timestamp] convention already does this for SQL Server:
    // modelBuilder.Entity<Order>().Property(o => o.RowVersion).IsRowVersion();
}
```

## Non-SQL-Server setup

For SQLite, PostgreSQL, and other providers, override `ValueGeneratedNever()` and update the version manually on each save (e.g. using an interceptor):

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.AddOptimisticConcurrencyConfiguration();
    modelBuilder.Entity<Order>().Property(o => o.RowVersion).ValueGeneratedNever();
}

protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    => optionsBuilder.AddInterceptors(new VersionIncrementInterceptor());

// Interceptor that stamps a new Guid on every save
sealed class VersionIncrementInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData e, InterceptionResult<int> r)
    {
        foreach (var entry in e.Context!.ChangeTracker.Entries<IOptimisticEntity>()
            .Where(x => x.State is EntityState.Added or EntityState.Modified))
            entry.Entity.RowVersion = Guid.NewGuid().ToByteArray();
        return base.SavingChanges(e, r);
    }
}
```

## How it works

| Piece | What it does |
|-------|-------------|
| `[Optimistic]` | Marks the entity for code generation |
| Source generator | Emits `[Timestamp] byte[] RowVersion` + `IOptimisticEntity` implementation |
| `AddOptimisticConcurrencyConfiguration` | Calls `IsConcurrencyToken()` for all `IOptimisticEntity` entities in `OnModelCreating` |
| `SaveChangesClientWinsAsync` | Retries on `DbUpdateConcurrencyException`; refreshes original values from DB, re-applies client values |
| `SaveChangesDatabaseWinsAsync` | Retries on `DbUpdateConcurrencyException`; reloads the entity, discards client changes |

## Stacking with other Swevo packages

```csharp
[Optimistic]  // → RowVersion + optimistic concurrency retry
[Auditable]   // → CreatedAt, UpdatedAt
[SoftDelete]  // → IsDeleted + global query filter
[Tenant]      // → TenantId + per-tenant query filter
public partial class Order
{
    public OrderId Id { get; set; }
}
```

## Requirements

- .NET 8+
- EF Core 8+


## Also by the same author

> 🌐 Full suite overview: **[swevo.github.io](https://swevo.github.io/)**

| Package | Description |
|---|---|
| [**AutoLog.Generator**](https://github.com/Swevo/AutoLog.Generator) | Compile-time high-performance logging — `[Log(Level, Message)]` generates `LoggerMessage.Define`. AOT-safe. |
| [**AutoHttpClient.Generator**](https://github.com/Swevo/AutoHttpClient.Generator) | Compile-time typed HTTP client — `[HttpClient]` on an interface generates a strongly-typed client. AOT-safe Refit alternative. |
| [**AutoDispatch.Generator**](https://github.com/Swevo/AutoDispatch.Generator) | Compile-time CQRS dispatcher — `[Handler]` generates a strongly-typed `IDispatcher`. No MediatR, no reflection. |
| [**AutoWire**](https://github.com/Swevo/AutoWire) | Compile-time DI auto-registration — `[Scoped]`/`[Singleton]`/`[Transient]` generates `IServiceCollection` registration code. |
| [**AutoMap.Generator**](https://github.com/Swevo/AutoMap.Generator) | Compile-time object mapping with generated extension methods. AOT-safe AutoMapper alternative. |

## Related Packages

| Package | Downloads | Description |
|---|---|---|
| [Swevo.EFCore.Outbox](https://www.nuget.org/packages/Swevo.EFCore.Outbox) | [![Downloads](https://img.shields.io/nuget/dt/Swevo.EFCore.Outbox.svg)](https://www.nuget.org/packages/Swevo.EFCore.Outbox) | Transactional outbox pattern for EF Core + AutoBus |
| [Swevo.EFCore.StronglyTyped](https://www.nuget.org/packages/Swevo.EFCore.StronglyTyped) | [![Downloads](https://img.shields.io/nuget/dt/Swevo.EFCore.StronglyTyped.svg)](https://www.nuget.org/packages/Swevo.EFCore.StronglyTyped) | Compile-time strongly-typed ID generation for  |
| [Swevo.EFCore.SoftDelete](https://www.nuget.org/packages/Swevo.EFCore.SoftDelete) | [![Downloads](https://img.shields.io/nuget/dt/Swevo.EFCore.SoftDelete.svg)](https://www.nuget.org/packages/Swevo.EFCore.SoftDelete) | Compile-time soft-delete generation for EF Core entities using Roslyn source generators |
| [Swevo.EFCore.Seeding](https://www.nuget.org/packages/Swevo.EFCore.Seeding) | [![Downloads](https://img.shields.io/nuget/dt/Swevo.EFCore.Seeding.svg)](https://www.nuget.org/packages/Swevo.EFCore.Seeding) | Fluent, idempotent, dependency-ordered seed data for EF Core |
| [Swevo.EFCore.Pagination](https://www.nuget.org/packages/Swevo.EFCore.Pagination) | [![Downloads](https://img.shields.io/nuget/dt/Swevo.EFCore.Pagination.svg)](https://www.nuget.org/packages/Swevo.EFCore.Pagination) | Offset and cursor-based pagination for EF Core |
| [Swevo.EFCore.JsonColumn](https://www.nuget.org/packages/Swevo.EFCore.JsonColumn) | [![Downloads](https://img.shields.io/nuget/dt/Swevo.EFCore.JsonColumn.svg)](https://www.nuget.org/packages/Swevo.EFCore.JsonColumn) | Compile-time JSON column configuration for EF Core 8+ — [JsonColumn] on owned navigation properties generates ConfigureJsonColumns(ModelBuilder) with OwnsOne( |
| [Swevo.EFCore.BulkOperations](https://www.nuget.org/packages/Swevo.EFCore.BulkOperations) | [![Downloads](https://img.shields.io/nuget/dt/Swevo.EFCore.BulkOperations.svg)](https://www.nuget.org/packages/Swevo.EFCore.BulkOperations) | Free, MIT-licensed bulk insert/update/delete for EF Core |
| [Swevo.EFCore.MultiTenant](https://www.nuget.org/packages/Swevo.EFCore.MultiTenant) | [![Downloads](https://img.shields.io/nuget/dt/Swevo.EFCore.MultiTenant.svg)](https://www.nuget.org/packages/Swevo.EFCore.MultiTenant) | Compile-time multi-tenancy for EF Core |

---

## License

MIT
