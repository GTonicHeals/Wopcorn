/**
 * The type-to-filter box on Watched and Watchlist.
 *
 * This narrows the rows already on screen — it is not a search. The list has
 * been fetched, the titles are in the store, and the whole point is that the
 * answer arrives on the keystroke rather than after a round trip. The Search
 * screen is the one that reaches TMDB.
 *
 * It matches a title's **own** name and nothing else. A season's name is what
 * TMDB gave it, which is almost always `Season 2` — so typing a series name will
 * not turn up its seasons, the same way the season's card does not print the
 * series name either. Reaching through `parentKey` would only work when the
 * series happens to be on the same list, and a filter that behaves differently
 * depending on what else you have added is worse than one that is merely narrow.
 */

/**
 * Casefolded, unaccented, and with every run of non-alphanumerics collapsed to a
 * single space: `"X-Men: Days of Future Past"` → `"x men days of future past"`,
 * `"Amélie"` → `"amelie"`.
 *
 * Punctuation is the whole reason this exists. Nobody types the colon in
 * `Léon: The Professional` or the hyphen in `Spider-Man`, and a filter that
 * required them would fail on exactly the titles people reach for it with.
 */
export function normalizeForSearch(value: string): string {
  return (
    value
      .normalize('NFD')
      /*
       * Nonspacing marks — the combining accents NFD just split off — and not
       * `\p{Diacritic}`, which also covers spacing characters that are really
       * punctuation. U+00B7 MIDDLE DOT is one, so `Wall·E` normalized under
       * `\p{Diacritic}` came out `walle` rather than `wall e`.
       */
      .replace(/\p{Mn}/gu, '')
      .toLowerCase()
      .replace(/[^\p{Letter}\p{Number}]+/gu, ' ')
      .trim()
  );
}

/** `"x men"` → `"xmen"`. See `matchesQuery`. */
function despace(value: string): string {
  return value.replace(/ /g, '');
}

/**
 * Whether `title` should survive the filter `query`. A blank query matches
 * everything, so the caller does not need to special-case an empty field.
 *
 * Two passes, in order of precision:
 *
 * 1. The normalized substring — `"x men"` finds `X-Men`, `"x men"` finds
 *    `X-Men: Days of Future Past`.
 * 2. The same comparison with the spaces dropped from both sides, which is what
 *    makes `"xmen"` and `"spiderman"` work. It can reach across a word boundary
 *    and over-match (`"x men"` also finds an `Ax Mental`), which on a list you
 *    can see all of is the cheaper mistake — the alternative is not finding
 *    something you know you own.
 */
export function matchesQuery(title: string, query: string): boolean {
  const needle = normalizeForSearch(query);
  if (needle === '') return true;

  const haystack = normalizeForSearch(title);
  return haystack.includes(needle) || despace(haystack).includes(despace(needle));
}
