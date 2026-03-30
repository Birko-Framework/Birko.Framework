using System;
using System.Threading.Tasks;
using Birko.Validation;
using Birko.Validation.Fluent;

namespace Birko.Framework.Examples.Validation
{
    /// <summary>
    /// Example product model for validation demonstrations.
    /// </summary>
    public class ExampleValidationProduct
    {
        public string? Name { get; set; }
        public string? SKU { get; set; }
        public decimal Price { get; set; }
        public int StockQuantity { get; set; }
        public string? Email { get; set; }
        public string? Description { get; set; }
    }

    /// <summary>
    /// Example validator using Birko.Validation fluent API.
    /// Define rules in the constructor using RuleFor().
    /// </summary>
    public class ProductValidator : AbstractValidator<ExampleValidationProduct>
    {
        public ProductValidator()
        {
            RuleFor(x => x.Name).Required().MinLength(2).MaxLength(200);
            RuleFor(x => x.SKU).Required().Matches(@"^[A-Z0-9-]+$", "SKU must contain only uppercase letters, digits, and dashes.");
            RuleFor(x => x.Price).GreaterThanOrEqual(0.01m, "Price must be positive.");
            RuleFor(x => x.StockQuantity).GreaterThanOrEqual(0);
            RuleFor(x => x.Email).Email();
        }
    }

    /// <summary>
    /// Examples demonstrating the Birko.Validation fluent validation framework.
    /// </summary>
    public static class ValidationExamples
    {
        /// <summary>
        /// Define a validator with fluent rules and validate instances.
        /// </summary>
        public static void RunBasicValidationExample()
        {
            ExampleOutput.WriteLine("=== Basic Validation Example ===\n");

            var validator = new ProductValidator();

            // Valid product
            var validProduct = new ExampleValidationProduct
            {
                Name = "Wireless Mouse",
                SKU = "WM-001",
                Price = 29.99m,
                StockQuantity = 50,
                Email = "support@example.com"
            };

            ValidationResult result = validator.Validate(validProduct);
            ExampleOutput.WriteLine($"Valid product - IsValid: {result.IsValid}");
            ExampleOutput.WriteLine($"  Errors: {result.Errors.Count}");

            // Invalid product
            var invalidProduct = new ExampleValidationProduct
            {
                Name = "",           // Required, MinLength(2) violation
                SKU = "invalid sku", // Regex violation (lowercase, spaces)
                Price = -5.00m,      // GreaterThanOrEqual(0.01) violation
                StockQuantity = -1,  // GreaterThanOrEqual(0) violation
                Email = "not-an-email"
            };

            ValidationResult invalidResult = validator.Validate(invalidProduct);
            ExampleOutput.WriteLine($"\nInvalid product - IsValid: {invalidResult.IsValid}");
            ExampleOutput.WriteLine($"  Errors: {invalidResult.Errors.Count}");
            foreach (var error in invalidResult.Errors)
            {
                ExampleOutput.WriteLine($"    [{error.ErrorCode}] {error.PropertyName}: {error.Message}");
            }

            // ToDictionary for API response formatting
            var errorDict = invalidResult.ToDictionary();
            ExampleOutput.WriteLine("\n  As dictionary (for API responses):");
            foreach (var kvp in errorDict)
            {
                ExampleOutput.WriteLine($"    {kvp.Key}: [{string.Join(", ", kvp.Value)}]");
            }

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// Use Must() for custom predicate validation.
        /// </summary>
        public static void RunCustomRuleExample()
        {
            ExampleOutput.WriteLine("=== Custom Rule Example ===\n");

            // Inline validator with custom Must() predicates
            var validator = new DescriptionValidator();

            var product = new ExampleValidationProduct
            {
                Name = "Test Product",
                Description = "Short",  // Too short for custom rule
                Price = 999.99m         // Exceeds custom price limit
            };

            var result = validator.Validate(product);
            ExampleOutput.WriteLine($"IsValid: {result.IsValid}");
            foreach (var error in result.Errors)
            {
                ExampleOutput.WriteLine($"  [{error.ErrorCode}] {error.PropertyName}: {error.Message}");
            }

            // Fix the issues
            product.Description = "This is a sufficiently detailed product description for the store.";
            product.Price = 499.99m;

            var fixedResult = validator.Validate(product);
            ExampleOutput.WriteLine($"\nAfter fixing - IsValid: {fixedResult.IsValid}");

            ExampleOutput.WriteLine("\nAvailable fluent rules:");
            ExampleOutput.WriteLine("  Required()              - not null, empty, or whitespace");
            ExampleOutput.WriteLine("  MinLength(n)            - minimum string length");
            ExampleOutput.WriteLine("  MaxLength(n)            - maximum string length");
            ExampleOutput.WriteLine("  Length(min, max)         - string length range");
            ExampleOutput.WriteLine("  Range(min, max)          - numeric/comparable range");
            ExampleOutput.WriteLine("  GreaterThanOrEqual(min)  - minimum value");
            ExampleOutput.WriteLine("  LessThanOrEqual(max)     - maximum value");
            ExampleOutput.WriteLine("  Matches(pattern)         - regex match");
            ExampleOutput.WriteLine("  Email()                  - email format");
            ExampleOutput.WriteLine("  Must(predicate)          - custom property predicate");
            ExampleOutput.WriteLine("  MustSatisfy(predicate)   - cross-property validation");
            ExampleOutput.WriteLine("  In(values...)            - allowed values set");
            ExampleOutput.WriteLine("  NotEqual(value)          - must not equal");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// Async validation using ValidateAsync.
        /// </summary>
        public static async Task RunAsyncValidationExample()
        {
            ExampleOutput.WriteLine("=== Async Validation Example ===\n");

            var validator = new ProductValidator();

            var product = new ExampleValidationProduct
            {
                Name = "Async Validated Product",
                SKU = "AVP-001",
                Price = 49.99m,
                StockQuantity = 10,
                Email = "async@example.com"
            };

            // ValidateAsync returns Task<ValidationResult>
            ValidationResult result = await validator.ValidateAsync(product);
            ExampleOutput.WriteLine($"Async validation - IsValid: {result.IsValid}");
            ExampleOutput.WriteLine($"  Errors: {result.Errors.Count}");

            // Static factory methods on ValidationResult
            var success = ValidationResult.Success();
            ExampleOutput.WriteLine($"\nValidationResult.Success() - IsValid: {success.IsValid}");

            var failure = ValidationResult.Failure("Price", "NEGATIVE_PRICE", "Price cannot be negative");
            ExampleOutput.WriteLine($"ValidationResult.Failure() - IsValid: {failure.IsValid}");
            ExampleOutput.WriteLine($"  Error: {failure.Errors[0].Message}");

            // Merge results
            var merged = ValidationResult.Success();
            merged.Merge(failure);
            ExampleOutput.WriteLine($"\nMerged result - IsValid: {merged.IsValid}, Errors: {merged.Errors.Count}");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }
    }

    /// <summary>
    /// Validator demonstrating Must() and MustSatisfy() custom predicates.
    /// </summary>
    internal class DescriptionValidator : AbstractValidator<ExampleValidationProduct>
    {
        public DescriptionValidator()
        {
            RuleFor(x => x.Name).Required();

            // Must() receives the property value
            RuleFor(x => x.Description)
                .Must(desc => desc != null && desc.Length >= 20,
                    "Description must be at least 20 characters.",
                    "DESCRIPTION_TOO_SHORT");

            // MustSatisfy() receives the full model for cross-property validation
            RuleFor(x => x.Price)
                .MustSatisfy(product => product.Price < 500m || !string.IsNullOrEmpty(product.Description),
                    "Products over $500 require a description.",
                    "EXPENSIVE_NO_DESCRIPTION");
        }
    }
}
