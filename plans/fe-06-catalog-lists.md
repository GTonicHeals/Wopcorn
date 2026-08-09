# fe-06 — Search, film detail, list views, queue, star rating

**Executor:** Opus 5 · **Depends on:** fe-05, be-02, be-03 · **Blocks:** fe-07

Layout and interaction decisions are **already made**. Implement the numbers as
written. Use the tokens from fe-05; introduce no new colours.

**Design reference:** `../design/wopcorn-mockup.html` renders this plan's
screens — Feed, Film detail, Queue, and Lists. Consult it for composition;
**this plan's text wins on every value** where the two disagree (see the
precedence note in fe-05).

---

## Task 1 — `posterUrl` (FR-H6)

In `src/stores/config.ts`:

```ts
posterUrl(path: string | null, targetWidthPx: number): string | null
```

Multiply `targetWidthPx` by `window.devicePixelRatio` (cap at 3), then pick the
**smallest** size in `posterSizes` whose numeric width is ≥ that value, falling
back to the largest available. Return `null` for a null path so callers render a
placeholder rather than a broken image. Same helper shape for backdrops and
profiles.

Unit-test this: DPR 1, 2, and 3 at 138px must select `w154`, `w342`, and `w500`.

---

## Task 2 — `FilmCard.vue`

The single most reused component in the app. This layout satisfies FR-C2, FR-C3,
and FR-H4 simultaneously — do not redesign it.

```
┌──────────────────┐
│                  │  poster, aspect-ratio 2/3
│                  │  border-radius var(--radius-md)
│                  │  1px solid var(--poster-edge)  ← contains bright posters
│                  │  loading="lazy"
├──────────────────┤
│ Title, 2 lines   │  --text-sm, --font-ui, line-clamp 2
│ 2021 · ★7.2      │  --text-xs, --text-muted
│ ★★★☆☆            │  only when myRating != null, in --accent
├────┬────┬────────┤
│ 👁  │ 🔖 │  ≡     │  three toggles, each flex:1, height 44px, gap 4px
└────┴────┴────────┘     (icons are inline SVG — the glyphs above are notation)
```

- Grid: `repeat(auto-fill, minmax(138px, 1fr))`, gap `var(--space-3)`, page
  padding `var(--space-4)`. At 320px that yields two columns at 138px each.
- Reserve the poster box with `aspect-ratio: 2 / 3` **before** the image loads so
  the grid never reflows (NFR-2).
- The three action buttons are Watched / Watchlist / Queue, in that order.
  Member state = `--accent` background with `--accent-ink` icon **and** a filled
  icon variant; non-member = transparent with a `--border` outline and an outline
  icon. Never signal membership by colour alone (NFR-9).
- Each button carries `aria-pressed` and an `aria-label` like
  `"Watched — Dune (2021)"`. That is the whole of FR-C3's "at a glance"
  requirement for screen-reader users.
- One tap toggles that list — no sheet, no navigation (FR-C2). Tapping the poster
  or title navigates to `/film/:tmdbId`.

### Missing posters

When `posterPath` is `null`, render a **duotone placeholder**, not a broken
image or a grey box: a `linear-gradient(160deg, A, B)` chosen deterministically
from the film's release decade, with the title set small in the display face —
`--font-display`, 11px, uppercase, `letter-spacing: 0.24em`, white at 50%
opacity, bottom-left with 8px inset. Decade → gradient:

| Decade | A → B |
|---|---|
| pre-1970 | `#8A8560` → `#1C1B10` |
| 1970s | `#5A5A3C` → `#222416` |
| 1980s | `#4A7DA6` → `#0A1522` |
| 1990s | `#7A1E2B` → `#1E060A` |
| 2000s | `#3FA08A` → `#0A241C` |
| 2010s+ / unknown year | `#D98A3E` → `#2B1608` |

These are poster-area colours, not UI tokens — they carry no text and need no
contrast check. The placeholder keeps the same `aspect-ratio: 2 / 3`, radius,
and `--poster-edge` border as a real poster.

### The Watched special case (FR-C6)

When the Watched button is tapped **and** the film is currently on Watchlist or
Queue, open a `BaseSheet` with a checkbox per source list, pre-checked, and one
confirm button. Send a single `PUT /api/lists/watched/{id}` with
`alsoRemoveFrom`. When the film is on neither, add it immediately with no
prompt. Do not show the sheet in the case where it has nothing to ask.

All list mutations are **optimistic**: update the store, fire the request, and on
failure revert and show a toast with the error's `message`.

---

## Task 3 — `StarRating.vue` (FR-E1, FR-E2, FR-E5)

Ten values, half-star granularity, and it must be reliable under a thumb. The
naive implementation — ten 14px targets — fails; this one does not.

- The component is a single **56px-tall full-width row**; the five star glyphs are
  32px and centred within it. The hit area is the row, not the glyphs.
- Value from a pointer position:
  `value = clamp(Math.ceil(((clientX - rect.left) / rect.width) * 10), 1, 10)`.
- Handle `pointerdown`, `pointermove` (only while captured, via
  `setPointerCapture`), and `pointerup`. Show a live preview of the value under
  the pointer during the drag and commit on release. Dragging along the row —
  not precise tapping — is the primary gesture.
- Render half-stars with two clipped layers, not a font glyph.
- Accessibility (NFR-8): `role="slider"`, `aria-valuemin="1"`,
  `aria-valuemax="10"`, `aria-valuenow`, and
  `aria-valuetext="3 and a half stars"`. Arrow keys step by 1, Home/End jump to
  the ends, and the control is in the tab order.
- A clear affordance sits beside the row (`DELETE /api/films/{id}/rating`,
  FR-E4). Do not overload "tap the current value again" to mean clear.

Rating a film not on Watched adds it there server-side (FR-E3); reflect that in
the store immediately so the Watched toggle fills in the same frame.

Unit-test the pointer→value mapping at both edges and at every tenth.

---

## Task 4 — Search (FR-B1, FR-B2, NFR-1)

`src/views/SearchView.vue`.

- The input is `position: sticky; top: 0` under the status bar, at least 44px
  tall, `type="search"`, `enterkeyhint="search"`, `autocomplete="off"`. On mobile
  it sits at the **top** of the content area — the bottom nav already occupies
  thumb space, and a second bottom element would collide with the keyboard.
- Debounce 250 ms after the last keystroke.
- Every request carries an `AbortController`; abort the previous one on each new
  query. Additionally track a monotonically increasing request id and **discard
  any response whose id is lower than the newest already rendered** — aborting
  alone does not close the race.
- While the next result set loads, keep the previous results on screen with
  reduced opacity. Do not blank the grid.
- Empty `q` shows an `EmptyState` prompting a search; zero results shows one
  naming the query.

---

## Task 5 — Discovery (FR-B4)

Until fe-07 replaces `/` with the feed, the home route shows three horizontally
scrolling rows: Popular, Top Rated, Now Playing, from
`GET /api/films/discover/{feed}`.

Each row is `overflow-x: auto` with `scroll-snap-type: x mandatory`,
`-webkit-overflow-scrolling: touch`, and a hidden scrollbar. The **page** must
not scroll horizontally (FR-H5) — only the row does. Cards in a row are a fixed
138px wide.

---

## Task 6 — Film detail (FR-B3)

`src/views/FilmView.vue`.

- Backdrop at the top, full-bleed, `aspect-ratio: 16 / 9`, with the `--scrim`
  gradient over its lower half. The title sits on the scrim in `--font-display`
  at `--text-2xl`. Verify contrast against a **light** backdrop image, not just a
  dark one — if it cannot be made to pass, move the title below the image
  (NFR-9).
- Meta row: year · runtime formatted `2h 35m` · genres as chips.
- TMDB score, the user's `StarRating`, and the three list toggles grouped
  together directly under the meta row — the primary actions must be reachable
  without scrolling on a 320×568 screen.
- Synopsis, then director, then cast as a horizontally scrolling row of profile
  images with name and character.
- `friendsWatched` renders here (fe-07 populates it) directly beneath the TMDB
  score, so the upstream score and friends' opinions read as one unit.
- When `stale` is true, show a quiet inline note — "Showing saved details; TMDB
  is unreachable" — with a retry that calls `POST /api/films/{id}/refresh`. This
  is not an error state.
- TMDB attribution in the footer (FR-B9).

---

## Task 7 — List views (FR-C4, FR-C5)

One component, `src/views/ListsView.vue`, for all three lists; the route decides
which. A segmented control at the top switches between Watched / Watchlist /
Queue and pushes the corresponding route.

- Header shows the entry count from the response, and when a filter is active,
  "showing 12 of 84".
- Sort control: date added, title, release year, runtime, TMDB score, and — on
  Watched only — rating. Direction toggle beside it. **The Queue hides the sort
  control entirely** and shows the preset control from task 8 instead.
- Filters: genre (multi-select from `GET /api/genres`) and release decade
  (multi-select, derived from the entries present). Both live in a `BaseSheet`;
  active filters render as removable chips above the grid so the state is visible
  without opening the sheet.
- When filters produce nothing, the `EmptyState` names the active filters and
  offers a "Clear filters" action.

**Performance (NFR-2):** render 200 real entries and measure before adding a
virtualization library. With `aspect-ratio` boxes, `loading="lazy"`, and
`content-visibility: auto` on the grid items, 200 cards should scroll smoothly.
Only if measurement says otherwise, add windowing — and say so in the handoff.

---

## Task 8 — Queue (FR-D1…FR-D5)

`vuedraggable@^4.1.0` is installed and **verified working under Vue 3.5** — the
bare `vuedraggable` name on npm still resolves to the Vue 2 build, so keep the
`^4.1.0` constraint if you ever touch it.

```vue
<draggable v-model="entries" item-key="tmdbId" handle=".drag-handle"
           :animation="150" :delay="0" :touch-start-threshold="5"
           force-fallback ghost-class="queue-ghost" @end="persist">
```

`force-fallback` is required — the native HTML5 drag implementation is
unreliable on iOS Safari. The handle is an explicit grip element at least 44px
square on the right of each row, so dragging never fights vertical scrolling.

Rows are a single-line layout here, not the grid card: small poster (46×69),
title, and the handle.

### Queue numerals and the "Up next" hero — the app's signature

The queue is the only ordered list in the app, so it is the **only place
numerals appear**. Do not add position numbers, step markers, or numbered
sections anywhere else.

**Rows 2..n** each carry their position as a numeral: `--font-display`, 24px,
`--text-muted`, lining figures, in a 26px column left of the poster.

**Position 1 renders as the "Up next" hero** instead of a plain row, whenever
the queue is non-empty:

- A card, `border-radius: var(--radius-lg)`, `aspect-ratio: 16 / 10`, 1px
  `--poster-edge` border, full content width.
- Background: the film's backdrop via the fe-06 task 1 helper (backdrop sizes),
  falling back to the decade duotone from task 2 when `backdropPath` is null.
- A bottom scrim (`var(--scrim)`, covering the lower ~65%), and on it:
  an eyebrow — the text "Up next", 11px, uppercase,
  `letter-spacing: 0.26em`, `--accent`, weight 700; the title in
  `--font-display` at 28px in `var(--text)`; a meta line at 12px
  `--text-muted`: year · runtime · "added {date}".
- Text sits on the scrim, so `var(--text)` stays legible in both themes; verify
  against a light backdrop image, same rule as the film-detail hero (NFR-9).
- Tapping the hero navigates to `/film/:tmdbId`. It is not itself a drag row.

**Reordering across the hero boundary:** the draggable list holds positions
2..n. Dropping a row at the very top of that list — or pressing "Move up" on
row 2 — promotes it to position 1; the previous №1 becomes row №2. With exactly
one entry, the queue shows only the hero; with zero, the usual `EmptyState`.

**Persistence (FR-D5):** reorder the local array immediately, then
`PUT /api/queue/order` with the complete list of ids. On `409
queue_out_of_sync`, replace local state with the `tmdbIds` from the response and
show a toast: "Your queue changed elsewhere — showing the latest order." Never
silently discard the server's answer.

**Non-drag fallback (NFR-8) — mandatory, not optional.** Each row carries "Move
up" and "Move down" buttons, reachable by keyboard and screen reader, writing the
same endpoint. A drag-only reorder fails the accessibility requirement outright.

**Presets (FR-D3):** a control offering date added, title, runtime, TMDB score,
calling `POST /api/queue/sort`. Because this **overwrites** a hand-made order,
confirm first via `BaseSheet` — "This replaces your current queue order" — but
only when the queue has more than one entry. Entries stay draggable afterwards
(FR-D4); nothing special is needed for that, positions are just integers.

---

## Task 9 — Rating statistics (FR-E6)

On `/me`, in the slot fe-05 left: a ten-bucket histogram from
`GET /api/me/rating-stats`, plus count and average rendered in stars.

Draw it as plain CSS bars — ten rows, label on the left, bar width as a
percentage of the largest bucket, count on the right. No chart library. Bars use
`--accent`. Give the whole thing a text alternative (a visually hidden list of
"4 stars: 12 films") since a bar chart is not readable by a screen reader.

---

## Task 10 — Stores

`src/stores/films.ts` — a `Map<number, FilmCard | FilmDetail>` shared by every
view, so search → detail → back does not refetch. Mutation responses write into
this map.

`src/stores/lists.ts` — per-list entry arrays, plus the optimistic add/remove
and rating actions. Every mutation endpoint returns the resulting `ListEntry`;
write that into the store rather than refetching the list.

`src/stores/queue.ts` — order array plus the reconciliation logic from task 8.

---

## Done when

- [ ] Membership across all three lists is visible and one-tap on every card
- [ ] Half-star selection is reliable with a thumb on a real phone
- [ ] Marking watched from Queue/Watchlist offers removal in the same action
- [ ] Queue drags with touch and mouse **and** has working move up/down buttons
- [ ] Queue position 1 renders as the "Up next" hero; rows carry display-face numerals; numerals appear nowhere else in the app
- [ ] Films without a poster render the decade duotone placeholder, never a broken image
- [ ] A forced `409` on reorder reconciles to the server's order with a message
- [ ] A 200-entry list scrolls smoothly and issues no per-film requests
- [ ] Search survives fast typing without an older response overwriting a newer
- [ ] With TMDB unreachable, lists and ratings still work and the failure is explained
- [ ] No horizontal page scroll at 320px, including the discovery rows
- [ ] Tests from `00-testing.md` for `posterUrl`, search racing, the star mapping, and queue reconciliation pass
