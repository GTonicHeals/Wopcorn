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
    <circle cx="12" cy="8.6" r="3.6" :fill="filled ? 'currentColor' : 'none'" />
    <path d="M4.8 20c.8-3.4 3.8-5 7.2-5s6.4 1.6 7.2 5" :fill="filled ? 'currentColor' : 'none'" />
  </svg>
</template>
