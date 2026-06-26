using EFCore.RowVersion;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Update;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace EFCore.RowVersion.Tests;

// ── Test model ────────────────────────────────────────────────────────────────

[Optimistic]
public partial class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

[Optimistic]
public partial class Category
{
    public int Id { get; set; }
    public string Label { get; set; } = "";
}

public class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddOptimisticConcurrencyConfiguration();
        // [Timestamp] convention sets ValueGeneratedOnAddOrUpdate (SQL Server behaviour).
        // Override to ValueGeneratedNever so our VersionIncrementInterceptor controls the value.
        foreach (var et in modelBuilder.Model.GetEntityTypes()
            .Where(t => typeof(IOptimisticEntity).IsAssignableFrom(t.ClrType)))
        {
            modelBuilder.Entity(et.ClrType)
                .Property(nameof(IOptimisticEntity.RowVersion))
                .ValueGeneratedNever();
        }
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.AddInterceptors(new VersionIncrementInterceptor());
}

/// <summary>
/// Simulates database-managed row versioning for SQLite by updating RowVersion
/// on every Added/Modified entity before saving.
/// </summary>
file sealed class VersionIncrementInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
{
    public override Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> SavingChanges(
        Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
        Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result)
    {
        Stamp(eventData.Context!);
        return base.SavingChanges(eventData, result);
    }

    public override System.Threading.Tasks.ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
        Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
        Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
        System.Threading.CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context!);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Stamp(DbContext ctx)
    {
        foreach (var entry in ctx.ChangeTracker.Entries<IOptimisticEntity>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified))
        {
            entry.Entity.RowVersion = System.Guid.NewGuid().ToByteArray();
        }
    }
}

file static class Helpers
{
    private static int s_idx;

    public static TestDbContext CreateContext(string connString)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connString)
            .Options;
        return new TestDbContext(options);
    }

    public static string NewConnString()
    {
        var name = $"rvtest_{System.Threading.Interlocked.Increment(ref s_idx)}";
        return $"Data Source={name};Mode=Memory;Cache=Shared";
    }
}

// ── Generator output tests ─────────────────────────────────────────────────────

public class GeneratorOutputTests
{
    private static Dictionary<string, string> RunGenerator(string source)
    {
        var refs = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
        };
        try { refs.Add(MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location)); }
        catch { /* best-effort */ }

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new RowVersionGenerator();
        var driver = CSharpGeneratorDriver.Create(generator)
            .RunGenerators(compilation);

        return driver.GetRunResult().GeneratedTrees
            .ToDictionary(
                t => Path.GetFileName(t.FilePath),
                t => t.GetText().ToString());
    }

    private static IReadOnlyList<Diagnostic> GetDiagnostics(string source)
    {
        var refs = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
        };
        try { refs.Add(MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location)); }
        catch { /* best-effort */ }

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new RowVersionGenerator();
        CSharpGeneratorDriver.Create(generator).RunGeneratorsAndUpdateCompilation(
            compilation, out _, out var diagnostics);

        return diagnostics;
    }

    [Fact]
    public void AlwaysEmits_AttributeFile()
        => RunGenerator("").Should().ContainKey("EFCore.RowVersion.Attribute.g.cs");

    [Fact]
    public void AlwaysEmits_InterfaceFile()
        => RunGenerator("").Should().ContainKey("EFCore.RowVersion.Interface.g.cs");

    [Fact]
    public void AlwaysEmits_CoreFile()
        => RunGenerator("").Should().ContainKey("EFCore.RowVersion.Core.g.cs");

    [Fact]
    public void CoreFile_ContainsAddOptimisticConcurrencyConfiguration()
        => RunGenerator("")["EFCore.RowVersion.Core.g.cs"].Should().Contain("AddOptimisticConcurrencyConfiguration");

    [Fact]
    public void CoreFile_ContainsSaveChangesClientWinsAsync()
        => RunGenerator("")["EFCore.RowVersion.Core.g.cs"].Should().Contain("SaveChangesClientWinsAsync");

    [Fact]
    public void CoreFile_ContainsSaveChangesDatabaseWinsAsync()
        => RunGenerator("")["EFCore.RowVersion.Core.g.cs"].Should().Contain("SaveChangesDatabaseWinsAsync");

    [Fact]
    public void PartialClass_GeneratesRowVersionProperty()
    {
        var source = @"
using EFCore.RowVersion;
[Optimistic]
public partial class Order { public int Id { get; set; } }";
        RunGenerator(source)["EFCore.RowVersion.Order.g.cs"]
            .Should().Contain("public byte[] RowVersion { get; set; }");
    }

    [Fact]
    public void PartialClass_HasTimestampAttribute()
    {
        var source = @"
using EFCore.RowVersion;
[Optimistic]
public partial class Order { public int Id { get; set; } }";
        RunGenerator(source)["EFCore.RowVersion.Order.g.cs"]
            .Should().Contain("[System.ComponentModel.DataAnnotations.Timestamp]");
    }

    [Fact]
    public void PartialClass_ImplementsIOptimisticEntity()
    {
        var source = @"
using EFCore.RowVersion;
[Optimistic]
public partial class Order { }";
        RunGenerator(source)["EFCore.RowVersion.Order.g.cs"]
            .Should().Contain(": global::EFCore.RowVersion.IOptimisticEntity");
    }

    [Fact]
    public void NamespacedClass_WrapsInNamespace()
    {
        var source = @"
using EFCore.RowVersion;
namespace MyApp.Domain
{
    [Optimistic]
    public partial class Order { }
}";
        var output = RunGenerator(source)["EFCore.RowVersion.Order.g.cs"];
        output.Should().Contain("namespace MyApp.Domain");
        output.Should().Contain("partial class Order");
    }

    [Fact]
    public void NonPartialClass_ReportsRVRS001()
    {
        var source = @"
using EFCore.RowVersion;
[Optimistic]
public class Order { public int Id { get; set; } }";
        GetDiagnostics(source).Should().ContainSingle(d => d.Id == "RVRS001");
    }

    [Fact]
    public void NonPartialClass_DoesNotGenerateFile()
    {
        var source = @"
using EFCore.RowVersion;
[Optimistic]
public class Order { public int Id { get; set; } }";
        RunGenerator(source).Should().NotContainKey("EFCore.RowVersion.Order.g.cs");
    }

    [Fact]
    public void ValidClass_NoRVRS001Diagnostic()
    {
        var source = @"
using EFCore.RowVersion;
[Optimistic]
public partial class Order { }";
        GetDiagnostics(source).Should().NotContain(d => d.Id == "RVRS001");
    }

    [Fact]
    public void PartialClass_HasAutoGeneratedComment()
    {
        var source = @"
using EFCore.RowVersion;
[Optimistic]
public partial class Order { }";
        RunGenerator(source)["EFCore.RowVersion.Order.g.cs"]
            .Should().Contain("// <auto-generated by Swevo.EFCore.RowVersion/>");
    }
}

// ── Generated type tests ──────────────────────────────────────────────────────

public class GeneratedTypeTests
{
    [Fact]
    public void Product_ImplementsIOptimisticEntity()
        => typeof(Product).Should().Implement<IOptimisticEntity>();

    [Fact]
    public void Product_HasRowVersionProperty()
    {
        var p = new Product();
        p.RowVersion.Should().NotBeNull();
        p.RowVersion.Should().BeEmpty();
    }

    [Fact]
    public void Category_ImplementsIOptimisticEntity()
        => typeof(Category).Should().Implement<IOptimisticEntity>();

    [Fact]
    public void RowVersion_HasTimestampAttribute()
    {
        var prop = typeof(Product).GetProperty(nameof(IOptimisticEntity.RowVersion));
        prop.Should().NotBeNull();
        prop!.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.TimestampAttribute), inherit: false)
             .Should().HaveCount(1);
    }
}

// ── SQLite integration / retry tests ─────────────────────────────────────────

public class ConcurrencyRetryTests : System.IDisposable
{
    // One root context keeps the shared in-memory DB alive for the test's lifetime
    private readonly string _conn = Helpers.NewConnString();
    private readonly TestDbContext _root;

    public ConcurrencyRetryTests()
    {
        _root = Helpers.CreateContext(_conn);
        _root.Database.OpenConnection(); // keep shared in-memory DB alive
        _root.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _root.Database.CloseConnection();
        _root.Dispose();
    }

    [Fact]
    public async Task SaveChangesAsync_SucceedsWithoutConflict()
    {
        using var ctx = Helpers.CreateContext(_conn);
        ctx.Products.Add(new Product { Id = 1, Name = "Widget" });
        var rows = await ctx.SaveChangesClientWinsAsync();
        rows.Should().Be(1);
    }

    [Fact]
    public async Task ClientWins_UpdateSucceeds_WhenNoConflict()
    {
        using var seed = Helpers.CreateContext(_conn);
        seed.Products.Add(new Product { Id = 10, Name = "A" });
        await seed.SaveChangesAsync();

        using var ctx = Helpers.CreateContext(_conn);
        var p = await ctx.Products.FindAsync(10);
        p!.Name = "B";
        var rows = await ctx.SaveChangesClientWinsAsync();
        rows.Should().Be(1);
    }

    [Fact]
    public async Task DatabaseWins_UpdateSucceeds_WhenNoConflict()
    {
        using var seed = Helpers.CreateContext(_conn);
        seed.Products.Add(new Product { Id = 20, Name = "A" });
        await seed.SaveChangesAsync();

        using var ctx = Helpers.CreateContext(_conn);
        var p = await ctx.Products.FindAsync(20);
        p!.Name = "B";
        var rows = await ctx.SaveChangesDatabaseWinsAsync();
        rows.Should().Be(1);
    }

    [Fact]
    public async Task ClientWins_ResolvesConflict_AndSavesClientValue()
    {
        using var seed = Helpers.CreateContext(_conn);
        seed.Products.Add(new Product { Id = 30, Name = "Original" });
        await seed.SaveChangesAsync();

        // Context A loads entity
        using var ctxA = Helpers.CreateContext(_conn);
        var pA = await ctxA.Products.FindAsync(30);
        pA!.Name = "ClientValue";

        // Context B wins the race
        using var ctxB = Helpers.CreateContext(_conn);
        var pB = await ctxB.Products.FindAsync(30);
        pB!.Name = "DatabaseValue";
        await ctxB.SaveChangesAsync();

        // Context A retries; client value should be persisted
        await ctxA.SaveChangesClientWinsAsync(maxRetries: 3);

        using var verify = Helpers.CreateContext(_conn);
        var result = await verify.Products.FindAsync(30);
        result!.Name.Should().Be("ClientValue");
    }

    [Fact]
    public async Task DatabaseWins_ResolvesConflict_AndDiscardsClientValue()
    {
        using var seed = Helpers.CreateContext(_conn);
        seed.Products.Add(new Product { Id = 40, Name = "Original" });
        await seed.SaveChangesAsync();

        // Context A loads entity
        using var ctxA = Helpers.CreateContext(_conn);
        var pA = await ctxA.Products.FindAsync(40);
        pA!.Name = "ClientValue";

        // Context B wins the race
        using var ctxB = Helpers.CreateContext(_conn);
        var pB = await ctxB.Products.FindAsync(40);
        pB!.Name = "DatabaseValue";
        await ctxB.SaveChangesAsync();

        // Context A retries; database value should win
        await ctxA.SaveChangesDatabaseWinsAsync(maxRetries: 3);

        using var verify = Helpers.CreateContext(_conn);
        var result = await verify.Products.FindAsync(40);
        result!.Name.Should().Be("DatabaseValue");
    }

    [Fact]
    public async Task ClientWins_ThrowsAfterMaxRetries_WhenAlwaysConflicting()
    {
        // Use a DbContext subclass whose SaveChangesAsync always throws
        using var ctx = new AlwaysConflictingDbContext(
            new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlite(_conn)
                .Options);

        var act = () => ctx.SaveChangesClientWinsAsync(maxRetries: 2);
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }
}

/// <summary>DbContext whose <c>SaveChangesAsync</c> always throws <see cref="DbUpdateConcurrencyException"/>.</summary>
file sealed class AlwaysConflictingDbContext(DbContextOptions<TestDbContext> options) : TestDbContext(options)
{
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => throw new DbUpdateConcurrencyException("Simulated perpetual conflict",
               new List<IUpdateEntry>());
}
