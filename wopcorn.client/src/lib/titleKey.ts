import type { MediaType } from '@/api/types';

/**
 * The title key — the one identifier the API, the router and the stores all use.
 *
 * ```
 * movie-603          a film
 * tv-1396            a series
 * tv-1396-s2         season 2 of that series
 * ```
 *
 * TMDB's film and TV ids are separate namespaces and they collide: 1396 is
 * *Mirror* (1975) as a movie and *Breaking Bad* as a series. Carrying the media
 * type in the identifier is what keeps the two apart.
 *
 * This module mirrors the server's `TitleKey` and is the **only** place the
 * format is known on this side. Nothing else builds one by concatenation or
 * reads one by slicing — the grammar is the single thing both tracks have to
 * agree on, so it is tested directly rather than through a component.
 */

export type ParsedTitleKey = {
  key: string;
  mediaType: MediaType;
  /** The TMDB id — of the **series** when this is a season. */
  tmdbId: number;
  /** TMDB's own season number; `0` is the specials season and is legal. */
  seasonNumber: number | null;
};

/**
 * The grammar, for the router's path constraint. Deliberately the same shape as
 * `parse`'s scan, and deliberately not the parser: a route match that a later
 * `parse` would reject is a bug worth not having.
 */
export const TITLE_KEY_PATTERN = /^(movie|tv)-(\d+)(?:-s(\d+))?$/;

/** Rejects `+7`, ` 7` and `007` — two spellings of one key would be two entries. */
function isCanonicalNumber(raw: string): boolean {
  return /^(0|[1-9]\d*)$/.test(raw);
}

/** `null` for anything that is not a well-formed key. Never throws. */
export function parse(key: string | null | undefined): ParsedTitleKey | null {
  if (typeof key !== 'string') return null;

  const match = TITLE_KEY_PATTERN.exec(key);
  if (!match) return null;

  const [, prefix, rawId, rawSeason] = match;
  if (rawId === undefined || !isCanonicalNumber(rawId)) return null;
  if (rawSeason !== undefined && !isCanonicalNumber(rawSeason)) return null;

  const tmdbId = Number(rawId);
  if (!Number.isSafeInteger(tmdbId)) return null;

  if (prefix === 'movie') {
    // `movie-1-s2` is not a film with a season; it is not a key at all.
    if (rawSeason !== undefined) return null;
    return { key, mediaType: 'movie', tmdbId, seasonNumber: null };
  }

  if (rawSeason === undefined) {
    return { key, mediaType: 'series', tmdbId, seasonNumber: null };
  }

  return { key, mediaType: 'season', tmdbId, seasonNumber: Number(rawSeason) };
}

export function isValid(key: string | null | undefined): boolean {
  return parse(key) !== null;
}

/** The canonical string for a media type and its parts. */
export function format(
  mediaType: MediaType,
  tmdbId: number,
  seasonNumber?: number | null
): string {
  if (mediaType === 'movie') return `movie-${tmdbId}`;
  if (mediaType === 'series') return `tv-${tmdbId}`;
  return `tv-${tmdbId}-s${seasonNumber ?? 0}`;
}

export function isSeason(key: string | null | undefined): boolean {
  return parse(key)?.mediaType === 'season';
}

export function isSeries(key: string | null | undefined): boolean {
  return parse(key)?.mediaType === 'series';
}

/** A season's series; `null` for a film, a series, or a key that does not parse. */
export function parentOf(key: string | null | undefined): string | null {
  const parsed = parse(key);
  return parsed?.mediaType === 'season' ? format('series', parsed.tmdbId) : null;
}

/** The route a title's own screen lives at. One place builds it. */
export function titlePath(key: string): string {
  return `/title/${key}`;
}
