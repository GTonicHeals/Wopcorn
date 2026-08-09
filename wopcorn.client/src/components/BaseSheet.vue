<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue';

import IconClose from '@/components/icons/IconClose.vue';

/**
 * A bottom sheet on mobile, a centred dialog from 900px. Built on native
 * `<dialog>` + `showModal()`, which gives focus trapping, Esc-to-close, and
 * focus restoration to the opener for free. `alert`/`confirm` are never used.
 */
const props = withDefaults(
  defineProps<{
    open: boolean;
    title: string;
    /** Hides the header close button when the caller supplies its own actions. */
    dismissible?: boolean;
    /**
     * `lg` widens the desktop dialog for content that is a grid rather than a
     * form — the favourites picker. Unchanged on mobile, where every sheet is
     * already full width.
     */
    size?: 'md' | 'lg';
  }>(),
  { dismissible: true, size: 'md' }
);

const emit = defineEmits<{ 'update:open': [boolean]; close: [] }>();

const dialog = ref<HTMLDialogElement | null>(null);

function close(): void {
  emit('update:open', false);
  emit('close');
}

watch(
  () => props.open,
  (open) => {
    const element = dialog.value;
    if (!element) return;

    if (open) {
      // jsdom and very old browsers may not implement showModal.
      if (typeof element.showModal === 'function') {
        if (!element.open) element.showModal();
      } else {
        element.setAttribute('open', '');
      }
    } else if (element.open) {
      element.close();
    }
  },
  { flush: 'post' }
);

/** Esc and form-method="dialog" both fire `close` on the element itself. */
function onNativeClose(): void {
  if (props.open) close();
}

/** A click that lands on the dialog box itself is a backdrop click. */
function onBackdropClick(event: MouseEvent): void {
  if (props.dismissible && event.target === dialog.value) close();
}

onBeforeUnmount(() => {
  if (dialog.value?.open) dialog.value.close();
});
</script>

<template>
  <dialog
    ref="dialog"
    class="sheet"
    :class="`sheet--${size}`"
    @close="onNativeClose"
    @click="onBackdropClick"
  >
    <div class="sheet__panel">
      <header class="sheet__head">
        <h2 class="sheet__title">{{ title }}</h2>
        <button
          v-if="dismissible"
          type="button"
          class="sheet__close"
          aria-label="Close"
          @click="close"
        >
          <IconClose />
        </button>
      </header>
      <div class="sheet__body">
        <slot />
      </div>
      <footer v-if="$slots.actions" class="sheet__actions">
        <slot name="actions" />
      </footer>
    </div>
  </dialog>
</template>

<style scoped>
.sheet {
  padding: 0;
  border: 0;
  background: none;
  width: 100%;
  max-width: 100%;
  max-height: 90dvh;
  /* Bottom sheet on mobile. */
  margin: auto auto 0;
  color: var(--text);
}

.sheet::backdrop {
  background: rgba(0, 0, 0, 0.55);
}

.sheet__panel {
  background: var(--surface);
  border: 1px solid var(--border);
  border-bottom: 0;
  border-radius: var(--radius-lg) var(--radius-lg) 0 0;
  padding: var(--space-4) var(--space-4) calc(var(--space-4) + env(safe-area-inset-bottom));
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}

.sheet__head {
  display: flex;
  align-items: center;
  gap: var(--space-3);
}

.sheet__title {
  font-family: var(--font-display);
  font-size: var(--text-lg);
  font-weight: 500;
  flex: 1;
  min-width: 0;
}

.sheet__close {
  display: flex;
  align-items: center;
  justify-content: center;
  width: var(--tap-min);
  height: var(--tap-min);
  margin: calc(var(--space-2) * -1) calc(var(--space-2) * -1) 0 0;
  border: 0;
  background: none;
  border-radius: var(--radius-full);
  color: var(--text-muted);
}

.sheet__body {
  overflow-y: auto;
}

.sheet__actions {
  display: flex;
  gap: var(--space-2);
  justify-content: flex-end;
}

@media (min-width: 900px) {
  .sheet {
    max-width: 480px;
    margin: auto;
  }

  .sheet--lg {
    max-width: 720px;
  }

  .sheet__panel {
    border-bottom: 1px solid var(--border);
    border-radius: var(--radius-lg);
    padding-bottom: var(--space-4);
  }
}
</style>
