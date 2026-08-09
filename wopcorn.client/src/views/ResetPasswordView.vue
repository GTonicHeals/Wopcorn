<script setup lang="ts">
import { computed, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';

import BaseButton from '@/components/BaseButton.vue';
import FormField from '@/components/FormField.vue';
import { ApiError } from '@/api/client';
import { distributeErrors } from '@/api/formErrors';
import { useAuthStore } from '@/stores/auth';

/**
 * The far end of the emailed link: `/reset-password?email=…&token=…`.
 *
 * A successful reset does not sign anyone in (API-CONTRACT.md) — a link should
 * not mint a session. It hands off to /login with the email prefilled.
 */
const auth = useAuthStore();
const route = useRoute();
const router = useRouter();

// Query values arrive already percent-decoded, so the token goes back to the
// server exactly as Identity issued it.
const email = computed(() => String(route.query.email ?? ''));
const token = computed(() => String(route.query.token ?? ''));

/** A link missing either half can never work; say so instead of failing on submit. */
const linkComplete = computed(() => email.value !== '' && token.value !== '');

const password = ref('');
const confirm = ref('');
const submitting = ref(false);
const banner = ref('');
const fieldErrors = ref<Record<string, string[]>>({});
const mismatch = ref(false);

function errorsFor(field: string): string[] {
  return fieldErrors.value[field] ?? [];
}

async function submit(): Promise<void> {
  if (submitting.value) return;

  // Checked here rather than server-side: the confirmation field is a client
  // affordance and never travels.
  mismatch.value = password.value !== confirm.value;
  if (mismatch.value) return;

  submitting.value = true;
  banner.value = '';
  fieldErrors.value = {};

  try {
    await auth.resetPassword({
      email: email.value,
      token: token.value,
      password: password.value
    });

    await router.replace({ name: 'login', query: { reset: '1' } });
  } catch (error) {
    if (error instanceof ApiError && error.code === 'invalid_reset_token') {
      // Nothing on this form can fix a dead token, so it is a banner, not a field.
      banner.value = error.message;
    } else {
      const distributed = distributeErrors(error, ['password']);
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
    <h1 class="auth__wordmark">Choose a new password</h1>

    <template v-if="!linkComplete">
      <p class="auth__tagline">
        That reset link is incomplete. Ask for a new one and open it directly from
        the mail.
      </p>
      <p class="auth__alt">
        <RouterLink :to="{ name: 'forgot-password' }">Request a new link</RouterLink>
      </p>
    </template>

    <template v-else>
      <p class="auth__tagline">Setting a new password for <b>{{ email }}</b>.</p>

      <p v-if="banner" class="auth__banner" role="alert">
        {{ banner }}
        <RouterLink :to="{ name: 'forgot-password' }">Request a new link</RouterLink>
      </p>

      <form class="auth__form" novalidate @submit.prevent="submit">
        <FormField
          v-model="password"
          label="New password"
          type="password"
          autocomplete="new-password"
          required
          :errors="errorsFor('password')"
        />
        <FormField
          v-model="confirm"
          label="Confirm new password"
          type="password"
          autocomplete="new-password"
          required
          :errors="mismatch ? ['Those two passwords do not match.'] : []"
        />

        <BaseButton type="submit" variant="primary" block :loading="submitting">
          Set new password
        </BaseButton>
      </form>

      <p class="auth__alt">
        <RouterLink :to="{ name: 'login' }">Back to sign in</RouterLink>
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
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  align-items: flex-start;
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
