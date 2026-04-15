# Web Component Framework Guide

## Overview

Birko ships three TypeScript projects for building modern Single-Page Applications on top of Web Components:

| Project | Purpose |
|---|---|
| `Birko.Web.Core` | Minimal Web Component framework — Shadow DOM base class, reactive state, HTTP/SSE clients, hash router. No external dependencies. |
| `Birko.Web.Components` | Component library of ~43 production-ready Shadow DOM web components (inputs, layout, data, feedback, navigation, command palette) built on Web.Core. |
| `Birko.Web.Shell` | Application shell framework — abstract `BAppShell` with ribbon, status bar, notifications, tenant switcher, plus factory functions for auth, modules, routing, page bases. |

All three are **pure TypeScript ES modules** with no build tooling. They are consumed via TypeScript path aliases or esbuild path mappings in the downstream app. No virtual DOM, no bundler required.

## Birko.Web.Core

### `BaseComponent`

Abstract base extending `HTMLElement` with Shadow DOM. Subclass it, override `render()`, register with `define()`.

```typescript
import { BaseComponent, define } from 'birko-web-core';

export class MyWidget extends BaseComponent {
  private _count = 0;

  static get styles() {
    return `:host { display: block; } button { cursor: pointer; }`;
  }

  render() {
    return `
      <section>
        <p>Count: ${this._count}</p>
        <button id="inc">+1</button>
      </section>
    `;
  }

  protected onMount() {
    this.$('#inc')?.addEventListener('click', () => {
      this._count++;
      this.update();  // re-renders the Shadow DOM
    });
  }
}

define('my-widget', MyWidget);
```

**Lifecycle:** `onMount()`, `onUpdated()`, `onUnmount()`.
**Rendering:** `render() → HTML string`, `update()` re-renders.
**Querying:** `this.$(selector)`, `this.$$(selector)`, `this.child<T>(selector)`.
**Events:** `this.emit(eventName, detail)`.
**Styles:** `static get styles`, `static get sharedStyles` (merged).

### Reactive state

```typescript
import { Signal, computed, Store } from 'birko-web-core/state';

const count = new Signal(0);
const doubled = computed(() => count.value * 2, [count]);

count.subscribe(v => console.log('count:', v));
count.value = 5;              // triggers subscription; doubled updates to 10

// Store — key-value of Signals
const store = new Store<{ user: string; theme: 'light' | 'dark' }>();
store.set('user', 'alice');
store.onChange('user', v => console.log('user:', v));
```

`persistSet` / `persistGet` / `persistRemove` are localStorage helpers that JSON-serialize safely.

### HTTP client (`ApiClient`)

Fetch-based with automatic token and tenant header injection.

```typescript
import { ApiClient, unwrapList, apiErrorMessage } from 'birko-web-core/http';

const api = new ApiClient({
  baseUrl: '/api',
  getToken: () => authStore.get('token'),
  getTenantId: () => authStore.get('tenantId')
});

const res = await api.get<{ items: Product[] }>('/products');
if (res.ok) {
  const products = unwrapList<Product>(res);  // handles { items: [] } | { data: [] } | T[]
} else {
  console.error(apiErrorMessage(res));         // extracts ProblemDetails / ModelState
}
```

`ApiResponse<T>` fields: `ok`, `status`, `data`, `headers`, `queued`, `fromCache`.

### SSE client

```typescript
import { SseClient } from 'birko-web-core/http';

const sse = new SseClient('/api/events', { token });
sse.on('notification', e => console.log('new:', e.data));
sse.on('progress', e => console.log('progress:', e.data));
sse.connect();
// sse.disconnect();
```

Auto-reconnect on disconnect.

### Router

```typescript
import { Router, link } from 'birko-web-core/router';

const router = new Router([
  { path: '/', component: 'home-page' },
  { path: '/products/:id', component: 'product-detail' },
  { path: '/admin', component: 'admin-panel', guard: () => authStore.get('isAdmin') }
]);

// Navigation helpers
router.navigate('/products/42');
const html = link('/products/42', 'View product');   // <a href="#/products/42">View product</a>
```

Hash-based (`#/route`), supports guards, child routes, parameter extraction.

## Birko.Web.Components

~43 components grouped into 6 categories. All are Shadow DOM components built on `BaseComponent`, styled with design tokens (`--b-*` CSS variables).

### Inputs (17)

`b-input`, `b-select`, `b-multi-select`, `b-button`, `b-checkbox`, `b-switch`, `b-radio`, `b-textarea`, `b-search-input` (debounced), `b-file-upload`, `b-inline-edit`, `b-range` (slider/from-to), `b-date-picker`, `b-time`, `b-datetime-picker`, `b-option-group` (segmented buttons), `b-form` (schema-driven form builder with validation and cascading selects).

### Layout (9)

`b-card`, `b-modal` (sm/md/lg/xl/xxl sizes), `b-drawer`, `b-tabs`, `b-confirm-dialog` (`show(): Promise<boolean>`), `b-dropdown-menu`, `b-tooltip`, `b-split-panel` (master-detail with responsive collapse), `b-chat`.

### Data (7)

- `b-table` — client-side sort/filter with setColumns/setData
- `b-data-table` — auto-fetching server-driven table with search, filters, pagination, bulk actions, inline editing
- `b-editable-table` — fully editable table (every cell in edit mode) for line items
- `b-pagination` — page indicator
- `b-badge` — status badge (success/warning/danger/info/secondary)
- `b-chart` — bar/line/area/pie/donut/gauge
- `b-tag` — tag/label element

### Feedback (5)

- `toast` — function API (`toast.success()`, `toast.error()`, etc.)
- `b-spinner`, `b-empty`, `b-skeleton` (text/circle/table/form), `b-stale-banner` (cache warning)

### Navigation (4)

- `b-sidebar` — collapsible nav tree
- `b-breadcrumb`
- `b-ribbon` — Office-style ribbon with grouped items, context actions, pin/unpin
- `b-tree-menu` — hierarchical menu with keyboard navigation

### Command Palette (1)

- `b-command-palette` — Ctrl+K / Cmd+K search interface
- Module API: `openCommandPalette()`, `closeCommandPalette()`, `toggleCommandPalette()`, `registerProvider()`, `createRecentProvider()` — pluggable search providers.

### Example — b-data-table

```typescript
import { BDataTable } from 'birko-web-components';

const el = document.createElement('b-data-table') as BDataTable;
el.setConfig({
  endpoint: '/api/devices',
  apiClient: api,
  pageSize: 20,
  columns: [
    { key: 'name', label: 'Name', sortable: true },
    { key: 'status', label: 'Status', render: v => `<b-badge variant="${v}">${v}</b-badge>` }
  ],
  searchable: true,
  actions: [{ id: 'add', label: '+ Add', variant: 'primary' }],
  rowActions: [
    { id: 'edit', label: 'Edit' },
    { id: 'delete', label: 'Delete', variant: 'danger' }
  ]
});
el.load();
```

## Birko.Web.Shell

### Three-level shell hierarchy

```
BCoreAppShell        (abstract — shared infrastructure, default minimal layout)
    └── BSidebarAppShell  (adds opt-in left + right sidebars via <b-sidebar>)
            └── BAppShell  (abstract — full ribbon-based Office-style shell)
```

Use **`BAppShell`** when you want the full ribbon shell (most apps). Sidebars are inherited and opt-in — your ribbon shell can also have left/right panels.
Use **`BSidebarAppShell`** when you need sidebars but a custom top instead of the ribbon (sidebar-driven navigation patterns).
Use **`BCoreAppShell`** directly when you need a different layout — mobile-first shell, kiosk mode, login page shell.

### `BCoreAppShell` — shared infrastructure

Abstract core (~280 LOC). Provides:
- Theme + layout persistence from `localStorage` (`data-theme`, `data-layout` on `<html>`)
- Online / offline tracking exposed as `protected get isOnline(): boolean`
- Default user dropdown (profile / settings / signout) with built-in handler
- Brand link with configurable target (`brandHref`)
- Breadcrumb event listener (`set-breadcrumbs` from pages)
- Default minimal layout (brand + user dropdown + content slot) — usable as-is
- Base CSS tokens (`:host` flex column, `.app-brand`, `.user-trigger`, `.app-content`, `.app-status-bar` skeleton, `.status-dot` variants)
- Render helpers: `renderBrand()`, `renderUserDropdown()` for subclasses

**Required (4 abstract methods):**
```typescript
protected abstract get brandName(): string;
protected abstract getUserName(): string;
protected abstract t(key: string, params?: Record<string, string>): string;
protected abstract onSignOut(): void;
```

**Subclass for a custom layout:**
```typescript
import { BCoreAppShell } from 'birko-web-shell';

export abstract class BSidebarAppShell extends BCoreAppShell {
  protected abstract getSidebarItems(): SidebarItem[];

  render() {
    return `
      <aside class="shell-sidebar">
        <div class="brand">${this.renderBrand()}</div>
        <b-sidebar id="sidebar"></b-sidebar>
        <div class="user">${this.renderUserDropdown()}</div>
      </aside>
      <main class="shell-content"><slot></slot></main>
    `;
  }

  static get styles() {
    return super.styles + `
      :host { display: grid; grid-template-columns: 16rem 1fr; }
      .shell-sidebar { /* ... */ }
      .shell-content { /* ... */ }
    `;
  }

  protected onMount() {
    super.onMount();   // theme, online/offline, breadcrumbs
    // your sidebar-specific setup
  }
}
```

### `BSidebarAppShell` — opt-in left + right sidebars

Extends `BCoreAppShell`. Adds opt-in **left and/or right sidebars** using `<b-sidebar>`. Both can be enabled simultaneously (Outlook-style: folder list left + reading pane right; VS Code-style: Explorer left + Outline right).

**No new required methods** — sidebar is fully opt-in via getter overrides:

| Override | Default | Purpose |
|---|---|---|
| `showLeftSidebar` / `showRightSidebar` | `false` | Enable a sidebar |
| `getLeftSidebarItems()` / `getRightSidebarItems()` | `[]` | `SidebarItem[]` items |
| `getActiveLeftSidebarItem()` / `getActiveRightSidebarItem()` | `''` | Active item highlight |
| `leftSidebarCollapsible` / `rightSidebarCollapsible` | `true` | Show collapse toggle |
| `onLeftSidebarToggle(c)` / `onRightSidebarToggle(c)` | noop | User toggle callback |

Collapsed state of each sidebar persists independently in `localStorage`. Refresh from store subscriptions via `refreshLeftSidebar()` / `refreshRightSidebar()`.

`BSidebarAppShell` only overrides `renderContent()` (wraps base content with sidebar containers) — header and footer hooks are unchanged. Subclasses overriding `render()` entirely (like `BAppShell`) just call `${this.renderContent()}` to get the sidebar layout.

### `BAppShell` — ribbon-based shell

Extends `BSidebarAppShell` (which extends `BCoreAppShell`). Adds the full Office-style layout: ribbon tabs, status bar, notification bell with badge, user dropdown with sign-out, tenant switcher, command palette — all wired together. Sidebars come automatically from the parent class (opt-in).

```typescript
import { BAppShell } from 'birko-web-shell/shell';
import { createAuthStore } from 'birko-web-shell/auth';
import { createModuleStore, buildRibbon } from 'birko-web-shell/modules';

const { store: authStore, setAuth, clearAuth } = createAuthStore({ /* ... */ });
const { store: moduleStore } = createModuleStore();

class AppShell extends BAppShell {
  protected get brandName() { return 'My App'; }

  protected getUserName()   { return authStore.get('userName') ?? 'User'; }
  protected getRibbonTabs() { return buildRibbon(moduleStore.get('modules')); }
  protected getActiveTabId() { return moduleStore.get('activeModuleId') ?? ''; }
  protected t(key: string)  { return i18n.t(key); }

  protected onTabChange(tabId: string) {
    const mod = moduleStore.get('modules').find(m => m.id === tabId);
    if (mod?.options[0]) window.location.hash = `#${mod.options[0].route}`;
  }

  protected onSignOut() {
    clearAuth();
    window.location.hash = '#/login';
  }
}

define('my-app-shell', AppShell);
```

Required abstract methods: 4 from `BCoreAppShell` (`brandName`, `getUserName`, `t`, `onSignOut`) + 3 added by `BAppShell` (`getRibbonTabs`, `getActiveTabId`, `onTabChange`). Optional overrides: ~20 (avatar URL, status indicators, notification click handlers, ...). Public refresh hooks: `refreshRibbon()`, `refreshStatusBar()`, `refreshBellBadge()`, `refreshTenantSwitcher()`, `refreshUserMenu()` (last one inherited from `BCoreAppShell`).

### Factory functions

| Factory | Returns | Purpose |
|---|---|---|
| `createAuthStore(config)` | `{ store, setAuth, clearAuth, setPendingChallenge, clearChallenge }` | JWT decoding, claim mapping, localStorage persistence, `AuthState` signal |
| `createModuleStore()` | `{ store, hasPermission, hasModulePermission, getVisibleOptions, resolveModuleFromHash }` | Tracks modules, active module, permission-filtered options. Wildcard `*` = superadmin |
| `buildRibbon(modules, labelResolver?)` | `RibbonTab[]` | Transform `ModuleManifest[]` into ribbon structure with i18n |
| `createAuthGuard(store, redirectPath)` | route guard fn | Redirect to login if not authenticated |
| `createModuleGuard(store, ...)` | `(moduleId, permission) => guard` | Permission-based access control |
| `createShellWrapper(shellTag)` | `(pageTag) => HTMLElement` | Persistent shell helper — creates shell once, swaps pages |
| `createConnectionStateManager()` | `{ setState, getState, onChange }` | Tracks SSE/WebSocket status for status bar |
| `createNotificationStore()` | `{ store, handleNewNotification, openDrawer, closeDrawer }` | Notification list + unread count |
| `createModuleNavProvider()` | palette provider | Command palette search over modules/options |
| `createEntitySearchProvider()` | palette provider | API-backed entity search with debounce |
| `applyBranding(tenant)` | void | Sets `--b-color-primary` from tenant branding |

### Page base classes

Drop-in base classes for the common page shapes:

| Class | Purpose |
|---|---|
| `BaseListPage<T>` | CRUD list: auto-fetching table, search toolbar, create/edit modal, delete confirm, permissions |
| `BaseSplitPage<T>` | Master-detail with optional CRUD — master list + detail pane, responsive collapse |
| `BaseDetailPage<T>` | Single-entity detail/edit — loads by ID, populates form, save/cancel |
| `BaseFormModal<T>` | Modal with form in create or edit mode (`open()` or `open(id)`) |
| `BaseDashboardWidget<TConfig>` | Dashboard widget base with auto-refresh and API access |

Extension points are typed — override `endpoint`, `api`, `columns`, `formSchema`, `entityLabel`, plus hooks (`onRowClick`, `afterSave`, `mapToForm`, `mapFromForm`, `onEntityLoaded`, `onSave`, ...).

### Breadcrumbs

```typescript
import { setBreadcrumbs } from 'birko-web-shell/shell';

setBreadcrumbs(this, [
  { label: 'Inventory', href: '#/inventory' },
  { label: 'Items' }   // last item has no href
]);
```

Dispatches a bubbling `CustomEvent`; the shell listens and updates its breadcrumb strip.

## Build system and dependencies

All three projects are **pure TypeScript ES modules**. Each has a `package.json` with `type: "module"` and explicit `exports` (subpath imports like `birko-web-core/state`, `birko-web-shell/auth`).

| Project | Depends on |
|---|---|
| `birko-web-core` | (none) |
| `birko-web-components` | `birko-web-core` |
| `birko-web-shell` | `birko-web-core`, `birko-web-components` |

Consumer apps wire these up via TypeScript path aliases or esbuild path mappings pointing directly to the source files — no compilation or bundling of the libraries themselves.

## Design notes

- **No virtual DOM** — `render()` returns an HTML string; `update()` replaces the Shadow DOM content. Cheap and predictable.
- **Shadow DOM encapsulation** — every component has scoped styles; no CSS leakage across components.
- **Design tokens** — `--b-color-*`, `--b-space-*`, `--b-font-*` CSS variables are themeable per app or per tenant (`applyBranding`).
- **Factory over singleton** — stores and guards are created by factory functions so the same code works in tests and across multiple apps.
- **No runtime dependency on Birko backend** — the client libraries are backend-agnostic; they just issue HTTP requests. Any API shape compatible with `ApiClient` works.

## See also

- [Security Guide](security.md) — JWT tokens consumed by `createAuthStore`
- [Telemetry Guide](telemetry.md) — correlation IDs flow through `ApiClient` headers
- [Communication Guide](communication.md) — server-side REST/SSE endpoints that pair with these clients
