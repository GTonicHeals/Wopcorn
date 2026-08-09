<script setup lang="ts">
// The star glyph is drawn, never typed: a font star renders differently on every
// platform and cannot be clipped precisely enough for half values.
withDefaults(defineProps<{ filled?: boolean }>(), { filled: false });
</script>

<template>
  <svg
    class="icon"
    viewBox="0 0 24 24"
    width="24"
    height="24"
    aria-hidden="true"
    focusable="false"
    :fill="filled ? 'currentColor' : 'none'"
    stroke="currentColor"
    stroke-width="1.5"
    stroke-linecap="round"
    stroke-linejoin="round"
  >
    <path d="m12 3.4 2.72 5.51 6.08.89-4.4 4.29 1.04 6.06L12 17.29l-5.44 2.86 1.04-6.06-4.4-4.29 6.08-.89Z" />
  </svg>
</template>
