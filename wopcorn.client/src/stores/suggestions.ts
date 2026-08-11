import { computed, ref } from 'vue';
import { defineStore } from 'pinia';

import { ApiError, api, jsonBody } from '@/api/client';
import { useToastStore } from '@/stores/toasts';
import type { Suggestion, SuggestionRequest, SuggestionsResponse } from '@/api/types';

/**
 * Friend-to-friend suggestions (plan 10).
 *
 * Shaped like `useFriendsStore` and for the same reason: `GET /api/suggestions`
 * answers both directions in one call, so the inbox badge is free, and every
 * mutation reloads that one endpoint rather than patching two arrays by hand and
 * hoping they agree with the server.
 *
 * The one thing worth patching optimistically is the answered row leaving the
 * inbox, because the badge count is on screen while the request is in flight.
 */

/** What `POST /api/suggestions` actually did, for the caller to explain. */
export type SuggestOutcome =
  | 'sent'
  /** A live suggestion of this title to this person already exists. */
  | 'already_suggested'
  | 'not_friends'
  | 'error';

export const useSuggestionsStore = defineStore('suggestions', () => {
  const toasts = useToastStore();

  const incoming = ref<Suggestion[]>([]);
  const outgoing = ref<Suggestion[]>([]);

  const status = ref<'idle' | 'loading' | 'ready' | 'error'>('idle');
  const error = ref<ApiError | null>(null);

  /** Drives the nav badge. Zero means the badge is not rendered at all. */
  const pendingCount = computed(() => incoming.value.length);

  let inFlight: Promise<void> | null = null;

  // -------------------------------------------------------------------- read

  async function load(force = false): Promise<void> {
    if (inFlight) return inFlight;
    if (!force && status.value === 'ready') return;

    inFlight = (async () => {
      if (status.value !== 'ready') status.value = 'loading';
      error.value = null;

      try {
        const page = await api<SuggestionsResponse>('/api/suggestions');
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

  /** The live suggestion this viewer already sent someone, if any. */
  function sentTo(userId: string, key: string): Suggestion | null {
    return (
      outgoing.value.find(
        (suggestion) =>
          suggestion.to.id === userId &&
          suggestion.title.key === key &&
          suggestion.state !== 'accepted'
      ) ?? null
    );
  }

  // --------------------------------------------------------------- mutations

  /**
   * `POST /api/suggestions`. A `409` is an ordinary answer rather than a failure
   * — it means the screen was stale and there is already one waiting, which the
   * caller should say rather than shrug at.
   */
  async function send(request: SuggestionRequest): Promise<SuggestOutcome> {
    try {
      await api<Suggestion>('/api/suggestions', {
        method: 'POST',
        body: jsonBody(request)
      });

      await load(true);
      return 'sent';
    } catch (failure) {
      if (failure instanceof ApiError && failure.status === 409) {
        await load(true);
        return 'already_suggested';
      }

      if (failure instanceof ApiError && failure.status === 403) {
        return 'not_friends';
      }

      report(failure, 'That suggestion did not go through.');
      return 'error';
    }
  }

  /**
   * `POST /api/suggestions/{id}/accept`. The title stays; only the accept/remove
   * line goes away. The lists it may have written to are the caller's to reload
   * — this store does not own them.
   */
  async function accept(id: string): Promise<boolean> {
    return answer(id, `/api/suggestions/${id}/accept`, 'POST', 'That could not be accepted.');
  }

  /**
   * `POST /api/suggestions/{id}/dismiss`. Removes the list entry **only** when
   * the suggestion created it — the server decides that, not the button.
   */
  async function dismiss(id: string): Promise<boolean> {
    return answer(id, `/api/suggestions/${id}/dismiss`, 'POST', 'That could not be dismissed.');
  }

  /**
   * `DELETE /api/suggestions/{id}` — the sender withdrawing. Takes back the
   * message and never the title: by now it may be a row in someone else's queue.
   */
  async function withdraw(id: string): Promise<boolean> {
    const snapshot = outgoing.value;
    outgoing.value = outgoing.value.filter((suggestion) => suggestion.id !== id);

    try {
      await api<void>(`/api/suggestions/${id}`, { method: 'DELETE' });
      await load(true);
      return true;
    } catch (failure) {
      outgoing.value = snapshot;
      // Answered elsewhere, or already gone: the reload settles it.
      if (failure instanceof ApiError && failure.status === 404) {
        await load(true);
      }
      report(failure, 'That could not be withdrawn.');
      return false;
    }
  }

  /** The two recipient verbs differ only in their route and their apology. */
  async function answer(
    id: string,
    path: string,
    method: string,
    fallback: string
  ): Promise<boolean> {
    // Optimistic only in that the badge drops at once; the reload below is what
    // actually decides both lists.
    const snapshot = incoming.value;
    incoming.value = incoming.value.filter((suggestion) => suggestion.id !== id);

    try {
      await api<unknown>(path, { method });
      await load(true);
      return true;
    } catch (failure) {
      incoming.value = snapshot;
      if (failure instanceof ApiError && failure.status === 404) {
        await load(true);
      }
      report(failure, fallback);
      return false;
    }
  }

  function report(failure: unknown, fallback: string): void {
    toasts.show(failure instanceof ApiError ? failure.message : fallback);
  }

  /** Sign-out. Suggestions belong to the session that fetched them. */
  function clear(): void {
    incoming.value = [];
    outgoing.value = [];
    status.value = 'idle';
    error.value = null;
  }

  return {
    incoming,
    outgoing,
    status,
    error,
    pendingCount,
    load,
    sentTo,
    send,
    accept,
    dismiss,
    withdraw,
    clear
  };
});
