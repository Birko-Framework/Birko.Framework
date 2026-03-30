using System;
using Birko.Models.Product;
using Birko.Models.Category;
using Birko.Models.Pricing;
using Birko.Models.Inventory;
using ProductModel = Birko.Models.Product.Product;
using ProductViewModel = Birko.Models.Product.ViewModels.Product;
using CategoryModel = Birko.Models.Category.Category;
using CategoryViewModel = Birko.Models.Category.ViewModels.Category;

namespace Birko.Framework.Examples.Models
{
    /// <summary>
    /// Examples demonstrating Birko.Models domain model types:
    /// Product, Category, Accounting (Currency, Tax), and Inventory (StockItem, StockItemVariant).
    /// </summary>
    public static class ProductModelsExamples
    {
        /// <summary>
        /// Create Product models and show their properties.
        /// Product extends AbstractLogModel (provides Guid, CreatedAt, UpdatedAt).
        /// </summary>
        public static void RunProductExample()
        {
            ExampleOutput.WriteLine("=== Product Models Example ===\n");

            // Product model: SKUCode, BarCode, Name, Slug, Description, Category (string)
            var product = new ProductModel
            {
                SKUCode = "WM-001",
                BarCode = "8901234567890",
                Name = "Wireless Mouse",
                Slug = "wireless-mouse",
                Description = "Ergonomic wireless mouse with USB receiver",
                Category = "Accessories"
            };

            ExampleOutput.WriteLine($"Product: {product.Name}");
            ExampleOutput.WriteLine($"  SKU: {product.SKUCode}");
            ExampleOutput.WriteLine($"  Barcode: {product.BarCode}");
            ExampleOutput.WriteLine($"  Slug: {product.Slug}");
            ExampleOutput.WriteLine($"  Category: {product.Category}");
            ExampleOutput.WriteLine($"  Guid: {product.Guid}");
            ExampleOutput.WriteLine($"  Created: {product.CreatedAt}");

            // Product ViewModel: same properties, extends LogViewModel (adds MVVM notifications)
            var productVM = new ProductViewModel();
            productVM.LoadFrom(product); // Load model data into ViewModel

            ExampleOutput.WriteLine($"\nViewModel loaded from model:");
            ExampleOutput.WriteLine($"  Name: {productVM.Name}");
            ExampleOutput.WriteLine($"  SKU: {productVM.SKUCode}");
            ExampleOutput.WriteLine($"  Slug: {productVM.Slug}");
            ExampleOutput.WriteLine($"  Description: {productVM.Description}");

            // ViewModel-to-ViewModel loading
            var anotherVM = new ProductViewModel();
            anotherVM.LoadFrom(productVM);
            ExampleOutput.WriteLine($"\nViewModel-to-ViewModel copy: {anotherVM.Name}");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// Create Category models and demonstrate hierarchies.
        /// Category has Title, Path, Description (not Name/Slug as some frameworks).
        /// </summary>
        public static void RunCategoryExample()
        {
            ExampleOutput.WriteLine("=== Category Models Example ===\n");

            // Category model: Title, Path, Description
            var electronics = new CategoryModel
            {
                Title = "Electronics",
                Path = "/electronics",
                Description = "Electronic devices and accessories"
            };

            var computers = new CategoryModel
            {
                Title = "Computers",
                Path = "/electronics/computers",
                Description = "Desktop and laptop computers"
            };

            var accessories = new CategoryModel
            {
                Title = "Accessories",
                Path = "/electronics/computers/accessories",
                Description = "Computer peripherals and accessories"
            };

            ExampleOutput.WriteLine("Category Hierarchy (using Path):");
            ExampleOutput.WriteLine($"  {electronics.Title} ({electronics.Path})");
            ExampleOutput.WriteLine($"    {computers.Title} ({computers.Path})");
            ExampleOutput.WriteLine($"      {accessories.Title} ({accessories.Path})");

            // Category ViewModel with MVVM property change notifications
            var categoryVM = new CategoryViewModel();
            categoryVM.LoadFrom(electronics);
            ExampleOutput.WriteLine($"\nViewModel: Title={categoryVM.Title}, Path={categoryVM.Path}");
            ExampleOutput.WriteLine($"  Guid: {categoryVM.Guid}");

            // IRelatedToCategory interface: models that belong to a category have CategoryGuid
            ExampleOutput.WriteLine("\nIRelatedToCategory interface:");
            ExampleOutput.WriteLine("  Implemented by models that reference a category (e.g., Inventory.StockItem)");
            ExampleOutput.WriteLine("  Provides CategoryGuid property for linking.");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// Show Currency and Tax accounting models.
        /// Currency has Name, Symbol, From/To exchange rates.
        /// Tax extends AbstractPercentage (has Percentage property).
        /// </summary>
        public static void RunPricingExample()
        {
            ExampleOutput.WriteLine("=== Pricing Models Example ===\n");

            // Currency: Code, Name, Symbol, IsLeftSymbol, IsDefault
            var eur = new Currency
            {
                Code = "EUR",
                Name = "Euro",
                Symbol = "€",
                IsLeftSymbol = false,   // 100 €
                IsDefault = true
            };

            var usd = new Currency
            {
                Code = "USD",
                Name = "US Dollar",
                Symbol = "$",
                IsLeftSymbol = true     // $100
            };

            ExampleOutput.WriteLine("Currencies:");
            ExampleOutput.WriteLine($"  {eur.Name} ({eur.Code}) {eur.Symbol} - Default: {eur.IsDefault}, LeftSymbol: {eur.IsLeftSymbol}");
            ExampleOutput.WriteLine($"  {usd.Name} ({usd.Code}) {usd.Symbol} - Default: {usd.IsDefault}, LeftSymbol: {usd.IsLeftSymbol}");

            // Tax: Name, ShortCut, Percentage, IsDefault
            var standardTax = new Tax
            {
                Name = "Standard VAT",
                ShortCut = "20%",
                Percentage = 20.0m,
                IsDefault = true
            };

            var reducedTax = new Tax
            {
                Name = "Reduced VAT",
                ShortCut = "10%",
                Percentage = 10.0m,
                IsDefault = false
            };

            ExampleOutput.WriteLine("\nTax Rates:");
            ExampleOutput.WriteLine($"  {standardTax.Name} ({standardTax.ShortCut}): {standardTax.Percentage}% - Default: {standardTax.IsDefault}");
            ExampleOutput.WriteLine($"  {reducedTax.Name} ({reducedTax.ShortCut}): {reducedTax.Percentage}% - Default: {reducedTax.IsDefault}");

            // Price calculation
            decimal basePrice = 199.99m;
            decimal taxAmount = basePrice * standardTax.Percentage / 100m;
            decimal totalInEur = basePrice + taxAmount;
            decimal eurToUsdRate = 1.08m;
            decimal totalInUsd = totalInEur * eurToUsdRate;

            ExampleOutput.WriteLine($"\nPrice calculation:");
            ExampleOutput.WriteLine($"  Base price: {basePrice:F2} {eur.Symbol}");
            ExampleOutput.WriteLine($"  Tax ({standardTax.Percentage}%): {taxAmount:F2} {eur.Symbol}");
            ExampleOutput.WriteLine($"  Total: {totalInEur:F2} {eur.Symbol} = {(usd.IsLeftSymbol ? usd.Symbol : "")}{totalInUsd:F2}{(usd.IsLeftSymbol ? "" : " " + usd.Symbol)}");

            // Tax supports ICopyable<Tax> for cloning
            var clonedTax = standardTax.CopyTo(null!);
            ExampleOutput.WriteLine($"\nCloned tax: {clonedTax.Name} ({clonedTax.Percentage}%)");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// Show Inventory StockItem and StockItemVariant models.
        /// StockItem has Code, BarCode, Name, ShortName, Description, Type.
        /// StockItemVariant has Name and references a StockItem via StockItemGuid.
        /// </summary>
        public static void RunInventoryExample()
        {
            ExampleOutput.WriteLine("=== Inventory Example ===\n");

            // StockItem: extends AbstractLogModel, implements ICatalogItem, ICategorizeable
            var item = new StockItem
            {
                Code = "LAPTOP-001",
                BarCode = "5901234567890",
                Name = "Business Laptop 15\"",
                ShortName = "Laptop 15",
                Description = "15-inch business laptop with Intel i7",
                Type = "Product"
            };

            ExampleOutput.WriteLine($"StockItem: {item.Name}");
            ExampleOutput.WriteLine($"  Code: {item.Code}");
            ExampleOutput.WriteLine($"  Barcode: {item.BarCode}");
            ExampleOutput.WriteLine($"  Short name: {item.ShortName}");
            ExampleOutput.WriteLine($"  Type: {item.Type}");
            ExampleOutput.WriteLine($"  Guid: {item.Guid}");

            // StockItemVariant: references parent StockItem via StockItemGuid
            var variant8gb = new StockItemVariant
            {
                Name = "8GB RAM / 256GB SSD",
                StockItemGuid = item.Guid
            };

            var variant16gb = new StockItemVariant
            {
                Name = "16GB RAM / 512GB SSD",
                StockItemGuid = item.Guid
            };

            ExampleOutput.WriteLine($"\nVariants for '{item.ShortName}':");
            ExampleOutput.WriteLine($"  - {variant8gb.Name} (StockItemGuid: {variant8gb.StockItemGuid})");
            ExampleOutput.WriteLine($"  - {variant16gb.Name} (StockItemGuid: {variant16gb.StockItemGuid})");

            // StockItem supports ICopyable<StockItem>
            var clonedItem = item.CopyTo(null!);
            clonedItem.Code = "LAPTOP-002";
            clonedItem.Name = "Business Laptop 17\"";
            ExampleOutput.WriteLine($"\nCloned item: {clonedItem.Name} (Code: {clonedItem.Code})");

            // StockItem contracts: ICatalogItem, ICategorizeable
            ExampleOutput.WriteLine("\nStockItem contracts:");
            ExampleOutput.WriteLine("  ICatalogItem   -> Name, Code, BarCode, Description");
            ExampleOutput.WriteLine("  ICategorizeable -> CategoryGuid");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }
    }
}
