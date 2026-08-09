<script setup lang="ts">
import { computed, ref } from 'vue';

import StarDisplay from '@/components/StarDisplay.vue';
import { MAX_RATING, MIN_RATING, clampRating, ratingText, valueFromPointer } from '@/lib/stars';

/**
 * Ten values at half-star granularity, reliable under a thumb (fe-06, task 3).
 *
 * The naive build — ten 14px targets — fails on a phone. This one is a single
 * 56px-tall full-width row: **the hit area is the row, not the glyphs**, and the
 * primary gesture is dragging along it rather than tapping precisely. The stars
 * are 32px and only display the value.
 *
 * Slider semantics (NFR-8): arrows step one half-star, Home/End jump to the ends,
 * and the control is in the tab order. Clearing is its own button — "tap the
 * current value again" is never overloaded to mean clear (FR-E4).
 */
const props = withDefaults(
  defineProps<{
    /** 1–10 half-stars, or null when unrated. */
    modelValue: number | null;
    /** Names the control for a screen reader: "Your rating for Dune". */
    filmTitle?: string | null;
    disabled?: boolean;
  }>(),
  { filmTitle: null, disabled: false }
);

const emit = defineEmits<{
  'update:modelValue': [number];
  clear: [];
}>();

const row = ref<HTMLDivElement | null>(null);
/** The value under the pointer mid-drag; committed on release. */
const preview = ref<number | null>(null);

const displayValue = computed(() => preview.value ?? props.modelValue);
const label = computed(() =>
  props.filmTitle ? `Your rating for ${props.filmTitle}` : 'Your rating'
);
const valueText = computed(() => ratingText(displayValue.value));

function valueAt(clientX: number): number {
  const element = row.value;
  if (!element) return MIN_RATING;
  const rect = element.getBoundingClientRect();
  return valueFromPointer(clientX, rect);
}

function commit(value: number): void {
  const next = clampRating(value);
  if (next !== props.modelValue) emit('update:modelValue', next);
}

function onPointerDown(event: PointerEvent): void {
  if (props.disabled || event.button !== 0) return;

  // Capturing means pointermove keeps arriving even when the finger leaves the
  // row — dragging past either end saturates rather than losing the gesture.
  row.value?.setPointerCapture(event.pointerId);
  preview.value = valueAt(event.clientX);
  event.preventDefault();
}

function onPointerMove(event: PointerEvent): void {
  if (props.disabled) return;
  if (!row.value?.hasPointerCapture(event.pointerId)) return;
  preview.value = valueAt(event.clientX);
}

function onPointerUp(event: PointerEvent): void {
  if (props.disabled) return;
  if (!row.value?.hasPointerCapture(event.pointerId)) return;

  row.value.releasePointerCapture(event.pointerId);
  const value = preview.value ?? valueAt(event.clientX);
  preview.value = null;
  commit(value);
}

function onPointerCancel(event: PointerEvent): void {
  if (row.value?.hasPointerCapture(event.pointerId)) {
    row.value.releasePointerCapture(event.pointerId);
  }
  preview.value = null;
}

function onKeyDown(event: KeyboardEvent): void {
  if (props.disabled) return;

  const current = props.modelValue ?? 0;
  let next: number | null = null;

  switch (event.key) {
    case 'ArrowRight':
    case 'ArrowUp':
      next = current + 1;
      break;
    case 'ArrowLeft':
    case 'ArrowDown':
      next = current - 1;
      break;
    case 'Home':
      next = MIN_RATING;
      break;
    case 'End':
      next = MAX_RATING;
      break;
    default:
      return;
  }

  event.preventDefault();
  commit(next);
}
</script>

<template>
  <div class="rating">
    <div
      ref="row"
      class="rating__row"
      role="slider"
      :tabindex="disabled ? -1 : 0"
      :aria-label="label"
      :aria-valuemin="MIN_RATING"
      :aria-valuemax="MAX_RATING"
      :aria-valuenow="displayValue ?? undefined"
      :aria-valuetext="valueText"
      :aria-disabled="disabled || undefined"
      @pointerdown="onPointerDown"
      @pointermove="onPointerMove"
      @pointerup="onPointerUp"
      @pointercancel="onPointerCancel"
      @keydown="onKeyDown"
    >
      <StarDisplay :value="displayValue" :size="32" track="strong" label="" />
    </div>

    <!--
      FR-E4: clearing is its own affordance. It only exists once there is
      something to clear.
    -->
    <button
      v-if="modelValue !== null"
      type="button"
      class="rating__clear"
      :disabled="disabled"
      @click="emit('clear')"
    >
      Clear
    </button>
  </div>
</template>

<style scoped>
.rating {
  display: flex;
  align-items: center;
  gap: var(--space-2);
}

.rating__row {
  /* The hit area is the whole row — 56px tall, full width. */
  flex: 1;
  min-width: 0;
  height: 56px;
  display: flex;
  align-items: center;
  /* The glyphs are centred; the hit area is still the full width. */
  justify-content: center;
  /* Stops the browser turning a horizontal drag into a scroll or a page swipe. */
  touch-action: none;
  user-select: none;
  border-radius: var(--radius-sm);
}

.rating__clear {
  flex: none;
  min-height: var(--tap-min);
  padding: 0 var(--space-3);
  border: 0;
  background: none;
  color: var(--text-muted);
  font-size: var(--text-xs);
  border-radius: var(--radius-sm);
}

.rating__clear:hover:not(:disabled) {
  color: var(--text);
}
</style>
