import { episodeCount, formatRuntime, formatYear, metaLine, seasonCount } from '@/lib/format';
import type { TitleCard } from '@/api/types';

/**
 * The meta line under a title, which reads differently per media type.
 *
 * ```
 * film    2021 · 2h 35m
 * series  2008 · 5 seasons
 * season  Season 2 · 13 episodes
 * ```
 *
 * A series shows its season count rather than a runtime because its runtime is
 * usually null — TMDB's `episode_run_time` is frequently empty — and a meta line
 * that is just a year reads like missing data rather than a series.
 *
 * A season leads with its own number instead of a year: within a series screen
 * the year is the same on every row and the number is the thing being chosen.
 * Written once here so the card, the list row and the queue row cannot drift.
 */
export function titleMeta(title: TitleCard): string {
  if (title.mediaType === 'series') {
    return metaLine([
      formatYear(title.releaseYear),
      title.seasonCount ? seasonCount(title.seasonCount) : null,
      // Only when TMDB actually gave one, which for a series is the exception.
      formatRuntime(title.runtimeMinutes)
    ]);
  }

  if (title.mediaType === 'season') {
    return metaLine([
      title.seasonNumber === null ? null : `Season ${title.seasonNumber}`,
      title.episodeCount ? episodeCount(title.episodeCount) : null
    ]);
  }

  return metaLine([formatYear(title.releaseYear), formatRuntime(title.runtimeMinutes)]);
}

/**
 * The compact form for a dense row, where the runtime is a separate column or
 * simply too much: year and type-defining count only.
 */
export function titleMetaShort(title: TitleCard): string {
  if (title.mediaType === 'series') {
    return metaLine([
      formatYear(title.releaseYear),
      title.seasonCount ? seasonCount(title.seasonCount) : null
    ]);
  }

  if (title.mediaType === 'season') {
    return metaLine([
      title.seasonNumber === null ? null : `Season ${title.seasonNumber}`,
      title.episodeCount ? episodeCount(title.episodeCount) : null
    ]);
  }

  return metaLine([formatYear(title.releaseYear), formatRuntime(title.runtimeMinutes)]);
}

/**
 * `"3 / 5 seasons"` for a series the viewer has watched some of, and `null`
 * otherwise — including a series with **no** watched seasons, which has nothing
 * to report and should not carry an accent for a zero.
 *
 * Nothing cascades: this is a count of independent season entries, not a claim
 * about the series entry itself, which is why `5 / 5` does not imply the series
 * is marked watched.
 */
export function seasonProgressLabel(
  progress: { watched: number; total: number } | null | undefined
): string | null {
  if (!progress || progress.watched === 0 || progress.total === 0) return null;
  return `${progress.watched} / ${progress.total} seasons`;
}
