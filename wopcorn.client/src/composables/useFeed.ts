import { computed, ref } from 'vue';

import { ApiError, api } from '@/api/client';
import { useTitlesStore } from '@/stores/titles';
import type { ActivityItem, FeedResponse } from '@/api/types';

/**
 * `GET /api/feed`, keyset paginated (fe-07, task 2 — FR-G2, FR-G3).
 *
 * Four rules the server's shape forces on any client of this endpoint:
 *
 * 1. **The cursor is opaque.** It is echoed back exactly as received, never
 *    parsed, never constructed. `URLSearchParams` handles the encoding.
 * 2. **`nextCursor: null` is the only stop condition.** A page can come back
 *    short — the server drops rows whose title is missing from the cache — while
 *    there is still more behind it, so "fewer than `limit` items" proves nothing.
 * 3. **A page can repeat an id** if activity lands between requests; items are
 *    de-duplicated by id so an event never renders twice.
 * 4. **A rejected cursor restarts paging.** `400 validation_failed` means the
 *    cursor is unparseable, and the only way forward is from the top.
 *
 * Every title goes through `titles.upsertMany` and is rendered from the store,
 * so a list toggle pressed on a feed card stays in sync with the same title on the
 * search, list, and detail screens.
 *
 * Lives in a composable rather than the view so the paging can be tested without
 * a DOM, a router, or an `IntersectionObserver`.
 */

export const FEED_PAGE_SIZE = 20;

export type FeedStatus = 'idle' | 'loading' | 'ready' | 'error';

export function useFeed() {
  const titles = useTitlesStore();

  const items = ref<ActivityItem[]>([]);
  const status = ref<FeedStatus>('idle');
  const error = ref<ApiError | null>(null);

  /** Verbatim from the last response. Never inspected, never built. */
  const nextCursor = ref<string | null>(null);
  const hasMore = ref(true);
  const loadingMore = ref(false);

  const seen = new Set<string>();
  let inFlight: Promise<void> | null = null;

  const isEmpty = computed(() => status.value === 'ready' && items.value.length === 0);

  function forget(): void {
    items.value = [];
    seen.clear();
    nextCursor.value = null;
    hasMore.value = true;
  }

  function absorb(page: FeedResponse): void {
    // The store is the single source of truth for every card in the app.
    titles.upsertMany(page.items.map((item) => item.title));

    const fresh = page.items.filter((item) => !seen.has(item.id));
    for (const item of fresh) seen.add(item.id);

    items.value = [...items.value, ...fresh];
    nextCursor.value = page.nextCursor;
    hasMore.value = page.nextCursor !== null;
  }

  function fetchPage(cursor: string | null): Promise<FeedResponse> {
    const params = new URLSearchParams({ limit: String(FEED_PAGE_SIZE) });
    if (cursor !== null) params.set('cursor', cursor);
    return api<FeedResponse>(`/api/feed?${params.toString()}`);
  }

  function isRejectedCursor(failure: unknown): boolean {
    return (
      failure instanceof ApiError &&
      failure.status === 400 &&
      failure.code === 'validation_failed'
    );
  }

  /**
   * The next page, or the first one. Concurrent callers — the sentinel and the
   * button — share one flight, which is the whole double-fire guard.
   */
  function loadMore(): Promise<void> {
    if (inFlight) return inFlight;
    if (!hasMore.value) return Promise.resolve();

    const isFirstPage = items.value.length === 0;
    if (isFirstPage) status.value = 'loading';
    else loadingMore.value = true;
    error.value = null;

    inFlight = (async () => {
      // At most one restart: a second rejection is a real failure, not a stale
      // cursor, and retrying forever would hammer the server.
      let cursor = nextCursor.value;

      for (let attempt = 0; attempt < 2; attempt++) {
        try {
          absorb(await fetchPage(cursor));
          status.value = 'ready';
          return;
        } catch (failure) {
          if (attempt === 0 && cursor !== null && isRejectedCursor(failure)) {
            // Start again from no cursor rather than stranding the feed.
            forget();
            cursor = null;
            continue;
          }

          error.value = failure instanceof ApiError ? failure : null;
          // Pages already on screen are still good; only an empty feed becomes
          // an error screen (NFR-10).
          status.value = items.value.length > 0 ? 'ready' : 'error';
          return;
        }
      }
    })().finally(() => {
      inFlight = null;
      loadingMore.value = false;
    });

    return inFlight;
  }

  /** Pull-to-refresh in spirit: drop everything and ask for page one again. */
  function refresh(): Promise<void> {
    if (inFlight) return inFlight;
    forget();
    error.value = null;
    status.value = 'idle';
    return loadMore();
  }

  return {
    items,
    status,
    error,
    hasMore,
    loadingMore,
    isEmpty,
    nextCursor,
    loadMore,
    refresh
  };
}
