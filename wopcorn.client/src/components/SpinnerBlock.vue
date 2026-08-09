<script setup lang="ts">
withDefaults(defineProps<{ label?: string }>(), { label: 'Loading' });
</script>

<template>
  <div class="spinner-block" role="status" :aria-label="label">
    <span class="spinner-block__ring" aria-hidden="true" />
  </div>
</template>

<style scoped>
.spinner-block {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: var(--space-12) var(--space-4);
}

.spinner-block__ring {
  width: 28px;
  height: 28px;
  border-radius: 50%;
  border: 2px solid var(--border);
  border-top-color: var(--accent);
  animation: spinner-block-spin 750ms linear infinite;
}

@keyframes spinner-block-spin {
  to {
    transform: rotate(360deg);
  }
}
</style>
