import { ref } from 'vue';
import { defineStore } from 'pinia';

import { api } from '@/api/client';
import type { Genre, TitleAvailability, TitleCard, TitleDetail } from '@/api/types';

/**
 * One `Map<key, TitleCard | TitleDetail>` shared by every view (fe-06, task 10).
 *
 * Two things fall out of having exactly one copy of a title in memory:
 *
 * - search → detail → back does not refetch, and
 * - a list toggle pressed on a search result also lights up on the same title in
 *   the feed, on the title screen, and in a list — membership and `myRating` live
 *   on the title row, not on each view's copy of it.
 *
 * The map is keyed by the **canonical key string** and nothing else. Never a
 * parsed tuple, never a bare TMDB id: film and TV ids collide, so `1396` alone
 * would make *Mirror* and Breaking Bad the same entry and every toggle on one
 * would light up the other.
 *
 * Every mutation response (`ListEntry.title`) is written back through `upsert`.
 */

/** A card merged over an existing detail keeps the detail fields. */
function merge(
  existing: TitleCard | TitleDetail | undefined,
  incoming: TitleCard | TitleDetail
): TitleCard | TitleDetail {
  return existing ? { ...existing, ...incoming } : incoming;
}

export function isDetail(title: TitleCard | TitleDetail | null): title is TitleDetail {
  return title !== null && 'overview' in title;
}

export const useTitlesStore = defineStore('titles', () => {
  const byKey = ref(new Map<string, TitleCard | TitleDetail>());
  const genres = ref<Genre[]>([]);

  /** Keys whose full detail has been fetched — a card in the map is not a detail. */
  const detailLoaded = ref(new Set<string>());

  const detailRequests = new Map<string, Promise<TitleDetail>>();
  let genreRequest: Promise<void> | null = null;

  function get(key: string): TitleCard | TitleDetail | null {
    return byKey.value.get(key) ?? null;
  }

  function upsert(title: TitleCard | TitleDetail): TitleCard | TitleDetail {
    const merged = merge(byKey.value.get(title.key), title);
    byKey.value.set(title.key, merged);
    return merged;
  }

  function upsertMany(titles: (TitleCard | TitleDetail)[]): void {
    for (const title of titles) upsert(title);
  }

  /** Narrow, local edits — the optimistic path in the lists store uses this. */
  function patch(key: string, changes: Partial<TitleDetail>): void {
    const existing = byKey.value.get(key);
    if (!existing) return;
    byKey.value.set(key, { ...existing, ...changes });
  }

  /**
   * `GET /api/titles/{key}`. Deduplicated per key, and skipped entirely when the
   * map already holds a full detail — unless `force`, which is the "this looks
   * wrong" refresh (FR-B7) going through `POST .../refresh`.
   */
  async function loadDetail(key: string, force = false): Promise<TitleDetail> {
    const cached = byKey.value.get(key);
    if (!force && detailLoaded.value.has(key) && isDetail(cached ?? null)) {
      return cached as TitleDetail;
    }

    const pending = detailRequests.get(key);
    if (pending && !force) return pending;

    const request = (async () => {
      try {
        const detail = force
          ? await api<TitleDetail>(`/api/titles/${key}/refresh`, { method: 'POST' })
          : await api<TitleDetail>(`/api/titles/${key}`);

        upsert(detail);
        // A series' detail carries its seasons already decorated, so the toggles
        // on the Seasons rows have entries in the shared map without a request
        // each — and a toggle pressed there lights up on the season's own screen.
        upsertMany(seasonCards(detail));
        detailLoaded.value.add(key);
        return detail;
      } finally {
        detailRequests.delete(key);
      }
    })();

    detailRequests.set(key, request);
    return request;
  }

  /**
   * The `seasons[]` rows as cards, so they live in the same map as everything
   * else. Deliberately partial — a season's own screen fills in the rest — but
   * complete in the parts a toggle and a star control read.
   */
  function seasonCards(detail: TitleDetail): TitleCard[] {
    return detail.seasons.map((season) => ({
      key: season.key,
      mediaType: 'season' as const,
      tmdbId: detail.tmdbId,
      seasonNumber: season.seasonNumber,
      parentKey: detail.key,
      title: season.name,
      releaseYear: season.airDate ? Number(season.airDate.slice(0, 4)) : null,
      posterPath: season.posterPath,
      tmdbVoteAverage: null,
      runtimeMinutes: null,
      episodeCount: season.episodeCount,
      seasonCount: null,
      // A season has no seasons of its own to be part-way through.
      seasonProgress: null,
      // A season's genres are its series' genres.
      genreIds: detail.genreIds,
      lists: season.lists,
      myRating: season.myRating,
      // And so is where it can be watched — TMDB has no season-level providers,
      // so a season resolves to its series everywhere, including here.
      availableOn: detail.availableOn,
      // A suggestion is *not* inherited: a friend recommends a season or the
      // series, never both at once, and the series' badge on a season row would
      // offer an accept that answers the wrong thing. The season's own screen
      // fills this in.
      suggestion: null
    }));
  }

  // ---------------------------------------------------------- availability

  /**
   * `GET /api/titles/{key}/availability`, cached beside the detail and keyed the
   * same way, so navigating back to a title does not refetch it.
   *
   * Deliberately a separate call from the detail (D-3): folding it in would tie
   * a 24-hour answer to the detail's seven-day TTL and let a providers failure
   * delay the page. The title screen renders first and this arrives after.
   */
  const availabilityByKey = ref(new Map<string, TitleAvailability>());
  const availabilityRequests = new Map<string, Promise<TitleAvailability>>();

  function availability(key: string): TitleAvailability | null {
    return availabilityByKey.value.get(key) ?? null;
  }

  async function loadAvailability(key: string, force = false): Promise<TitleAvailability> {
    const cached = availabilityByKey.value.get(key);
    if (!force && cached) return cached;

    const pending = availabilityRequests.get(key);
    if (pending && !force) return pending;

    const request = (async () => {
      try {
        const answer = await api<TitleAvailability>(`/api/titles/${key}/availability`);
        availabilityByKey.value = new Map(availabilityByKey.value).set(key, answer);
        return answer;
      } finally {
        availabilityRequests.delete(key);
      }
    })();

    availabilityRequests.set(key, request);
    return request;
  }

  /** The cached TMDB genre list, for the list-view filter sheet. */
  async function loadGenres(): Promise<void> {
    if (genres.value.length > 0) return;
    if (genreRequest) return genreRequest;

    genreRequest = (async () => {
      try {
        genres.value = await api<Genre[]>('/api/genres');
      } catch {
        // A missing genre list costs the filter sheet its genre section and
        // nothing else; the lists themselves are unaffected.
      } finally {
        genreRequest = null;
      }
    })();

    return genreRequest;
  }

  function genreName(id: number): string {
    return genres.value.find((genre) => genre.id === id)?.name ?? '';
  }

  /**
   * Dropped on sign-out. Every title in the map carries `lists` and `myRating`
   * for the user who was signed in, so it must not outlive the session.
   */
  function clear(): void {
    byKey.value = new Map();
    detailLoaded.value = new Set();
    // Availability is answered for one region — the signed-out user's — so it
    // must not outlive the session either.
    availabilityByKey.value = new Map();
  }

  return {
    byKey,
    genres,
    detailLoaded,
    availabilityByKey,
    get,
    upsert,
    upsertMany,
    patch,
    loadDetail,
    availability,
    loadAvailability,
    loadGenres,
    genreName,
    clear
  };
});
