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
    <circle cx="9" cy="8.2" r="3.4" :fill="filled ? 'currentColor' : 'none'" />
    <path
      d="M2.8 19.5c0-3.3 2.9-5.2 6.2-5.2s6.2 1.9 6.2 5.2"
      :fill="filled ? 'currentColor' : 'none'"
    />
    <circle cx="17.2" cy="9.2" r="2.5" :fill="filled ? 'currentColor' : 'none'" />
    <path d="M16.4 14.5c2.6.4 4.8 2 4.8 4.7" :fill="filled ? 'currentColor' : 'none'" />
  </svg>
</template>
