<script setup lang="ts">
import IconClose from '@/components/icons/IconClose.vue';
import { useToastStore } from '@/stores/toasts';

/**
 * Sits above the bottom nav so a message never lands under the thumb bar.
 * `aria-live="polite"` — these announce a mutation that quietly failed, so they
 * must reach a screen reader without stealing focus.
 */
const toasts = useToastStore();
</script>

<template>
  <div class="toast-host" role="status" aria-live="polite">
    <div v-for="toast in toasts.toasts" :key="toast.id" class="toast">
      <p class="toast__text">{{ toast.message }}</p>
      <button
        type="button"
        class="toast__close"
        aria-label="Dismiss"
        @click="toasts.dismiss(toast.id)"
      >
        <IconClose />
      </button>
    </div>
  </div>
</template>

<style scoped>
.toast-host {
  position: fixed;
  z-index: 30;
  left: var(--space-4);
  right: var(--space-4);
  bottom: calc(var(--nav-height) + env(safe-area-inset-bottom) + var(--space-3));
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  pointer-events: none;
}

.toast {
  pointer-events: auto;
  display: flex;
  align-items: center;
  gap: var(--space-2);
  max-width: 480px;
  margin: 0 auto;
  width: 100%;
  padding: var(--space-2) var(--space-2) var(--space-2) var(--space-4);
  background: var(--surface-raised);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.4);
}

.toast__text {
  flex: 1;
  min-width: 0;
  font-size: var(--text-sm);
}

.toast__close {
  flex: none;
  display: flex;
  align-items: center;
  justify-content: center;
  width: var(--tap-min);
  height: var(--tap-min);
  border: 0;
  background: none;
  border-radius: var(--radius-full);
  color: var(--text-muted);
}

.toast__close :deep(svg) {
  width: 18px;
  height: 18px;
}

@media (min-width: 900px) {
  .toast-host {
    bottom: var(--space-6);
    left: auto;
    right: var(--space-6);
    width: min(420px, calc(100vw - var(--space-12)));
  }
}
</style>
