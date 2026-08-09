import { reactive, ref, watch, type Ref } from 'vue';

import { ApiError, api } from '@/api/client';
import { useTitlesStore } from '@/stores/titles';
import type { ListName, ListResponse } from '@/api/types';

/**
 * `GET /api/friends/{userId}/lists/{list}` (fe-07, task 3).
 *
 * Deliberately **not** the lists store. That store owns *your* three lists and
 * every mutation on them; a friend's list is read-only borrowed data and must
 * never end up in the arrays that `PUT /api/queue/order` is computed from.
 *
 * What the two do share is the titles store: `entry.title` describes the title
 * plus **your** membership and rating, so upserting it keeps a friend's grid in
 * sync with the same title everywhere else, and the toggles on those cards act on
 * your lists with no special handling. The friend's own rating rides on
 * `entry.rating` and is kept beside the key — never written into `myRating`.
 */

export type FriendListState = {
  /** Keys in the server's order, paired with the friend's rating for each. */
  entries: { key: string; rating: number | null }[];
  count: number;
  status: 'idle' | 'loading' | 'ready' | 'error';
  error: ApiError | null;
};

function emptyState(): FriendListState {
  return { entries: [], count: 0, status: 'idle', error: null };
}

export function useFriendLists(userId: Ref<string>) {
  const titles = useTitlesStore();

  const state = reactive<Record<ListName, FriendListState>>({
    watched: emptyState(),
    watchlist: emptyState(),
    queue: emptyState()
  });

  /**
   * Set when any read comes back `403`. The friendship ended mid-session — every
   * one of these routes re-checks it on the request (NFR-4) — and the whole
   * screen has to say so rather than showing three empty lists.
   */
  const forbidden = ref(false);

  const requests = new Map<ListName, Promise<void>>();

  function reset(): void {
    for (const list of ['watched', 'watchlist', 'queue'] as ListName[]) {
      Object.assign(state[list], emptyState());
    }
    requests.clear();
    forbidden.value = false;
  }

  // A different profile is different data; nothing survives the switch.
  watch(userId, reset);

  async function load(list: ListName, force = false): Promise<void> {
    const current = state[list];

    const pending = requests.get(list);
    if (pending && !force) return pending;
    if (!force && current.status === 'ready') return;

    const request = (async () => {
      current.status = 'loading';
      current.error = null;

      try {
        const page = await api<ListResponse>(`/api/friends/${userId.value}/lists/${list}`);

        titles.upsertMany(page.entries.map((entry) => entry.title));
        current.entries = page.entries.map((entry) => ({
          key: entry.title.key,
          rating: entry.rating
        }));
        current.count = page.count;
        current.status = 'ready';
      } catch (failure) {
        current.status = 'error';
        current.error = failure instanceof ApiError ? failure : null;
        if (failure instanceof ApiError && failure.status === 403) forbidden.value = true;
      } finally {
        requests.delete(list);
      }
    })();

    requests.set(list, request);
    return request;
  }

  function ensure(list: ListName): Promise<void> {
    return load(list, false);
  }

  return { state, forbidden, load, ensure, reset };
}
