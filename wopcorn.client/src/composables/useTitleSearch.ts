import { computed, onScopeDispose, ref, watch } from 'vue';

import { ApiError, api } from '@/api/client';
import { useTitlesStore } from '@/stores/titles';
import type { MediaType, Paged, TitleCard } from '@/api/types';

/**
 * `GET /api/titles/search`, debounced and race-guarded (fe-06, task 4).
 *
 * Two separate defences, because one is not enough:
 *
 * 1. every request carries an `AbortController` and the previous one is aborted;
 * 2. every request carries a monotonically increasing id, and a response whose id
 *    is lower than the newest already rendered is **discarded**.
 *
 * Aborting alone does not close the race — a response can already be in flight,
 * or resolved and queued as a microtask, when the abort lands. The id check is
 * what actually guarantees that a slow answer for query *n-1* never overwrites
 * the results of query *n* (NFR-1).
 *
 * Lives in a composable rather than the view so the guard is testable without a
 * DOM or a router.
 */

export const SEARCH_DEBOUNCE_MS = 250;

export type SearchStatus = 'idle' | 'loading' | 'ready' | 'error';

export function useTitleSearch(types?: () => MediaType[]) {
  const titles = useTitlesStore();

  const query = ref('');
  const resultKeys = ref<string[]>([]);
  const status = ref<SearchStatus>('idle');
  const error = ref<ApiError | null>(null);
  const totalResults = ref(0);
  /** The query the currently rendered results belong to. */
  const renderedQuery = ref('');

  let nextRequestId = 0;
  let renderedRequestId = 0;
  let controller: AbortController | null = null;
  let timer: ReturnType<typeof setTimeout> | null = null;

  const results = computed(() =>
    resultKeys.value
      .map((key) => titles.get(key))
      .filter((title): title is TitleCard => title !== null)
  );

  const isLoading = computed(() => status.value === 'loading');

  function isAbort(reason: unknown): boolean {
    return reason instanceof Error && reason.name === 'AbortError';
  }

  async function execute(raw: string): Promise<void> {
    const term = raw.trim();
    const requestId = ++nextRequestId;

    controller?.abort();
    controller = null;

    if (term.length === 0) {
      renderedRequestId = requestId;
      renderedQuery.value = '';
      resultKeys.value = [];
      totalResults.value = 0;
      error.value = null;
      status.value = 'idle';
      return;
    }

    const ac = new AbortController();
    controller = ac;
    status.value = 'loading';
    error.value = null;

    try {
      // `type` is repeatable and defaults to all; only movie and series are
      // meaningful here, since TMDB has no season search.
      const params = new URLSearchParams({ q: term, page: '1' });
      for (const type of types?.() ?? []) params.append('type', type);

      const page = await api<Paged<TitleCard>>(
        `/api/titles/search?${params.toString()}`,
        { signal: ac.signal }
      );

      // A superseded response is dropped whole — including its titles, which are
      // still worth keeping in the shared map.
      titles.upsertMany(page.results);
      if (requestId < renderedRequestId) return;

      renderedRequestId = requestId;
      renderedQuery.value = term;
      resultKeys.value = page.results.map((title) => title.key);
      totalResults.value = page.totalResults;
      status.value = 'ready';
    } catch (failure) {
      if (isAbort(failure)) return;
      if (requestId < renderedRequestId) return;

      renderedRequestId = requestId;
      renderedQuery.value = term;
      error.value = failure instanceof ApiError ? failure : null;
      status.value = 'error';
    } finally {
      if (controller === ac) controller = null;
    }
  }

  function schedule(raw: string): void {
    if (timer !== null) clearTimeout(timer);
    timer = setTimeout(() => {
      timer = null;
      void execute(raw);
    }, SEARCH_DEBOUNCE_MS);
  }

  /** Skips the debounce — the Enter key and the retry button both want this. */
  function runNow(): Promise<void> {
    if (timer !== null) {
      clearTimeout(timer);
      timer = null;
    }
    return execute(query.value);
  }

  watch(query, (next) => {
    if (next.trim().length === 0) {
      // Clearing the field should empty the grid at once, not in 250ms.
      if (timer !== null) {
        clearTimeout(timer);
        timer = null;
      }
      void execute('');
      return;
    }

    schedule(next);
  });

  onScopeDispose(() => {
    if (timer !== null) clearTimeout(timer);
    controller?.abort();
  });

  return {
    query,
    results,
    resultKeys,
    renderedQuery,
    status,
    isLoading,
    error,
    totalResults,
    runNow
  };
}
