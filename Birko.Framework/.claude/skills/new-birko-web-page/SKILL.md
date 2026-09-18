---
name: new-birko-web-page
description: Add a new module/page to a **consumer** Birko.Web app (e.g. Symbio UI) — using `base-crud-page` / `base-list-page` / `base-detail-page` / `base-split-page` / `base-form-modal` from `Birko.Web.Shell`. Use when the user says "novy modul", "novy page", "add a CRUD module", "add a new entity page", "pridaj stranku do shell", "novy CRUD pre Entity", or similar requests to extend a Birko.Web-powered application with a new entity surface. This is the consumer-side counterpart to [[new-birko-web-component]] (which adds a `b-*` component to the framework library). Wires the page class extending the right base, registers it as a `ModuleManifest` so the ribbon picks it up via `buildRibbon()`, adds the route via `buildModuleRoutes()`, defines its FormField + TableColumn config, adds `bws.*` i18n keys, and wires permissions via the module store.
---

# Birko.Web — New Module / Page in a Consumer App

Add a new entity page to a Birko.Web consumer app. The app already has the shell scaffolded (via [[birko-new-project]] Web UI shape) — this skill adds **one entity surface** (typically: list + detail + form modal) following the canonical base-page pattern from `Birko.Web.Shell`.

## Authoritative references — READ THESE FIRST when invoked

- `C:\Source\Birko\Web\Birko.Web.Shell\CLAUDE.md` — shell architecture: `BCoreAppShell` / `BSidebarAppShell` / `BAppShell` three-level hierarchy, module-store, route-builder, command palette providers, factory pattern (no singletons).
- `C:\Source\Birko\Web\Birko.Web.Shell\src\pages\` — base page classes:
  - `base-page.ts` — root abstract page
  - `base-list-page.ts` — table + pagination + search
  - `base-detail-page.ts` — read-only detail view
  - `base-crud-page.ts` — list + detail + edit modal in one
  - `base-split-page.ts` — master/detail layout (list on left, detail on right)
  - `base-form-modal.ts` — modal form for create/edit
- `C:\Source\Birko\Web\Birko.Web.Shell\src\modules\module-types.ts` — `ModuleManifest`, `ModuleOption`, `ModuleStatus`, permission shape.
- `C:\Source\Birko\Web\Birko.Web.Shell\src\modules\ribbon-builder.ts` — `buildRibbon(modules, labelResolver?)` — pure function that consumes manifests.
- `C:\Source\Birko\Web\Birko.Web.Shell\src\modules\route-builder.ts` — `buildModuleRoutes()`, `resolveModuleFromHash()`.
- `C:\Source\Birko\Web\Birko.Web.Components\CLAUDE.md` — the `b-*` components your page will compose (b-table, b-data-table, b-form, b-modal, b-confirm-dialog, etc.).
- **In the consumer app itself** — look for an existing module/page closest to what you're adding and mirror its file shape. The patterns are repository-specific (the consumer app's own `src/modules/` or `src/pages/` folder).

**If anything below contradicts those files, follow the files.**

## Inputs to gather from the user

1. **Consumer app path** — where the page lives. Examples: `C:\Source\SymbioUI`, `C:\Source\FisData.UI`. Must be a Birko.Web-powered app (check for a `package.json` referencing `birko-web-shell`).
2. **Entity name** — singular, PascalCase. Examples: `Customer`, `Invoice`, `StockItem`.
3. **Page shape** — pick one:
   - **CRUD** (`base-crud-page`) — list + inline create/edit modal. Most common.
   - **List only** (`base-list-page`) — read-only table with row actions.
   - **Split** (`base-split-page`) — master/detail with persistent detail panel.
   - **Detail only** (`base-detail-page`) — when navigated from another page.
   - **Custom** — extends `base-page` directly when none of the above fit.
4. **Module ribbon tab** — which ribbon tab does this module belong under? (Existing tabs come from the app's `getRibbonTabs()` — list them with `gh` or by reading the app's shell class.)
5. **Permissions** — which permission keys gate this module? Convention: `module:{name}:view`, `module:{name}:edit`, `module:{name}:delete`.
6. **Data source** — which `ApiClient` endpoint backs the list? `GET /api/{entities}` is the typical shape. The skill assumes a Birko-style REST endpoint; adapt if the app uses GraphQL or a different convention.
7. **Form fields** — the columns and editable fields for the entity. The skill produces a `FormField[]` and `TableColumn[]` config; the user supplies the field list (name, type, validation).

## Per-file checklist for the new module

### 1. The page class — `src/modules/{name}/{name}-page.ts`

```typescript
import { BaseCrudPage } from 'birko-web-shell';
import { TableColumn, FormField, FieldType } from 'birko-web-components';
import { apiClient } from '../../app/api';
import { t } from 'birko-web-core';

export class CustomerPage extends BaseCrudPage<Customer> {
  protected entityLabel = 'Customer';  // used by t() auto-interpolation of {entity}
  protected idField = 'id';

  protected getColumns(): TableColumn[] {
    return [
      { field: 'name', label: t('bws.customer.name', undefined, 'Name'), sortable: true },
      { field: 'email', label: t('bws.customer.email', undefined, 'Email') },
      // …
    ];
  }

  protected getFormFields(): FormField[] {
    return [
      { name: 'name', type: FieldType.Text, label: 'bws.customer.name', required: true },
      { name: 'email', type: FieldType.Email, label: 'bws.customer.email' },
      // …
    ];
  }

  protected async loadList(query: ListQuery): Promise<ListResult<Customer>> {
    return apiClient.get('/api/customers', query);
  }
  protected async createEntity(entity: Customer): Promise<Customer> {
    return apiClient.post('/api/customers', entity);
  }
  protected async updateEntity(entity: Customer): Promise<Customer> {
    return apiClient.put(`/api/customers/${entity.id}`, entity);
  }
  protected async deleteEntity(id: string): Promise<void> {
    return apiClient.delete(`/api/customers/${id}`);
  }
}
define('customer-page', CustomerPage);
```

The exact base-class method names depend on the version of `Birko.Web.Shell` — open the matching base file (`base-crud-page.ts` etc.) and follow its actual abstract methods. The above is illustrative.

### 2. The module manifest — `src/modules/{name}/{name}-manifest.ts`

```typescript
import { ModuleManifest } from 'birko-web-shell';

export const customerModule: ModuleManifest = {
  id: 'customer',
  labelKey: 'bws.module.customer',                  // i18n key resolved by labelResolver
  fallbackLabel: 'Customers',
  icon: 'users',                                    // or whatever icon system the app uses
  ribbonTabId: 'main',                              // which tab from getRibbonTabs()
  permissions: ['module:customer:view'],            // gating
  options: [
    { id: 'list', labelKey: 'bws.module.customer.list', route: '#customer/list',
      permissions: ['module:customer:view'] },
    { id: 'new', labelKey: 'bws.module.customer.new', route: '#customer/new',
      permissions: ['module:customer:edit'] },
  ],
};
```

### 3. The route — wire into the app's `buildModuleRoutes()`

In the app's router setup file (typically `src/app/router.ts` or similar):

```typescript
import { customerModule } from '../modules/customer/customer-manifest';
import { CustomerPage } from '../modules/customer/customer-page';

const routes = buildModuleRoutes([
  // existing modules …
  {
    manifest: customerModule,
    component: CustomerPage,
  },
]);
```

### 4. Register in the module store

In the app's bootstrap (typically `src/app/main.ts` or `src/app/index.ts`):

```typescript
moduleStore.register(customerModule);
```

This makes the ribbon pick it up automatically via `buildRibbon()` and the permission helpers (`hasModulePermission`, `getVisibleOptions`) start checking it.

### 5. i18n keys — `src/locales/en.json` (or wherever the app keeps them)

Add the `bws.*` keys used above:

```json
{
  "bws.module.customer": "Customers",
  "bws.module.customer.list": "All customers",
  "bws.module.customer.new": "New {entity}",
  "bws.customer.name": "Name",
  "bws.customer.email": "Email"
}
```

Note the `{entity}` placeholder — `BAppShell.t()` auto-interpolates with `this.entityLabel`, so `"Nový {entity}"` produces `"Nový Customer"` in Slovak.

### 6. Permissions — backend-side

If the new module introduces new permission keys, ensure they're added to:
- The auth provider's permission catalogue (whatever produces JWT claims).
- The role configuration UI (if the app has one).
- Per `Birko.Security.AspNetCore` (2026-04-28 change in CLAUDE.md), if the consumer uses `IUserPermissionResolver`, the resolver implementation must know about the new keys.

## After scaffolding

1. **Run the app** — `npm run dev` (or whatever the app uses). Verify the new module appears in the ribbon under the chosen tab, only when the user has the right permissions.
2. **Click through the CRUD flow** — list loads, create modal opens, save persists, edit pre-fills, delete confirms.
3. **Switch locale** to Slovak (or whichever non-English the app supports) and confirm all labels resolve through i18n. Anything still showing English came from a missing key.
4. **Test responsive** — sidebar collapse, mobile width. Components from `Birko.Web.Components` handle most of this; your page just composes them.

## What this skill does NOT do

- It does not generate the backend API. Backend C# work is a separate task (see [[new-birko-subproject]] / [[new-store-backend]] for the framework-side pieces).
- It does not add a new ribbon tab — if the new module belongs under a brand-new tab, that's a shell change (the app's concrete `BAppShell` subclass overrides `getRibbonTabs()`).
- It does not register a new `b-*` component — if the page needs a UI piece that doesn't exist, use [[new-birko-web-component]] first to add it to `Birko.Web.Components`, then come back here.
- It does not modify `Birko.Web.Shell` itself. The shell is the framework; this skill operates on a consumer of it.
