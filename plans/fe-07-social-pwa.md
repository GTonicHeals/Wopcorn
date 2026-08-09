# fe-07 — Friends, feed, profiles, taste match, final hardening

**Executor:** Opus 5 · **Depends on:** fe-06, be-04 · **Blocks:** nothing

Reuse `FilmCard`, `BaseSheet`, `EmptyState`, and `ErrorState` from the earlier
plans. Build no new visual language here.

---

## Task 1 — Friends screen (FR-F1…FR-F4)

`src/views/FriendsView.vue`, fed by one `GET /api/friends` call.

Three sections in this order — incoming requests first, because they need action:

1. **Requests to you** — each row: avatar, display name, Accept and Decline
   buttons. Hidden entirely when empty.
2. **Your friends** — avatar, display name, taste match (task 4), tapping opens
   `/u/:userId`. A "Remove" action lives behind an overflow button with a
   `BaseSheet` confirm, never as a bare button next to the name.
3. **Sent requests** — display name and a muted "Pending" label, with a cancel
   action.

Above all three, a search field calling `GET /api/users/search`. Each result
carries a `relationship`, and the button must reflect it rather than optimistically
saying "Add" and failing with a 409:

| `relationship` | Button |
|---|---|
| `none` | **Add friend** → `POST /api/friends/requests` |
| `request_sent` | **Pending** (disabled) |
| `request_received` | **Accept** → accept that request |
| `friends` | **Friends** (disabled) |

Populate the `friends` store's pending count so the shell badge from fe-05 lights
up (FR-F4). Refresh it after every accept/decline.

---

## Task 2 — Feed (FR-G2, FR-G3)

`src/views/FeedView.vue` takes over `/`, replacing the discovery rows. Move the
discovery rows to the bottom of the feed as a "Browse" section so a user with no
friends still has something to do — that is also this screen's empty state, and
it must include a prominent link to `/friends`.

- `GET /api/feed?cursor=&limit=20`, keyset paginated. Keep the opaque cursor
  exactly as received; never construct one.
- Load more via an `IntersectionObserver` sentinel **plus** a visible "Load more"
  button that does the same thing. Infinite scroll alone strands keyboard and
  screen-reader users, and the button is also the recovery path when the observer
  misfires (NFR-8).
- Guard against double-firing: ignore the sentinel while a request is in flight,
  and stop entirely when `nextCursor` is null.

Each item is one line of text above a `FilmCard`, and the four kinds must not
look identical:

| `kind` | Line |
|---|---|
| `rated` | "**Ada** rated" + inline read-only stars |
| `watched` | "**Ada** watched" |
| `added_watchlist` | "**Ada** added to their watchlist" |
| `added_queue` | "**Ada** queued" |

Plus a relative timestamp ("2h ago") with the absolute time in a `title`
attribute and a `<time datetime>` element.

Because the item reuses `FilmCard`, the viewer's own membership and rating show
on every feed entry — that is deliberate. Seeing a friend rate something already
in your queue should read without a tap.

---

## Task 3 — Friend profile (FR-G1)

`src/views/ProfileView.vue` at `/u/:userId`, from
`GET /api/friends/{userId}/profile`.

Header: avatar, display name, "Friends since", taste match. Then the rating
statistics using the **same histogram component** as `/me` (fe-06 task 9). Then a
segmented control over their Watched / Watchlist / Queue, rendering
`GET /api/friends/{userId}/lists/{list}` through the same list component as
fe-06.

On each entry, the friend's rating renders as read-only stars, while the card's
membership toggles still act on **your** lists — the server decorates for the
requester, so no special handling is needed, but do not accidentally show their
rating in the `myRating` slot.

A `403` on any of these routes means the friendship ended mid-session. Show
`ErrorState` with "You're no longer friends with this person" and a link back to
`/friends` — not a generic failure.

---

## Task 4 — Taste match (FR-G5, FR-G6)

This requirement is about honesty, so the display rules are strict.

- `score` is **never** rendered without `sharedCount` in the same visual unit.
- When `qualified` is `false`, do **not** show the percentage at all. Render
  "Not enough overlap yet — 3 films in common" (or "No films in common yet" when
  `score` is null).
- When `qualified` is `true`, render `"78% match"` with "based on 24 films" in
  `--text-muted` immediately beneath, at `--text-xs`.
- Never sort, rank, or highlight friends by an unqualified score.

A single `TasteMatch.vue` component enforces all of the above. Both the friends
list and the profile header use it — do not re-implement the formatting inline,
because that is how the unqualified case leaks into the UI.

---

## Task 5 — Verify FR-G7

Nothing to build; confirm by hand:

- Unrate a film → it leaves the friend's feed.
- Remove a film from a list → the corresponding feed item disappears.
- Unfriend someone → their items vanish from your feed immediately.

---

## Task 6 — Final hardening pass

This is a full-app audit, not a pass over this plan's screens only. Walk every
route.

| Requirement | Check |
|---|---|
| FR-H2, FR-H5 | Every route at 320, 375, 768, 1280px. No horizontal page scroll anywhere. Discovery rows and cast rows scroll internally only. |
| FR-H3 | Bottom nav reachable one-handed; no primary action stranded at the top of a long screen. |
| FR-H4 | Every interactive target ≥ 44×44px — including filter chips, star row, avatar buttons, queue handles, "Load more", and the sheet close button. Measure in DevTools; do not eyeball. |
| FR-H6 | Every poster uses `posterUrl` with a real target width and `loading="lazy"`. No `original` in a grid. |
| NFR-8 | Full keyboard traversal of every flow with a visible focus ring. Sheets trap focus and restore it on close. Queue move up/down works without a mouse. |
| NFR-9 | Re-check contrast on anything added after fe-05, especially text over backdrops and posters. |
| NFR-10 | Every fetch has loading, empty, and error states. Point `Tmdb:BaseUrl` at an unroutable host and walk the whole app — lists, ratings, feed, friends, and profiles must all still work, because none of them need TMDB at read time. |
| FR-H1, FR-H7 | Install from the production build; confirm standalone launch, icon, theme colour, and that no `/api/` request is served by the service worker. |
| FR-B9 | Attribution present on the film detail view and `/me`. |

Then: `npm run type-check` clean, `npx eslint .` clean, `npm run test:unit`
green, `npm run build` succeeds, and the built output serves correctly from
`dotnet run --project Wopcorn.Server --launch-profile https` with no Vite process
running.

---

## Done when

- [ ] Friend request round trip works from both sides, with correct button states
- [ ] Pending requests light the shell badge
- [ ] Feed paginates with no duplicates and no full-history load
- [ ] Undone actions disappear from friends' feeds
- [ ] Taste match never shows an unqualified score
- [ ] A non-friend's data is unreachable from the UI, and a mid-session unfriend
      is handled gracefully
- [ ] The entire hardening table above passes
