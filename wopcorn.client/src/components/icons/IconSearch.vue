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
    <circle cx="11" cy="11" r="6.5" :fill="filled ? 'currentColor' : 'none'" />
    <path d="m20.5 20.5-4.8-4.8" />
  </svg>
</template>
