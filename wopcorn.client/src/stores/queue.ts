import { ref } from 'vue';
import { defineStore } from 'pinia';

import { ApiError, api, jsonBody } from '@/api/client';
import { useToastStore } from '@/stores/toasts';
import type { QueueOrder, QueueSortPreset, SortDirection } from '@/api/types';

/**
 * The queue's order, and only its order (fe-06, task 10). The entries themselves
 * live in the lists store — this holds the one thing the queue has that no other
 * list does: a position.
 *
 * The order is a list of **title keys**, so a film, a series and a season sit in
 * one queue and reorder against each other like any other three entries.
 *
 * FR-D5: reorder locally first, then `PUT /api/queue/order` with the complete
 * list. The server's answer always wins, including the `409 queue_out_of_sync`
 * answer — a rejected reorder is never silently discarded.
 */

const CONFLICT_MESSAGE = 'Your queue changed elsewhere — showing the latest order.';

/** The 409 body carries the authoritative order beside the usual error shape. */
function keysFromBody(body: unknown): string[] | null {
  if (typeof body !== 'object' || body === null) return null;

  const value = (body as { keys?: unknown }).keys;
  if (!Array.isArray(value)) return null;
  if (!value.every((key): key is string => typeof key === 'string')) return null;

  return [...value];
}

export type PersistResult = 'ok' | 'conflict' | 'error';

/** Pure list move — extracted so the reorder maths is testable on its own. */
export function moveWithin<T>(ids: T[], from: number, to: number): T[] {
  if (from === to || from < 0 || to < 0 || from >= ids.length || to >= ids.length) {
    return [...ids];
  }

  const next = [...ids];
  const [moved] = next.splice(from, 1);
  if (moved === undefined) return [...ids];
  next.splice(to, 0, moved);
  return next;
}

export const useQueueStore = defineStore('queue', () => {
  /** The complete queue in stored order; index 0 is the "Up next" hero. */
  const keys = ref<string[]>([]);
  const saving = ref(false);

  function setKeys(next: string[]): void {
    keys.value = [...next];
  }

  /** A new queue entry appends to the end (FR-D1). */
  function append(key: string): void {
    if (!keys.value.includes(key)) keys.value = [...keys.value, key];
  }

  function insertAt(key: string, index: number): void {
    if (keys.value.includes(key)) return;
    const next = [...keys.value];
    next.splice(Math.max(0, Math.min(index, next.length)), 0, key);
    keys.value = next;
  }

  function drop(key: string): number {
    const index = keys.value.indexOf(key);
    if (index >= 0) keys.value = keys.value.filter((existing) => existing !== key);
    return index;
  }

  /**
   * Applies `next` immediately, then writes it. On `409` the response's order
   * replaces local state and the user is told; on any other failure the previous
   * order comes back so the screen never shows an order the server rejected.
   */
  async function persist(next: string[]): Promise<PersistResult> {
    const previous = [...keys.value];
    keys.value = [...next];
    saving.value = true;

    try {
      const order = await api<QueueOrder>('/api/queue/order', {
        method: 'PUT',
        body: jsonBody({ keys: next })
      });
      keys.value = [...order.keys];
      return 'ok';
    } catch (error) {
      if (error instanceof ApiError && error.code === 'queue_out_of_sync') {
        const authoritative = keysFromBody(error.body);
        keys.value = authoritative ?? previous;
        useToastStore().show(CONFLICT_MESSAGE);
        return 'conflict';
      }

      keys.value = previous;
      useToastStore().show(
        error instanceof ApiError ? error.message : 'That reorder did not go through.'
      );
      return 'error';
    } finally {
      saving.value = false;
    }
  }

  /**
   * FR-D3. A preset is a write: it rewrites stored positions and hands back the
   * result. Entries stay hand-draggable afterwards (FR-D4) — positions are just
   * integers.
   */
  async function applyPreset(preset: QueueSortPreset, dir: SortDirection): Promise<boolean> {
    saving.value = true;
    try {
      const order = await api<QueueOrder>('/api/queue/sort', {
        method: 'POST',
        body: jsonBody({ preset, dir })
      });
      keys.value = [...order.keys];
      return true;
    } catch (error) {
      useToastStore().show(
        error instanceof ApiError ? error.message : 'That sort did not go through.'
      );
      return false;
    } finally {
      saving.value = false;
    }
  }

  function clear(): void {
    keys.value = [];
  }

  return { keys, saving, setKeys, append, insertAt, drop, persist, applyPreset, clear };
});
