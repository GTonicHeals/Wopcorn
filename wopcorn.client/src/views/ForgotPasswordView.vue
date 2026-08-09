<script setup lang="ts">
import { ref } from 'vue';

import BaseButton from '@/components/BaseButton.vue';
import FormField from '@/components/FormField.vue';
import { ApiError } from '@/api/client';
import { useAuthStore } from '@/stores/auth';

/**
 * Asks for the reset mail. The server answers 202 for an address it has never
 * seen, so this screen must not branch on the result — showing "we sent it" only
 * for real accounts would leak exactly what the 202 exists to hide.
 *
 * The sent state therefore replaces the form unconditionally.
 */
const auth = useAuthStore();

const email = ref('');
const submitting = ref(false);
const sent = ref(false);
const banner = ref('');

async function submit(): Promise<void> {
  if (submitting.value) return;

  submitting.value = true;
  banner.value = '';

  try {
    await auth.forgotPassword(email.value);
    sent.value = true;
  } catch (error) {
    // Only a network or server fault lands here — never "no such account".
    banner.value =
      error instanceof ApiError ? error.message : 'Something went wrong. Try again.';
  } finally {
    submitting.value = false;
  }
}
</script>

<template>
  <main class="auth">
    <h1 class="auth__wordmark">Reset your password</h1>

    <template v-if="sent">
      <p class="auth__tagline">
        If an account exists for <b>{{ email }}</b
        >, a reset link is on its way. The link works once and expires shortly.
      </p>
      <p class="auth__alt">
        <RouterLink :to="{ name: 'login' }">Back to sign in</RouterLink>
      </p>
    </template>

    <template v-else>
      <p class="auth__tagline">
        Enter your email and we'll send a link to choose a new password.
      </p>

      <p v-if="banner" class="auth__banner" role="alert">{{ banner }}</p>

      <form class="auth__form" novalidate @submit.prevent="submit">
        <FormField v-model="email" label="Email" type="email" autocomplete="email" required />

        <BaseButton type="submit" variant="primary" block :loading="submitting">
          Send reset link
        </BaseButton>
      </form>

      <p class="auth__alt">
        Remembered it?
        <RouterLink :to="{ name: 'login' }">Sign in</RouterLink>
      </p>
    </template>
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
