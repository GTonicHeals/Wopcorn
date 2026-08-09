<script setup lang="ts">
// Watchlist.
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
    <path d="M6.5 3.5h11V21l-5.5-3.8L6.5 21Z" :fill="filled ? 'currentColor' : 'none'" />
  </svg>
</template>
