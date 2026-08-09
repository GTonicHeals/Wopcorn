<script setup lang="ts">
import { ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';

import BaseButton from '@/components/BaseButton.vue';
import FormField from '@/components/FormField.vue';
import { ApiError } from '@/api/client';
import { distributeErrors } from '@/api/formErrors';
import { useAuthStore } from '@/stores/auth';
import { safeNextPath } from '@/router/next';

const auth = useAuthStore();
const route = useRoute();
const router = useRouter();

const displayName = ref('');
const email = ref('');
const password = ref('');
const submitting = ref(false);
const banner = ref('');
const fieldErrors = ref<Record<string, string[]>>({});

function errorsFor(field: string): string[] {
  return fieldErrors.value[field] ?? [];
}

async function submit(): Promise<void> {
  if (submitting.value) return;

  submitting.value = true;
  banner.value = '';
  fieldErrors.value = {};

  try {
    await auth.register({
      email: email.value,
      password: password.value,
      displayName: displayName.value
    });
    await router.replace(safeNextPath(route.query.next));
  } catch (error) {
    // A taken display name is a problem with one field, so it belongs under that
    // field — never in a banner (FR-A2). The 409 carries no `errors` map, so the
    // placement has to be made here.
    if (error instanceof ApiError && error.code === 'display_name_taken') {
      fieldErrors.value = { displayName: [error.message] };
    } else {
      const distributed = distributeErrors(error, ['displayName', 'email', 'password']);
      fieldErrors.value = distributed.fields;
      banner.value = distributed.banner;
    }
  } finally {
    submitting.value = false;
  }
}
</script>

<template>
  <main class="auth">
    <h1 class="auth__wordmark">Wopcorn</h1>
    <p class="auth__tagline">Track what you have watched, and what is next.</p>

    <p v-if="banner" class="auth__banner" role="alert">{{ banner }}</p>

    <form class="auth__form" novalidate @submit.prevent="submit">
      <FormField
        v-model="displayName"
        label="Display name"
        autocomplete="nickname"
        required
        :maxlength="32"
        hint="This is the name your friends see."
        :errors="errorsFor('displayName')"
      />
      <FormField
        v-model="email"
        label="Email"
        type="email"
        autocomplete="email"
        required
        :errors="errorsFor('email')"
      />
      <FormField
        v-model="password"
        label="Password"
        type="password"
        autocomplete="new-password"
        required
        hint="At least 8 characters."
        :errors="errorsFor('password')"
      />

      <BaseButton type="submit" variant="primary" block :loading="submitting">
        Create account
      </BaseButton>
    </form>

    <p class="auth__alt">
      Already have an account?
      <RouterLink :to="{ name: 'login', query: route.query }">Sign in</RouterLink>
    </p>
  </main>
</template>

<style scoped>
.auth {
  width: 100%;
  max-width: 360px;
  margin: 0 auto;
  padding: var(--space-12) var(--space-4) var(--space-8);
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}

.auth__wordmark {
  font-family: var(--font-display);
  font-size: var(--text-2xl);
  font-weight: 500;
  line-height: 1.05;
}

.auth__tagline {
  font-size: var(--text-sm);
  color: var(--text-muted);
  margin-bottom: var(--space-2);
}

.auth__banner {
  border: 1px solid var(--border);
  border-left: 3px solid var(--text);
  border-radius: var(--radius-sm);
  padding: var(--space-3);
  font-size: var(--text-sm);
}

.auth__form {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
  margin-top: var(--space-2);
}

.auth__alt {
  font-size: var(--text-sm);
  color: var(--text-muted);
  margin-top: var(--space-4);
}

/* On a tall viewport an optically centred column beats a form pinned to the
   top of an otherwise empty page. Slightly more padding below than above keeps
   it a touch high of true centre. */
@media (min-width: 900px) {
  .auth {
    min-height: 100dvh;
    justify-content: center;
    padding-top: var(--space-8);
    padding-bottom: var(--space-12);
  }
}
</style>
