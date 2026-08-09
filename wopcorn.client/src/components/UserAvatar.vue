<script setup lang="ts">
import { computed, ref, watch } from 'vue';

import type { UserSummary } from '@/api/types';

const props = withDefaults(
  defineProps<{
    user: UserSummary | null;
    /** Edge length in px. */
    size?: number;
    /** Overrides the stored avatar — used for the local preview during upload. */
    src?: string | null;
  }>(),
  { size: 32, src: null }
);

const broken = ref(false);
const source = computed(() => (broken.value ? null : (props.src ?? props.user?.avatarUrl ?? null)));

// A new URL deserves a fresh attempt.
watch(
  () => props.src ?? props.user?.avatarUrl ?? null,
  () => {
    broken.value = false;
  }
);

const initial = computed(() => {
  const name = props.user?.displayName?.trim() ?? '';
  return name ? [...name][0]!.toUpperCase() : '?';
});

const dimension = computed(() => `${props.size}px`);
const fontSize = computed(() => `${Math.max(11, Math.round(props.size * 0.42))}px`);
</script>

<template>
  <span class="avatar" :style="{ width: dimension, height: dimension, fontSize }">
    <!-- A broken avatar falls back to the initial rather than a torn image box. -->
    <img v-if="source" :src="source" alt="" class="avatar__img" @error="broken = true" />
    <span v-else aria-hidden="true">{{ initial }}</span>
  </span>
</template>

<style scoped>
.avatar {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex: none;
  border-radius: var(--radius-full);
  background: var(--surface-raised);
  border: 1px solid var(--border);
  color: var(--text);
  font-weight: 700;
  line-height: 1;
  overflow: hidden;
  user-select: none;
}

.avatar__img {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  object-fit: cover;
}
</style>
