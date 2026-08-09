<script setup lang="ts">
import { RouterLink } from 'vue-router';

import UserAvatar from '@/components/UserAvatar.vue';
import type { UserSummary } from '@/api/types';

/**
 * One person, four times over: a search result, an incoming request, a friend,
 * a sent request. Same row, different trailing actions — no new visual language
 * per section (fe-07, task 1).
 *
 * The whole name-and-avatar block is the link when there is somewhere to go, so
 * the target clears 44px without a stretched hit area (FR-H4).
 */
defineProps<{
  user: UserSummary;
  /** A route makes the row tappable; friends link to `/u/:userId`. */
  to?: string | null;
}>();
</script>

<template>
  <li class="person">
    <RouterLink v-if="to" :to="to" class="person__main">
      <UserAvatar :user="user" :size="40" />
      <span class="person__text">
        <span class="person__name">{{ user.displayName }}</span>
        <slot name="meta" />
      </span>
    </RouterLink>

    <div v-else class="person__main">
      <UserAvatar :user="user" :size="40" />
      <span class="person__text">
        <span class="person__name">{{ user.displayName }}</span>
        <slot name="meta" />
      </span>
    </div>

    <div class="person__actions"><slot name="actions" /></div>
  </li>
</template>

<style scoped>
.person {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  padding: var(--space-1) 0;
}

.person__main {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  flex: 1;
  min-width: 0;
  min-height: var(--tap-min);
  text-decoration: none;
  color: inherit;
  border-radius: var(--radius-md);
}

.person__text {
  display: flex;
  flex-direction: column;
  gap: 1px;
  min-width: 0;
}

.person__name {
  font-size: var(--text-base);
  font-weight: 600;
  line-height: 1.25;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.person__actions {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  flex: none;
}
</style>
