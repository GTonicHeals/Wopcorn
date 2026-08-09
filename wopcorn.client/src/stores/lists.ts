import { reactive, ref } from 'vue';
import { defineStore } from 'pinia';

import { ApiError, api, jsonBody } from '@/api/client';
import { useFriendsStore } from '@/stores/friends';
import { useQueueStore } from '@/stores/queue';
import { useTitlesStore } from '@/stores/titles';
import { useToastStore } from '@/stores/toasts';
import type {
  ListEntry,
  ListMembership,
  ListName,
  ListResponse,
  ListSort,
  RatingStats,
  SortDirection
} from '@/api/types';

/**
 * The three lists, their sort state, and every mutation (fe-06, task 10).
 *
 * An entry here holds only what the *entry* owns — when it was added, its queue
 * position, its watched date. The title itself lives once in the titles store, so
 * membership and `myRating` have exactly one home and a toggle pressed anywhere
 * is visible everywhere.
 *
 * Everything is keyed by the title key, which is what keeps a series and its
 * seasons independent: they are separate keys, so marking a season watched
 * touches nothing on the series, in either direction.
 *
 * Every mutation is optimistic: apply locally, fire the request, and on failure
 * restore the snapshot and explain in a toast. Mutation responses are written
 * into the stores rather than triggering a refetch of the list.
 */

export type StoredEntry = {
  key: string;
  addedAt: string;
  position: number | null;
  watchedOn: string | null;
};

export type ListState = {
  entries: StoredEntry[];
  /** The server's unfiltered total for the list — "12 of 84" needs both halves. */
  count: number;
  status: 'idle' | 'loading' | 'ready' | 'error';
  error: ApiError | null;
  sort: ListSort;
  dir: SortDirection;
};

export type AddOptions = {
  /** FR-C6, in one round trip. */
  alsoRemoveFrom?: ListName[];
  watchedOn?: string;
};

export const LIST_NAMES: ListName[] = ['watched', 'watchlist', 'queue'];

/** Contract default: `desc` for added/score/rating, `asc` otherwise. */
export function defaultDirection(sort: ListSort): SortDirection {
  return sort === 'added' || sort === 'score' || sort === 'rating' ? 'desc' : 'asc';
}

function emptyState(): ListState {
  return { entries: [], count: 0, status: 'idle', error: null, sort: 'added', dir: 'desc' };
}

function toStored(entry: ListEntry): StoredEntry {
  return {
    key: entry.title.key,
    addedAt: entry.addedAt,
    position: entry.position,
    watchedOn: entry.watchedOn
  };
}

type Snapshot = {
  membership: ListMembership | null;
  myRating: number | null;
  entries: Record<ListName, StoredEntry[]>;
  queueKeys: string[];
};

export const useListsStore = defineStore('lists', () => {
  const titles = useTitlesStore();
  const friends = useFriendsStore();
  const queue = useQueueStore();
  const toasts = useToastStore();

  const state = reactive<Record<ListName, ListState>>({
    watched: emptyState(),
    watchlist: emptyState(),
    queue: emptyState()
  });

  const ratingStats = ref<RatingStats | null>(null);

  const requests = new Map<ListName, Promise<void>>();
  let statsRequest: Promise<void> | null = null;

  // ------------------------------------------------------------------- reads

  /**
   * `GET /api/lists/{list}` with the current sort. Genre, decade and type filters
   * are applied in the view rather than sent: the decade options are "derived from
   * the entries present", which needs the unfiltered set anyway, and holding it
   * client-side keeps every filter toggle instant and request-free (NFR-2).
   */
  async function load(list: ListName, force = false): Promise<void> {
    const current = state[list];

    const pending = requests.get(list);
    if (pending && !force) return pending;
    if (!force && current.status === 'ready') return;

    const request = (async () => {
      current.status = 'loading';
      current.error = null;

      try {
        const params = new URLSearchParams({ sort: current.sort, dir: current.dir });
        const page = await api<ListResponse>(`/api/lists/${list}?${params.toString()}`);

        titles.upsertMany(page.entries.map((entry) => entry.title));
        current.entries = page.entries.map(toStored);
        current.count = page.count;
        current.status = 'ready';

        // The queue always comes back in stored order, whatever `sort` said.
        if (list === 'queue') {
          queue.setKeys(current.entries.map((entry) => entry.key));
        }
      } catch (error) {
        current.status = 'error';
        current.error = error instanceof ApiError ? error : null;
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

  async function setSort(list: ListName, sort: ListSort, dir: SortDirection): Promise<void> {
    const current = state[list];
    if (current.sort === sort && current.dir === dir) return;

    current.sort = sort;
    current.dir = dir;
    await load(list, true);
  }

  function entryOf(list: ListName, key: string): StoredEntry | null {
    return state[list].entries.find((entry) => entry.key === key) ?? null;
  }

  function isOn(list: ListName, key: string): boolean {
    return titles.get(key)?.lists[list] ?? false;
  }

  // ------------------------------------------------------- optimistic plumbing

  function snapshot(key: string): Snapshot {
    const title = titles.get(key);
    return {
      membership: title ? { ...title.lists } : null,
      myRating: title?.myRating ?? null,
      entries: {
        watched: [...state.watched.entries],
        watchlist: [...state.watchlist.entries],
        queue: [...state.queue.entries]
      },
      queueKeys: [...queue.keys]
    };
  }

  function restore(key: string, snap: Snapshot): void {
    if (snap.membership) {
      titles.patch(key, { lists: snap.membership, myRating: snap.myRating });
    }
    for (const list of LIST_NAMES) {
      state[list].entries = snap.entries[list];
      state[list].count = snap.entries[list].length;
    }
    queue.setKeys(snap.queueKeys);
  }

  function setMembership(key: string, list: ListName, member: boolean): void {
    const title = titles.get(key);
    if (!title) return;
    titles.patch(key, { lists: { ...title.lists, [list]: member } });
  }

  /** A stand-in row so the list view updates in the same frame as the toggle. */
  function insertPlaceholder(list: ListName, key: string): void {
    const current = state[list];
    if (current.entries.some((entry) => entry.key === key)) return;

    const placeholder: StoredEntry = {
      key,
      addedAt: new Date().toISOString(),
      position: null,
      watchedOn: null
    };

    // Newest-first is the default view, so a new row belongs at the top there.
    const atFront = list !== 'queue' && current.sort === 'added' && current.dir === 'desc';
    current.entries = atFront
      ? [placeholder, ...current.entries]
      : [...current.entries, placeholder];
    current.count = current.entries.length;
  }

  function detach(list: ListName, key: string): void {
    const current = state[list];
    current.entries = current.entries.filter((entry) => entry.key !== key);
    current.count = current.entries.length;
  }

  /** Writes a server `ListEntry` into both stores — never a refetch of the list. */
  function ingest(list: ListName, entry: ListEntry): void {
    titles.upsert(entry.title);

    const current = state[list];
    const stored = toStored(entry);
    const index = current.entries.findIndex((existing) => existing.key === stored.key);

    if (index >= 0) {
      const next = [...current.entries];
      next[index] = stored;
      current.entries = next;
    } else {
      current.entries = [...current.entries, stored];
      current.count = current.entries.length;
    }
  }

  function report(error: unknown, fallback: string): void {
    toasts.show(error instanceof ApiError ? error.message : fallback);
  }

  // --------------------------------------------------------------- mutations

  /**
   * `PUT /api/lists/{list}/{key}`, idempotent. `alsoRemoveFrom` implements FR-C6 —
   * marking a queued title watched removes it from the queue in the same request.
   */
  async function add(list: ListName, key: string, options: AddOptions = {}): Promise<boolean> {
    const snap = snapshot(key);
    const alsoRemoveFrom = (options.alsoRemoveFrom ?? []).filter((other) => other !== list);

    setMembership(key, list, true);
    insertPlaceholder(list, key);
    if (list === 'queue') queue.append(key);

    for (const other of alsoRemoveFrom) {
      setMembership(key, other, false);
      detach(other, key);
      if (other === 'queue') queue.drop(key);
    }

    try {
      const entry = await api<ListEntry>(`/api/lists/${list}/${key}`, {
        method: 'PUT',
        body: jsonBody({
          alsoRemoveFrom: alsoRemoveFrom.length > 0 ? alsoRemoveFrom : undefined,
          watchedOn: options.watchedOn
        })
      });

      ingest(list, entry);
      return true;
    } catch (error) {
      restore(key, snap);
      report(error, 'That did not go through.');
      return false;
    }
  }

  /** `DELETE /api/lists/{list}/{key}`, idempotent. */
  async function remove(list: ListName, key: string): Promise<boolean> {
    const snap = snapshot(key);

    setMembership(key, list, false);
    detach(list, key);
    if (list === 'queue') queue.drop(key);
    // The rating lives on the watched row and goes with it (FR-E4's converse).
    if (list === 'watched') titles.patch(key, { myRating: null });

    try {
      await api<void>(`/api/lists/${list}/${key}`, { method: 'DELETE' });
      return true;
    } catch (error) {
      restore(key, snap);
      report(error, 'That did not go through.');
      return false;
    }
  }

  function toggle(list: ListName, key: string, options: AddOptions = {}): Promise<boolean> {
    return isOn(list, key) ? remove(list, key) : add(list, key, options);
  }

  /**
   * `PUT /api/titles/{key}/rating`. Rating a title watches it (FR-E3), so the
   * Watched toggle fills in the same frame as the stars.
   */
  async function setRating(key: string, rating: number): Promise<boolean> {
    const snap = snapshot(key);

    titles.patch(key, { myRating: rating });
    setMembership(key, 'watched', true);
    insertPlaceholder('watched', key);

    try {
      const entry = await api<ListEntry>(`/api/titles/${key}/rating`, {
        method: 'PUT',
        body: jsonBody({ rating })
      });

      ingest('watched', entry);
      ratingStats.value = null;
      return true;
    } catch (error) {
      restore(key, snap);
      report(error, 'That rating did not go through.');
      return false;
    }
  }

  /** FR-E4: clears the rating and keeps the Watched entry. */
  async function clearRating(key: string): Promise<boolean> {
    const snap = snapshot(key);
    titles.patch(key, { myRating: null });

    try {
      await api<void>(`/api/titles/${key}/rating`, { method: 'DELETE' });
      ratingStats.value = null;
      return true;
    } catch (error) {
      restore(key, snap);
      report(error, 'That did not go through.');
      return false;
    }
  }

  // ------------------------------------------------------------------- stats

  /** FR-E6, for the histogram on /me. */
  async function loadRatingStats(force = false): Promise<void> {
    if (!force && ratingStats.value) return;
    if (statsRequest) return statsRequest;

    statsRequest = (async () => {
      try {
        ratingStats.value = await api<RatingStats>('/api/me/rating-stats');
      } catch {
        // The histogram is a summary of data the user can already see; a failure
        // hides it rather than taking over the profile screen.
      } finally {
        statsRequest = null;
      }
    })();

    return statsRequest;
  }

  /** Sign-out: none of this belongs to the next user (see `titles.clear`). */
  function clear(): void {
    for (const list of LIST_NAMES) Object.assign(state[list], emptyState());
    ratingStats.value = null;
    requests.clear();
    queue.clear();
    titles.clear();
    // Friends, pending requests and taste matches are per-session too — leaving
    // them would light the next user's badge with the previous user's requests.
    friends.clear();
  }

  return {
    state,
    ratingStats,
    load,
    ensure,
    setSort,
    entryOf,
    isOn,
    add,
    remove,
    toggle,
    setRating,
    clearRating,
    loadRatingStats,
    clear
  };
});
