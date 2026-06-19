using a2n.Vista.Examples.Northwind.Entities;
using Microsoft.EntityFrameworkCore;

namespace a2n.Vista.Examples.Northwind.Data;

/// <summary>
/// Creates the SQLite database and seeds a handful of categories, suppliers, and products so the
/// List/Detail facets return real data. Idempotent: seeding only runs when the products table is empty.
/// </summary>
public static class NorthwindSeeder
{
    /// <summary>
    /// Ensures the database exists and is seeded with sample rows.
    /// </summary>
    /// <param name="db">The context to create and seed.</param>
    public static void EnsureSeeded(NorthwindDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        db.Database.EnsureCreated();

        if (db.Products.Any())
        {
            return;
        }

        var beverages = new Category { CategoryId = 1, CategoryName = "Beverages", Description = "Soft drinks, coffees, teas" };
        var condiments = new Category { CategoryId = 2, CategoryName = "Condiments", Description = "Sauces and spreads" };
        var produce = new Category { CategoryId = 3, CategoryName = "Produce", Description = "Dried fruit and bean curd" };

        var exotic = new Supplier { SupplierId = 1, CompanyName = "Exotic Liquids", Country = "UK" };
        var orleans = new Supplier { SupplierId = 2, CompanyName = "New Orleans Cajun Delights", Country = "USA" };
        var tokyo = new Supplier { SupplierId = 3, CompanyName = "Tokyo Traders", Country = "Japan" };

        db.Categories.AddRange(beverages, condiments, produce);
        db.Suppliers.AddRange(exotic, orleans, tokyo);

        db.Products.AddRange(
            new Product { ProductId = 1, ProductName = "Chai", UnitPrice = 18.00m, UnitsInStock = 39, CategoryId = 1, SupplierId = 1 },
            new Product { ProductId = 2, ProductName = "Chang", UnitPrice = 19.00m, UnitsInStock = 17, CategoryId = 1, SupplierId = 1 },
            new Product { ProductId = 3, ProductName = "Aniseed Syrup", UnitPrice = 10.00m, UnitsInStock = 13, CategoryId = 2, SupplierId = 1 },
            new Product { ProductId = 4, ProductName = "Cajun Seasoning", UnitPrice = 22.00m, UnitsInStock = 53, CategoryId = 2, SupplierId = 2 },
            new Product { ProductId = 5, ProductName = "Gumbo Mix", UnitPrice = 21.35m, UnitsInStock = 0, CategoryId = 2, SupplierId = 2, Discontinued = true },
            new Product { ProductId = 6, ProductName = "Tofu", UnitPrice = 23.25m, UnitsInStock = 35, CategoryId = 3, SupplierId = 3 },
            new Product { ProductId = 7, ProductName = "Dried Apples", UnitPrice = 53.00m, UnitsInStock = 42, CategoryId = 3, SupplierId = 3 },
            new Product { ProductId = 8, ProductName = "Green Tea", UnitPrice = 12.50m, UnitsInStock = 100, CategoryId = 1, SupplierId = 3 });

        db.SaveChanges();
    }
}
