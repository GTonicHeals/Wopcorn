# fe-05 — Design system, app shell, auth, theming, PWA

**Executor:** Opus 5 · **Depends on:** be-01 · **Blocks:** fe-06

Every visual decision in this plan is **already made**. Implement the values as
written. Where this plan gives a number, use that number. Do not substitute a UI
library, a CSS framework, or an icon package.

Read [`API-CONTRACT.md`](API-CONTRACT.md) for the Auth and Config sections.

## Design reference

`../design/wopcorn-mockup.html` is the rendered mockup of this design system —
open it in a browser (or read its source) to see the intended composition of
the shell, cards, and screens before building. **Precedence: this plan's text
and tokens win on every value.** The mockup shows intent and composition; where
the two disagree, the plan is right. Known example: the mockup draws the card
toggle buttons at 40px tall — the 44px `--tap-min` floor in this plan wins.

## Already done for you

Dependencies are installed and verified against Vue 3.5.40 — do not re-resolve
or upgrade them:

```
vue-router@^5.2.0   pinia@^4.0.2   vuedraggable@^4.1.0   (fe-06 uses the last)
```

Vitest, `@vue/test-utils`, and jsdom are configured. See
[`00-testing.md`](00-testing.md).

---

## Task 1 — Delete the template

Delete: `src/components/HelloWorld.vue`, `src/components/TheWelcome.vue`,
`src/components/WelcomeItem.vue`, `src/components/icons/` (whole directory),
`src/assets/*`, `wopcorn.client/README.md`, `wopcorn.client/CHANGELOG.md`.

Empty out `src/App.vue` and `src/main.ts` — they are rewritten in tasks 4 and 6.

**Verify:** `grep -ri "helloworld\|weatherforecast" src/` returns nothing.

---

## Task 2 — Tokens

Create `src/assets/tokens.css`. These hex values were contrast-checked; the
ratios in the comments are measured, not estimated. **Do not change a colour
without re-checking it** (NFR-9).

```css
:root {
  /* Type */
  --font-ui: 'Inter var', system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
  --font-display: 'Fraunces', 'Iowan Old Style', 'Palatino Linotype', Georgia, serif;

  --text-xs: 0.75rem;    /* 12px — meta rows */
  --text-sm: 0.8125rem;  /* 13px — card titles */
  --text-base: 0.9375rem;/* 15px — body */
  --text-lg: 1.125rem;   /* 18px — section headings */
  --text-xl: 1.5rem;     /* 24px — screen titles */
  --text-2xl: 2rem;      /* 32px — film detail title (display face) */

  /* Space — 4px base */
  --space-1: 4px;  --space-2: 8px;  --space-3: 12px;
  --space-4: 16px; --space-6: 24px; --space-8: 32px; --space-12: 48px;

  /* Radius */
  --radius-sm: 6px; --radius-md: 10px; --radius-lg: 16px; --radius-full: 999px;

  /* Layout */
  --nav-height: 56px;
  --tap-min: 44px;          /* FR-H4 floor. Never smaller. */
  --content-max: 1100px;
  --sidebar-width: 240px;
}

/* Dark is the default. */
:root, :root[data-theme='dark'] {
  --bg: #12100E;
  --surface: #1C1917;
  --surface-raised: #262220;
  --border: #35302C;
  --text: #F5F1EC;          /* 16.88 on bg, 15.55 on surface */
  --text-muted: #A8A09A;    /*  7.38 on bg,  6.80 on surface */
  --accent: #E8B33D;        /*  9.90 on bg,  9.12 on surface */
  --accent-ink: #12100E;    /*  9.90 on accent */
  --poster-edge: rgba(255, 255, 255, 0.09);
  --scrim: linear-gradient(to top, rgba(18,16,14,0.95) 0%, rgba(18,16,14,0) 100%);
}

:root[data-theme='light'] {
  --bg: #FAF7F2;
  --surface: #FFFFFF;
  --surface-raised: #F2ECE3;
  --border: #E5DED4;
  --text: #1A1714;          /* 16.70 on bg, 17.85 on surface */
  --text-muted: #6B625A;    /*  5.58 on bg,  5.97 on surface */
  --accent: #7E5400;        /*  6.23 on bg,  6.66 on surface */
  --accent-ink: #FFFFFF;    /*  6.66 on accent */
  --poster-edge: rgba(0, 0, 0, 0.10);
  --scrim: linear-gradient(to top, rgba(250,247,242,0.95) 0%, rgba(250,247,242,0) 100%);
}

@media (prefers-color-scheme: light) {
  :root:not([data-theme]) {
    /* Repeat the light block here verbatim. */
  }
}
```

The OS preference is the default (FR-H8); a `data-theme` attribute on `<html>`
overrides it in **both** directions, which is why the light rules are written
twice. Set the attribute from a `theme` Pinia store persisted to
`localStorage`, applied before first paint by a small inline script in
`index.html` to avoid a flash of the wrong theme.

**The accent means one thing: the signed-in user's own state** — their rating,
their list membership, the active nav item. Never use it decoratively.

### Fonts

Self-host two variable fonts as woff2 in `src/assets/fonts/`: **Inter** (UI) and
**Fraunces** (display). Declare them with `@font-face` and
`font-display: swap`. If you cannot obtain the files, ship without them — the
stacks above fall back to system faces and the app must still look deliberate.
Do **not** add a Google Fonts `<link>`; this app is served on a LAN with no
guaranteed internet.

Fraunces is used **only** for the film-detail hero title and screen titles. Card
titles use `--font-ui` — a display serif is illegible at 13px in a dense grid.

---

## Task 3 — Reset

Create `src/assets/base.css`, imported after `tokens.css`:

- `*, *::before, *::after { box-sizing: border-box; }`, zero default margins.
- `html, body { background: var(--bg); color: var(--text); font-family: var(--font-ui); overflow-x: hidden; }` (FR-H5)
- `body { -webkit-text-size-adjust: 100%; }`
- `img { max-width: 100%; display: block; }`
- `button, input, select { font: inherit; color: inherit; }`
- A single global focus style: `:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }` and `:focus:not(:focus-visible) { outline: none; }` (NFR-8).
- `@media (prefers-reduced-motion: reduce)` — disable transitions and animations.

No component styles in this layer.

---

## Task 4 — API client

Create `src/api/client.ts`:

```ts
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    message: string,
    readonly errors?: Record<string, string[]>
  ) { super(message); }
}

export async function api<T>(path: string, init?: RequestInit): Promise<T>;
```

Rules:

1. Always `credentials: 'include'`.
2. JSON bodies get `Content-Type: application/json`; `FormData` bodies get **no**
   explicit content type — the browser sets the multipart boundary.
3. Non-2xx → parse the `ApiError` shape from `API-CONTRACT.md` and throw
   `ApiError`. If the body is not JSON, throw with code `network_error` and the
   message "Wopcorn's server is not responding."
4. `204` → resolve `undefined`.
5. On `401`, clear the auth store and redirect to `/login` — **except** for
   `GET /api/auth/me`, which is allowed to 401 during boot. Pass an
   `allow401` option for that one call.
6. Accept an `AbortSignal` and let `AbortError` propagate untouched; fe-06
   depends on cancelling superseded searches.

Create `src/api/types.ts` holding the TypeScript types from `API-CONTRACT.md`
verbatim. Every other file imports from there — do not redeclare them.

---

## Task 5 — Stores

`src/stores/auth.ts` — state `{ user: UserSummary | null, status: 'loading' | 'ready' }`,
actions `boot()`, `register()`, `login()`, `logout()`, `updateProfile()`,
`uploadAvatar()`. `boot()` calls `GET /api/auth/me` once with `allow401`.

`src/stores/config.ts` — fetches `GET /api/config` once at boot and holds it.
Exposes `posterUrl(path, targetWidthPx)` (fe-06 uses it heavily) and the
attribution text.

`src/stores/theme.ts` — `'system' | 'light' | 'dark'`, persisted, applies the
`data-theme` attribute.

---

## Task 6 — Router and app boot

`src/router/index.ts`, `createWebHistory`. Routes:

| Path | Name | Auth | Notes |
|---|---|---|---|
| `/` | feed | required | Discovery placeholder until fe-07 |
| `/search` | search | required | |
| `/watched` `/watchlist` `/queue` | lists | required | One component, list from the route |
| `/film/:tmdbId` | film | required | `props: true`, `tmdbId` cast to Number |
| `/friends` | friends | required | |
| `/u/:userId` | profile | required | |
| `/me` | me | required | |
| `/login` `/register` | auth | anonymous only | Redirect to `/` when signed in |

A global `beforeEach` guard awaits `auth.boot()` on the first navigation, then
redirects unauthenticated users to `/login` with `?next=<intended path>`. After a
successful login, honour `next`.

`src/main.ts` creates the app, installs Pinia and the router, imports
`tokens.css` then `base.css`, and mounts. Do not render the shell until
`auth.status === 'ready'` — a signed-in user must never see the login screen
flash.

**Verify:** hard-refresh on `/watched` while signed in lands on `/watched`, not `/login`.

---

## Task 7 — App shell

`src/components/AppShell.vue` wraps every authenticated route.

**Mobile (< 900px):** a fixed bottom navigation bar, `height: var(--nav-height)`
plus `padding-bottom: env(safe-area-inset-bottom)`, `background: var(--surface)`,
1px top border. Exactly five destinations, in this order:

| Icon | Label | Route |
|---|---|---|
| house | Feed | `/` |
| magnifier | Search | `/search` |
| stacked layers | Lists | `/watched` |
| two figures | Friends | `/friends` |
| avatar or circle | You | `/me` |

Each item is a `<RouterLink>` filling `1fr` of a 5-column grid, minimum 44px
square, icon above an 11px label. The active item uses `--accent`; inactive uses
`--text-muted`. Active state must not be colour-only — the active icon is filled,
inactive is outlined (NFR-9).

The main content area gets `padding-bottom: calc(var(--nav-height) + env(safe-area-inset-bottom))`
so the bar never covers content.

**Desktop (≥ 900px):** the same five destinations become a left sidebar,
`width: var(--sidebar-width)`, icon + label in a row, and the content area is
`max-width: var(--content-max)` centred. This is a media query on the same
component, not a second component.

The Friends item renders a **pending-request badge** — a dot with a count, driven
by a `friends` store that fe-07 populates. Build the slot and the store field
now; it stays at 0 until then.

Icons: inline SVG single-file components in `src/components/icons/`, 24×24,
`stroke="currentColor"`, `fill="none"` for outline and `fill="currentColor"` for
filled. **No emoji, no icon library.**

---

## Task 8 — Auth screens

`src/views/LoginView.vue` and `RegisterView.vue`. Centred single column,
`max-width: 360px`, the wordmark in `--font-display` above the form.

- Inputs are at least 44px tall with visible `<label>` elements — not
  placeholder-only labelling (NFR-8).
- `type="email"`, `autocomplete="email"`, `autocomplete="current-password"` /
  `"new-password"`, `type="password"`.
- Submit disables while in flight and shows a spinner in place of the label.
- On `ApiError` with an `errors` map, render each message under its field,
  matching the key case-insensitively. `display_name_taken` (409) must land
  **under the display-name field**, not in a banner.
- Any other failure renders one banner above the form using the error's
  `message`.

Do not store tokens, user objects, or "remember me" flags in `localStorage`. The
session is the server's cookie (FR-A4); duplicating it client-side creates a
second source of truth that will go stale.

**Verify:** register → close the browser entirely → reopen → still signed in.

---

## Task 9 — Profile screen

`src/views/MeView.vue` (FR-A7): display-name rename with the same validation and
409 handling as registration; avatar upload via `PUT /api/me/avatar` as
`FormData` with field name `file`; a theme selector (System / Light / Dark); a
sign-out button; and the TMDB attribution block (FR-B9) using the text and logo
from `GET /api/config`.

Show a local `URL.createObjectURL` preview the instant a file is chosen, then
replace it with the returned `avatarUrl` and revoke the object URL. Reject files
over 2 MB client-side with a clear message rather than letting the server 400.

fe-06 adds the rating histogram to this screen — leave a slot for it.

---

## Task 10 — Shared UI components

Build these once; fe-06 and fe-07 consume them.

| Component | Contract |
|---|---|
| `BaseButton.vue` | Variants `primary` (accent fill), `secondary` (border), `ghost`. Min height 44px. `loading` prop swaps the label for a spinner and sets `aria-busy`. |
| `BaseSheet.vue` | Bottom sheet on mobile, centred dialog ≥900px. Uses `<dialog>` with `showModal()`, focus trapped, `Esc` closes, focus returns to the opener. Never use `alert`/`confirm`. |
| `EmptyState.vue` | Icon, headline, one-sentence explanation, optional action slot. |
| `ErrorState.vue` | Takes an `ApiError`. For code `tmdb_unavailable`, headline "Film search is unavailable" and body "TMDB isn't responding right now. Your lists, ratings, and friends are unaffected." plus a Retry button. Never render a blank region on failure (NFR-10). |
| `SpinnerBlock.vue` | Centred, `role="status"`, `aria-label="Loading"`. |

---

## Task 11 — PWA

`public/manifest.webmanifest`:

```json
{
  "name": "Wopcorn",
  "short_name": "Wopcorn",
  "start_url": "/",
  "scope": "/",
  "display": "standalone",
  "background_color": "#12100E",
  "theme_color": "#12100E",
  "icons": [
    { "src": "/icons/icon-192.png", "sizes": "192x192", "type": "image/png" },
    { "src": "/icons/icon-512.png", "sizes": "512x512", "type": "image/png" },
    { "src": "/icons/icon-maskable-512.png", "sizes": "512x512", "type": "image/png", "purpose": "maskable" },
    { "src": "/icons/icon.svg", "type": "image/svg+xml", "sizes": "any" }
  ]
}
```

Link it from `index.html` along with `<meta name="theme-color">` (one per colour
scheme via `media`) and `<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">`
— `viewport-fit=cover` is what makes `env(safe-area-inset-bottom)` work.

Write `public/sw.js` **by hand** — do not add `vite-plugin-pwa`:

- `install`: cache `/` and `/index.html` only.
- `activate`: delete caches whose name is not the current version constant.
- `fetch`: **only** handle `request.mode === 'navigate'`, serving network-first
  with the cached shell as fallback. Every other request, including anything
  under `/api/`, must fall through untouched.

Offline is explicitly out of scope (FR-H7). A service worker that caches API
responses would serve stale lists and ratings, which is worse than not working
offline at all.

Register it from `main.ts` in production builds only.

**Verify:** `npm run build`, run the server, install to a phone home screen, confirm it opens standalone with the right icon; then confirm in DevTools that no `/api/` request is served from the service worker.

---

## Done when

- [ ] No template file remains anywhere in `src/`
- [ ] Both themes work, follow the OS by default, and the toggle overrides both ways
- [ ] Register → sign out → sign in → restart browser → still signed in
- [ ] `display_name_taken` renders under the display-name field
- [ ] Deep links work in dev and from the production build
- [ ] Installs to a home screen, opens standalone
- [ ] No horizontal scroll at 320px on any screen
- [ ] `npm run type-check`, `npx eslint .`, and `npm run test:unit` all clean
- [ ] Tests from `00-testing.md` for `api/client.ts` pass
