import { computed, onScopeDispose, ref, watch } from 'vue';

import { ApiError, api } from '@/api/client';
import type { UserSearchResult } from '@/api/types';

/**
 * `GET /api/users/search`, debounced and race-guarded (fe-07, task 1).
 *
 * The same two defences as `useFilmSearch`, for the same reason: an
 * `AbortController` per request, **plus** a monotonically increasing request id
 * so a slow answer for query *n-1* cannot overwrite the results of query *n*.
 * Aborting alone does not close the race — a response can already be resolved
 * and queued as a microtask when the abort lands.
 *
 * The server matches a display-name **prefix**, case-insensitively, excludes the
 * caller, and caps at 20 rows; a blank `q` answers `[]` without a query. Nothing
 * here needs to re-filter.
 */

export const USER_SEARCH_DEBOUNCE_MS = 250;

export type UserSearchStatus = 'idle' | 'loading' | 'ready' | 'error';

export function useUserSearch() {
  const query = ref('');
  const results = ref<UserSearchResult[]>([]);
  const status = ref<UserSearchStatus>('idle');
  const error = ref<ApiError | null>(null);
  /** The query the currently rendered rows belong to. */
  const renderedQuery = ref('');

  let nextRequestId = 0;
  let renderedRequestId = 0;
  let controller: AbortController | null = null;
  let timer: ReturnType<typeof setTimeout> | null = null;

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
      results.value = [];
      error.value = null;
      status.value = 'idle';
      return;
    }

    const ac = new AbortController();
    controller = ac;
    status.value = 'loading';
    error.value = null;

    try {
      const found = await api<UserSearchResult[]>(
        `/api/users/search?q=${encodeURIComponent(term)}`,
        { signal: ac.signal }
      );

      if (requestId < renderedRequestId) return;

      renderedRequestId = requestId;
      renderedQuery.value = term;
      results.value = found;
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
    }, USER_SEARCH_DEBOUNCE_MS);
  }

  /** Skips the debounce — the Enter key wants this. */
  function runNow(): Promise<void> {
    if (timer !== null) {
      clearTimeout(timer);
      timer = null;
    }
    return execute(query.value);
  }

  function reset(): void {
    query.value = '';
  }

  /**
   * Rewrites one row's relationship in place. Sending a request does not change
   * the search response, but the button next to that person must stop saying
   * "Add friend" the moment it succeeds.
   */
  function setRelationship(userId: string, relationship: UserSearchResult['relationship']): void {
    results.value = results.value.map((result) =>
      result.id === userId ? { ...result, relationship } : result
    );
  }

  watch(query, (next) => {
    if (next.trim().length === 0) {
      // Clearing the field should empty the list at once, not in 250ms.
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
    renderedQuery,
    status,
    isLoading,
    error,
    runNow,
    reset,
    setRelationship
  };
}
