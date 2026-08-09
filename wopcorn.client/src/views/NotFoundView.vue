<script setup lang="ts">
import { useRouter } from 'vue-router';

import BaseButton from '@/components/BaseButton.vue';
import EmptyState from '@/components/EmptyState.vue';
import ScreenHeader from '@/components/ScreenHeader.vue';

const router = useRouter();
</script>

<template>
  <div>
    <ScreenHeader title="Not found" />
    <EmptyState headline="That page does not exist" body="The link may be out of date.">
      <template #action>
        <BaseButton variant="secondary" @click="router.push('/')">Go to your feed</BaseButton>
      </template>
    </EmptyState>
  </div>
</template>
