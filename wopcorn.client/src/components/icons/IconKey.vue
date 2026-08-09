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
    <circle cx="8" cy="12" r="4.2" :fill="filled ? 'currentColor' : 'none'" />
    <path d="M12.2 12H21" />
    <path d="M18 12v3.2" />
    <path d="M15 12v2.2" />
  </svg>
</template>
