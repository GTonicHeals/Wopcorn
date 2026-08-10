import type { WatchProvider } from '@/api/types';

/**
 * The small amount of reasoning the streaming surfaces share, kept out of the
 * components so it can be tested without mounting one.
 *
 * All three rules turn on the same distinction: `availableOn` and the directory
 * are two different things. `availableOn` is a list of ids the server computed
 * for this viewer; the directory is what those ids are *called*. A surface needs
 * both, and neither is much use alone.
 */

/**
 * The viewer's own services, named, in the directory's order (which is TMDB's
 * `display_priority`, so the plausible ones come first).
 *
 * Empty when nothing is configured — and every streaming control keys off that,
 * because a filter that can only ever match nothing is worse than no filter.
 */
export function viewerServices(
  directory: WatchProvider[],
  providerIds: number[]
): WatchProvider[] {
  return directory.filter((provider) => providerIds.includes(provider.id));
}

/**
 * The directory entries for a card's `availableOn`, in the directory's order.
 *
 * An id the directory cannot name is **dropped** rather than drawn as a mystery
 * square: the badge exists to be recognised, and an unrecognisable one is noise.
 */
export function namedProviders(directory: WatchProvider[], providerIds: number[]): WatchProvider[] {
  return directory.filter((provider) => providerIds.includes(provider.id));
}

/**
 * Whether a title survives a streaming filter.
 *
 * `availableOn` is already the answer the server's `service=` parameter gives —
 * the viewer's own services, flatrate, in their region — so this is an
 * intersection and nothing more. An empty selection is **no filter**, never
 * "match nothing", which is the rule every other filter in the app follows.
 */
export function matchesServices(availableOn: number[], selected: number[]): boolean {
  if (selected.length === 0) return true;
  return availableOn.some((id) => selected.includes(id));
}
