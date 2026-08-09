<script setup lang="ts">
import { computed } from 'vue';

import { describeTasteMatch, tasteMatchLabel } from '@/lib/tasteMatch';
import type { TasteMatch } from '@/api/types';

/**
 * The only place a taste match is ever formatted (fe-07, task 4).
 *
 * The score and its sample size are rendered as one visual unit — they are two
 * lines of the same block, and there is no prop that can separate them. Below
 * the overlap threshold the percentage is not rendered at all; the component
 * takes that decision so the unqualified case cannot leak into a caller.
 */
const props = withDefaults(
  defineProps<{
    match: TasteMatch | null;
    /** `lg` heads a profile; `sm` sits under a name in the friends list. */
    size?: 'sm' | 'lg';
  }>(),
  { size: 'sm' }
);

const display = computed(() => describeTasteMatch(props.match));
const label = computed(() => tasteMatchLabel(display.value));
</script>

<template>
  <p v-if="display" class="taste" :class="`taste--${size}`">
    <!-- One announcement, so the number and its basis are never heard apart. -->
    <span class="sr-only">{{ label }}</span>

    <template v-if="display.kind === 'match'">
      <span class="taste__score" aria-hidden="true">{{ display.headline }}</span>
      <span class="taste__detail" aria-hidden="true">{{ display.detail }}</span>
    </template>

    <span v-else class="taste__detail" aria-hidden="true">{{ display.text }}</span>
  </p>
</template>

<style scoped>
.taste {
  display: flex;
  flex-direction: column;
  gap: 1px;
  min-width: 0;
}

.taste__score {
  font-weight: 700;
  font-variant-numeric: tabular-nums;
  line-height: 1.15;
}

/* The sample size is quiet, but it is always there. */
.taste__detail {
  font-size: var(--text-xs);
  color: var(--text-muted);
  line-height: 1.3;
}

.taste--sm .taste__score {
  font-size: var(--text-sm);
}

.taste--lg .taste__score {
  font-size: 22px;
}
</style>
