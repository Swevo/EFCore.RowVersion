# Swevo.EFCore.RowVersion

[![NuGet](https://img.shields.io/nuget/v/Swevo.EFCore.RowVersion
[![NuGet Downloads](https://img.shields.io/nuget/dt/Swevo.EFCore.RowVersion.svg)](https://www.nuget.org/packages/Swevo.EFCore.RowVersion).svg)](https://www.nuget.org/packages/Swevo.EFCore.RowVersion)
[![CI](https://github.com/Swevo/EFCore.RowVersion/actions/workflows/build.yml/badge.svg)](https://github.com/Swevo/EFCore.RowVersion/actions)

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

## License

MIT
