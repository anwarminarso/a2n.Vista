using a2n.Vista.Examples.Northwind.Entities;
using Microsoft.EntityFrameworkCore;

namespace a2n.Vista.Examples.Northwind.Data;

/// <summary>
/// The application's EF Core context for the Northwind sample. Backed by SQLite (a local file) and
/// exposes the three source entities the <c>vProductCategory</c> view projects from.
/// </summary>
/// <remarks>
/// The Vista EF executor resolves this captured context type at request time (recorded by
/// <c>RegisterTemplate&lt;NorthwindViews, NorthwindDbContext&gt;</c>) and obtains each view's source
/// set via the <c>Set&lt;TSource&gt;()</c> convention.
/// </remarks>
public class NorthwindDbContext : DbContext
{
    /// <summary>Creates the context with the supplied options (configured for SQLite at startup).</summary>
    public NorthwindDbContext(DbContextOptions<NorthwindDbContext> options)
        : base(options)
    {
    }

    /// <summary>The products table.</summary>
    public DbSet<Product> Products => Set<Product>();

    /// <summary>The categories table.</summary>
    public DbSet<Category> Categories => Set<Category>();

    /// <summary>The suppliers table.</summary>
    public DbSet<Supplier> Suppliers => Set<Supplier>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Category>(e =>
        {
            e.HasKey(c => c.CategoryId);
            e.Property(c => c.CategoryId).ValueGeneratedNever();
            e.Property(c => c.CategoryName).HasMaxLength(64).IsRequired();
        });

        modelBuilder.Entity<Supplier>(e =>
        {
            e.HasKey(s => s.SupplierId);
            e.Property(s => s.SupplierId).ValueGeneratedNever();
            e.Property(s => s.CompanyName).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<Product>(e =>
        {
            e.HasKey(p => p.ProductId);
            e.Property(p => p.ProductId).ValueGeneratedNever();
            e.Property(p => p.ProductName).HasMaxLength(128).IsRequired();
            e.Property(p => p.UnitPrice).HasColumnType("decimal(18,2)");

            e.HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId);

            e.HasOne(p => p.Supplier)
                .WithMany(s => s.Products)
                .HasForeignKey(p => p.SupplierId);
        });
    }
}
