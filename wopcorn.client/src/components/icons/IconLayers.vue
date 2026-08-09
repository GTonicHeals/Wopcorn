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
    <path d="m12 3 9 5-9 5-9-5 9-5Z" :fill="filled ? 'currentColor' : 'none'" />
    <path d="m3.6 13.5 8.4 4.7 8.4-4.7" />
  </svg>
</template>
