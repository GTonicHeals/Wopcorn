<script setup lang="ts">
/**
 * The only button in the app. `primary` is the only filled element besides an
 * active list toggle — gold stays scarce, and it means "your state".
 */
withDefaults(
  defineProps<{
    variant?: 'primary' | 'secondary' | 'ghost';
    type?: 'button' | 'submit' | 'reset';
    loading?: boolean;
    disabled?: boolean;
    block?: boolean;
  }>(),
  {
    variant: 'secondary',
    type: 'button',
    loading: false,
    disabled: false,
    block: false
  }
);
</script>

<template>
  <button
    :type="type"
    :class="['btn', `btn--${variant}`, { 'btn--block': block }]"
    :disabled="disabled || loading"
    :aria-busy="loading || undefined"
  >
    <span v-if="loading" class="btn__spinner" aria-hidden="true" />
    <span v-else class="btn__label"><slot /></span>
    <span v-if="loading" class="sr-only">Working…</span>
  </button>
</template>

<style scoped>
.btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: var(--space-2);
  min-height: var(--tap-min);
  padding: 0 var(--space-4);
  border-radius: var(--radius-md);
  border: 1px solid transparent;
  background: none;
  font-size: var(--text-sm);
  font-weight: 600;
  line-height: 1;
  transition: background-color 120ms ease, border-color 120ms ease, color 120ms ease;
}

.btn--block {
  display: flex;
  width: 100%;
}

.btn:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}

.btn--primary {
  background: var(--accent);
  border-color: var(--accent);
  color: var(--accent-ink);
}

.btn--secondary {
  border-color: var(--border);
  color: var(--text);
}

.btn--secondary:hover:not(:disabled) {
  background: var(--surface-raised);
}

.btn--ghost {
  color: var(--text-muted);
}

.btn--ghost:hover:not(:disabled) {
  color: var(--text);
}

.btn__label {
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
}

.btn__spinner {
  width: 18px;
  height: 18px;
  border-radius: 50%;
  border: 2px solid currentColor;
  border-top-color: transparent;
  animation: btn-spin 700ms linear infinite;
}

@keyframes btn-spin {
  to {
    transform: rotate(360deg);
  }
}
</style>
