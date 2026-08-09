import { ref } from 'vue';
import { defineStore } from 'pinia';

/**
 * Transient messages. fe-06 needs these in two places the plan names explicitly:
 * a reverted optimistic list mutation ("update the store, fire the request, and
 * on failure revert and show a toast with the error's `message`") and the queue
 * reorder conflict.
 *
 * Deliberately not an error surface — a failure that leaves the screen unusable
 * gets `ErrorState`, not a toast.
 */
export type Toast = {
  id: number;
  message: string;
};

const DEFAULT_TIMEOUT_MS = 5000;

export const useToastStore = defineStore('toasts', () => {
  const toasts = ref<Toast[]>([]);

  let nextId = 1;
  const timers = new Map<number, ReturnType<typeof setTimeout>>();

  function dismiss(id: number): void {
    toasts.value = toasts.value.filter((toast) => toast.id !== id);
    const timer = timers.get(id);
    if (timer !== undefined) {
      clearTimeout(timer);
      timers.delete(id);
    }
  }

  function show(message: string, timeoutMs: number = DEFAULT_TIMEOUT_MS): number {
    const id = nextId++;
    toasts.value = [...toasts.value, { id, message }];

    if (timeoutMs > 0) {
      timers.set(
        id,
        setTimeout(() => dismiss(id), timeoutMs)
      );
    }

    return id;
  }

  function clear(): void {
    for (const timer of timers.values()) clearTimeout(timer);
    timers.clear();
    toasts.value = [];
  }

  return { toasts, show, dismiss, clear };
});
