<script setup lang="ts">
// Nav icons carry their own filled/outline pair: the active state must not be
// colour-only (NFR-9).
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
    stroke-width="1.7"
    stroke-linecap="round"
    stroke-linejoin="round"
  >
    <path d="M3.5 10.8 12 4l8.5 6.8V20a.8.8 0 0 1-.8.8h-4.9v-5.6H9.2v5.6H4.3a.8.8 0 0 1-.8-.8Z" />
  </svg>
</template>
