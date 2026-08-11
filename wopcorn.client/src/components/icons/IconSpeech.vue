<script setup lang="ts">
// A note — either a friend's word on a suggestion, or a comment on something
// watched. Filled reads as "there is one to read"; outline as "write one".
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
    <path
      d="M20.5 12.8c0 3.8-3.8 6.9-8.5 6.9-.9 0-1.8-.1-2.6-.3l-5.4 1.8 1.8-4A6.4 6.4 0 0 1 3.5 12.8C3.5 9 7.3 5.9 12 5.9s8.5 3.1 8.5 6.9Z"
      :fill="filled ? 'currentColor' : 'none'"
    />
  </svg>
</template>
