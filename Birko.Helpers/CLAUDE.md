# Birko.Helpers

## Overview
Helper utilities and extensions for Birko data layer operations.

## Project Location
`C:\Source\Birko.Helpers\`

## Purpose
- Common helper methods
- Extension methods
- Utilities for data operations
- Shared functionality across data projects

## Components

### Extensions
- String extensions
- Collection extensions
- DateTime extensions
- Guid extensions

### Helpers
- **PathValidator** — Path security utilities (ValidateUserPath, SanitizePath, NormalizePath, CombineAndValidate, CombineAndValidateUnchecked)
- Data conversion helpers
- Validation helpers
- Serialization helpers
- Reflection helpers

## Common Extensions

### Collection Extensions
```csharp
// Batch operations
public static IEnumerable<IEnumerable<T>> Batch<T>(this IEnumerable<T> source, int batchSize);

// Paging
public static IEnumerable<T> Page<T>(this IEnumerable<T> source, int page, int pageSize);

// Distinct by
public static IEnumerable<T> DistinctBy<T, TKey>(this IEnumerable<T> source, Func<T, TKey> keySelector);
```

### String Extensions
```csharp
// Safe truncate
public static string Truncate(this string value, int maxLength);

// To slug
public static string ToSlug(this string value);

// Sanitize SQL
public static string SanitizeSql(this string value);
```

### Guid Extensions
```csharp
// Is empty
public static bool IsEmpty(this Guid guid);

// To short string (base64)
public static string ToShortString(this Guid guid);

// From short string
public static Guid FromShortString(this string value);
```

### DateTime Extensions
```csharp
// To Unix timestamp
public static long ToUnixTime(this DateTime dateTime);

// From Unix timestamp
public static DateTime FromUnixTime(this long unixTime);

// Is between
public static bool IsBetween(this DateTime date, DateTime start, DateTime end);
```

## Data Conversion

```csharp
using Birko.Helpers;

// Entity to DTO
public static TDestination To<TDestination>(this object source);

// Entity to dictionary
public static IDictionary<string, object> ToDictionary(this object source);

// Dictionary to entity
public static T ToEntity<T>(this IDictionary<string, object> dictionary);
```

## Validation

```csharp
using Birko.Helpers;

// Validate email
public static bool IsValidEmail(this string email);

// Validate URL
public static bool IsValidUrl(this string url);

// Validate phone
public static bool IsValidPhone(this string phone);
```

## Serialization

```csharp
using Birko.Helpers;

// To JSON
public static string ToJson(this object obj);

// From JSON
public static T FromJson<T>(this string json);

// To XML
public static string ToXml(this object obj);
```

## Dependencies
- Birko.Data.Core
- Birko.Data.Stores
- Birko.Data.Repositories
- System.Text.Json (or Newtonsoft.Json)
- System.ComponentModel.DataAnnotations

## Use Cases
- Common operations across all data projects
- Reducing code duplication
- Providing consistent behavior
- Simplifying complex operations

## Best Practices

1. **Namespace organization** - Keep extensions well-organized
2. **Naming** - Use descriptive names
3. **Null handling** - Always handle null values
4. **Performance** - Consider performance for hot paths
5. **Documentation** - Document all public helpers

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly. This includes:
- New classes, interfaces, or methods
- Changed dependencies
- New or modified usage examples
- Breaking changes

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect:
- New or renamed files and components
- Changed architecture or patterns
- New dependencies or removed dependencies
- Updated interfaces or abstract class signatures
- New conventions or important notes

### Test Requirements
Every new public functionality must have corresponding unit tests. When adding new features:
- Create test classes in the corresponding test project
- Follow existing test patterns (xUnit + FluentAssertions)
- Test both success and failure cases
- Include edge cases and boundary conditions
