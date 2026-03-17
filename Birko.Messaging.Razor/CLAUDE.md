# Birko.Messaging.Razor

## Overview

Razor template engine for the Birko Messaging framework. Implements `ITemplateEngine` using RazorLight for full Razor syntax support — conditionals, loops, strongly-typed models, and layouts. Designed as a drop-in replacement for `StringTemplateEngine` when complex HTML email templates are needed.

## Project Location

- **Path:** `C:\Source\Birko.Messaging.Razor\`
- **Type:** Shared project (`.shproj` / `.projitems`)
- **Namespace:** `Birko.Messaging.Razor`
- **GUID:** `c8d2e4f6-a1b3-4c5d-9e7f-2a3b4c5d6e8f`

## Components

### RazorTemplateEngine.cs
- Implements `ITemplateEngine` and `IDisposable`
- Uses `RazorLightEngine` for Razor compilation and rendering
- `RenderAsync(string, object, CancellationToken)` — inline Razor string rendering with stable cache keys
- `RenderAsync(IMessageTemplate, object, CancellationToken)` — file lookup by `Name`, fallback to `BodyTemplate` inline
- `RenderFileAsync(string, object, CancellationToken)` — explicit file-based template rendering
- `InvalidateFileCache(string)` — invalidate cached file template
- Wraps `RazorLightException` and `InvalidOperationException` into `TemplateRenderException`
- Constructor accepts `RazorTemplateOptions` or pre-built `RazorLightEngine`

### RazorTemplateOptions.cs
- Configuration for template engine
- `TemplateBasePath` — base directory for `.cshtml` files (null = inline-only mode)
- `FileExtension` — default ".cshtml"
- `EnableCaching` — cache compiled templates (default true)
- `FileEncoding` — file reading encoding (default UTF-8)
- `DefaultNamespaces` — auto-imported namespaces in all templates

### RazorFileTemplateProvider.cs
- Loads `.cshtml` template content from disk
- In-memory caching with `ConcurrentDictionary`
- Directory traversal protection (validates resolved path stays within base)
- Path normalization (forward/back slashes, auto-append extension)
- `GetTemplateAsync(string, CancellationToken)` — resolve and read template file
- `InvalidateCache(string)` / `ClearCache()` — cache management

## Dependencies

- **Birko.Messaging** — `ITemplateEngine`, `IMessageTemplate`, `TemplateRenderException`
- **RazorLight** (NuGet) — Razor compilation engine (added by consuming project)

## Key Design Decisions

- **Shared project** — code compiles into consuming project, no separate assembly
- **RazorLight** — chosen over raw `Microsoft.AspNetCore.Razor.Language` for simplicity; consuming project adds NuGet
- **File + inline dual mode** — `IMessageTemplate` rendering tries file first (by Name), falls back to BodyTemplate
- **Stable inline cache keys** — inline templates get a stable GUID key per unique content string to avoid recompilation
- **Directory traversal protection** — `Path.GetFullPath` + prefix check prevents escaping base directory

## Conventions

- All public methods throw `TemplateRenderException` (from Birko.Messaging) for rendering failures
- Null arguments throw `ArgumentNullException`
- File-based methods require `TemplateBasePath` to be set, otherwise throw `TemplateRenderException`
- Template names support subdirectories: `"Emails/Welcome"` resolves to `<base>/Emails/Welcome.cshtml`

## Maintenance

- When adding new features, update this CLAUDE.md and README.md
- All new public functionality must have corresponding unit tests in Birko.Messaging.Razor.Tests
