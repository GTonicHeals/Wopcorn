<script setup lang="ts">
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
    <rect x="3.5" y="3.5" width="7" height="7" rx="1.4" :fill="filled ? 'currentColor' : 'none'" />
    <rect x="13.5" y="3.5" width="7" height="7" rx="1.4" :fill="filled ? 'currentColor' : 'none'" />
    <rect x="3.5" y="13.5" width="7" height="7" rx="1.4" :fill="filled ? 'currentColor' : 'none'" />
    <rect x="13.5" y="13.5" width="7" height="7" rx="1.4" :fill="filled ? 'currentColor' : 'none'" />
  </svg>
</template>
