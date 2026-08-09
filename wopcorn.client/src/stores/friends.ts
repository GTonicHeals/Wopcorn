import { computed, ref } from 'vue';
import { defineStore } from 'pinia';

import { ApiError, api, jsonBody } from '@/api/client';
import { useToastStore } from '@/stores/toasts';
import type { Friend, FriendRequest, FriendsResponse, UserSearchResult } from '@/api/types';

/**
 * Friends, both request directions, and every mutation on them (fe-07, task 1).
 *
 * `GET /api/friends` answers all three lists in one call, which is what makes the
 * shell's pending badge free (FR-F4) — so every mutation here reloads that one
 * endpoint rather than patching three arrays by hand and hoping they agree with
 * the server. The screen is small and the call is cheap; correctness wins.
 *
 * `FriendRequest.user` is always **the other party**: the sender on `incoming`,
 * the recipient on `outgoing`. Nothing here has to work out which.
 */

/** What `POST /api/friends/requests` actually did, for the caller to explain. */
export type SendOutcome =
  | 'sent'
  | 'already_friends'
  /** A request exists in one direction or the other — see `relationshipOf`. */
  | 'request_pending'
  | 'error';

export const useFriendsStore = defineStore('friends', () => {
  const toasts = useToastStore();

  const friends = ref<Friend[]>([]);
  const incoming = ref<FriendRequest[]>([]);
  const outgoing = ref<FriendRequest[]>([]);

  const status = ref<'idle' | 'loading' | 'ready' | 'error'>('idle');
  const error = ref<ApiError | null>(null);

  /** Drives the nav badge. Zero means the badge is not rendered at all. */
  const pendingCount = computed(() => incoming.value.length);

  let inFlight: Promise<void> | null = null;

  // -------------------------------------------------------------------- read

  /**
   * The whole screen in one request. Concurrent callers share the flight — the
   * shell asks at boot for the badge and the friends view asks on mount.
   */
  async function load(force = false): Promise<void> {
    if (inFlight) return inFlight;
    if (!force && status.value === 'ready') return;

    inFlight = (async () => {
      if (status.value !== 'ready') status.value = 'loading';
      error.value = null;

      try {
        const page = await api<FriendsResponse>('/api/friends');
        friends.value = page.friends;
        incoming.value = page.incoming;
        outgoing.value = page.outgoing;
        status.value = 'ready';
      } catch (failure) {
        error.value = failure instanceof ApiError ? failure : null;
        status.value = 'error';
      } finally {
        inFlight = null;
      }
    })();

    return inFlight;
  }

  // ----------------------------------------------------------- relationships

  /**
   * What the store knows about this person, or `null` when it knows nothing.
   *
   * A search result carries its own `relationship` from the server, but the
   * store is fresher after an accept or a removal, so the view prefers this and
   * falls back to the result's own value. Only a *positive* answer is returned:
   * absence from all three lists is reported as "no opinion" rather than
   * "none", because the store may simply not have been loaded yet.
   */
  function relationshipOf(userId: string): UserSearchResult['relationship'] | null {
    if (friends.value.some((friend) => friend.user.id === userId)) return 'friends';
    if (incoming.value.some((request) => request.user.id === userId)) return 'request_received';
    if (outgoing.value.some((request) => request.user.id === userId)) return 'request_sent';
    return null;
  }

  /** The pending request *from* this person, so a search row can accept it. */
  function incomingRequestFrom(userId: string): FriendRequest | null {
    return incoming.value.find((request) => request.user.id === userId) ?? null;
  }

  // --------------------------------------------------------------- mutations

  /**
   * `POST /api/friends/requests`. Both 409s are ordinary answers, not failures:
   * `already_friends` means the screen was stale, and `request_pending` means a
   * request already exists in one direction — after the reload the caller can
   * see which, and the right move for an incoming one is to accept it.
   */
  async function sendRequest(userId: string): Promise<SendOutcome> {
    try {
      await api<FriendRequest>('/api/friends/requests', {
        method: 'POST',
        body: jsonBody({ userId })
      });

      await load(true);
      return 'sent';
    } catch (failure) {
      if (failure instanceof ApiError && failure.status === 409) {
        // Whatever the conflict was, the server's view is the true one.
        await load(true);
        return failure.code === 'already_friends' ? 'already_friends' : 'request_pending';
      }

      report(failure, 'That friend request did not go through.');
      return 'error';
    }
  }

  /** `POST /api/friends/requests/{id}/accept`. Only the recipient may (FR-F2). */
  async function accept(requestId: string): Promise<boolean> {
    // Optimistic only in the sense that the badge drops at once; the reload
    // below is what actually decides the three lists.
    const snapshot = incoming.value;
    incoming.value = incoming.value.filter((request) => request.id !== requestId);

    try {
      await api<Friend>(`/api/friends/requests/${requestId}/accept`, { method: 'POST' });
      await load(true);
      return true;
    } catch (failure) {
      incoming.value = snapshot;
      // A request answered or withdrawn elsewhere is a 404; the reload clears it.
      if (failure instanceof ApiError && failure.status === 404) {
        await load(true);
      }
      report(failure, 'That request could not be accepted.');
      return false;
    }
  }

  /** `POST /api/friends/requests/{id}/decline`. */
  async function decline(requestId: string): Promise<boolean> {
    const snapshot = incoming.value;
    incoming.value = incoming.value.filter((request) => request.id !== requestId);

    try {
      await api<void>(`/api/friends/requests/${requestId}/decline`, { method: 'POST' });
      await load(true);
      return true;
    } catch (failure) {
      incoming.value = snapshot;
      if (failure instanceof ApiError && failure.status === 404) {
        await load(true);
      }
      report(failure, 'That request could not be declined.');
      return false;
    }
  }

  /**
   * `DELETE /api/friends/requests/{id}` — the sender withdrawing their own
   * request, the mirror of `decline`. A `403` here would mean we offered the
   * control to the recipient, who has `decline` instead.
   */
  async function cancel(requestId: string): Promise<boolean> {
    const snapshot = outgoing.value;
    outgoing.value = outgoing.value.filter((request) => request.id !== requestId);

    try {
      await api<void>(`/api/friends/requests/${requestId}`, { method: 'DELETE' });
      await load(true);
      return true;
    } catch (failure) {
      outgoing.value = snapshot;
      // Answered or withdrawn elsewhere: the reload settles who is what.
      if (failure instanceof ApiError && failure.status === 404) {
        await load(true);
      }
      report(failure, 'That request could not be withdrawn.');
      return false;
    }
  }

  /** `DELETE /api/friends/{userId}` — FR-F3, idempotent on the server. */
  async function remove(userId: string): Promise<boolean> {
    const snapshot = friends.value;
    friends.value = friends.value.filter((friend) => friend.user.id !== userId);

    try {
      await api<void>(`/api/friends/${userId}`, { method: 'DELETE' });
      await load(true);
      return true;
    } catch (failure) {
      friends.value = snapshot;
      report(failure, 'That did not go through.');
      return false;
    }
  }

  function report(failure: unknown, fallback: string): void {
    toasts.show(failure instanceof ApiError ? failure.message : fallback);
  }

  /**
   * Sign-out. Friends, requests and taste matches all belong to the session that
   * fetched them — `lists.clear()` calls this alongside the film cache.
   */
  function clear(): void {
    friends.value = [];
    incoming.value = [];
    outgoing.value = [];
    status.value = 'idle';
    error.value = null;
  }

  return {
    friends,
    incoming,
    outgoing,
    status,
    error,
    pendingCount,
    load,
    relationshipOf,
    incomingRequestFrom,
    sendRequest,
    accept,
    decline,
    cancel,
    remove,
    clear
  };
});
