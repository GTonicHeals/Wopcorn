<script setup lang="ts">
// The overflow affordance. Destructive actions live behind it rather than beside
// a name, so "Remove" is never one mistaken tap away.
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
    fill="none"
    stroke="currentColor"
    stroke-width="1.7"
    stroke-linecap="round"
    stroke-linejoin="round"
  >
    <circle cx="12" cy="5.2" r="1.5" :fill="filled ? 'currentColor' : 'none'" />
    <circle cx="12" cy="12" r="1.5" :fill="filled ? 'currentColor' : 'none'" />
    <circle cx="12" cy="18.8" r="1.5" :fill="filled ? 'currentColor' : 'none'" />
  </svg>
</template>
