# Birko.Models.SEO

## Overview
SEO (Search Engine Optimization) domain models for the Birko Framework.

## Project Location
`Birko.Models.SEO/`

## Purpose
- SEO metadata models
- URL slug generation
- Meta tag management
- Sitemap support

## Components

### Models
- `SEOMetadata` - SEO metadata entity
- `URLAlias` - URL alias/redirect
- `SitemapItem` - Sitemap entry

### ViewModels
- `SEOMetadataViewModel` - SEO metadata display

### Filters
- `SEOFilter` - SEO filter

## SEO Metadata

```csharp
using Birko.Models.SEO.Models;

public class SEOMetadata : Entity
{
    public string Title { get; set; }
    public string Description { get; set; }
    public string Keywords { get; set; }
    public string CanonicalUrl { get; set; }
    public string OGTitle { get; set; } // Open Graph
    public string OGDescription { get; set; }
    public string OGImage { get; set; }
    public bool NoIndex { get; set; }
    public bool NoFollow { get; set; }
}
```

## URL Alias

```csharp
public class URLAlias : Entity
{
    public string Alias { get; set; } // SEO-friendly URL
    public string TargetPath { get; set; } // Actual path
    public bool IsPermanent { get; set; } // 301 redirect
}
```

## Sitemap Item

```csharp
public class SitemapItem
{
    public string Url { get; set; }
    public DateTime? LastModified { get; set; }
    public ChangeFrequency ChangeFrequency { get; set; }
    public double Priority { get; set; }
}
```

## Dependencies
- Birko.Models

## Use Cases
- CMS systems
- E-commerce platforms
- Blog software
- Public websites

## Best Practices

1. **Unique titles** - Ensure unique page titles
2. **Meta descriptions** - Keep under 160 characters
3. **URL structure** - Use hyphens, not underscores
4. **Canonical URLs** - Always specify for duplicate content
5. **XML sitemaps** - Generate and submit sitemaps

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
