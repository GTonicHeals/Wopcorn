<script setup lang="ts">
import { computed, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';

import BaseButton from '@/components/BaseButton.vue';
import FormField from '@/components/FormField.vue';
import IconKey from '@/components/icons/IconKey.vue';
import { ApiError } from '@/api/client';
import { distributeErrors } from '@/api/formErrors';
import { isPasskeyCancellation, isPasskeySupported } from '@/lib/webauthn';
import { useAuthStore } from '@/stores/auth';
import { safeNextPath } from '@/router/next';

const auth = useAuthStore();
const route = useRoute();
const router = useRouter();

const email = ref('');
const password = ref('');
const submitting = ref(false);
const banner = ref('');
const fieldErrors = ref<Record<string, string[]>>({});

/** Set by ResetPasswordView on the way here — the only confirmation that reset worked. */
const justReset = computed(() => route.query.reset === '1');

// Hidden rather than disabled where WebAuthn is missing: a button that can never
// work is worse than no button.
const passkeysAvailable = isPasskeySupported();
const passkeyBusy = ref(false);

function errorsFor(field: string): string[] {
  return fieldErrors.value[field] ?? [];
}

/**
 * Usernameless: no email is sent, so the browser offers whichever account has a
 * credential for this site.
 */
async function signInWithPasskey(): Promise<void> {
  if (passkeyBusy.value) return;

  passkeyBusy.value = true;
  banner.value = '';
  fieldErrors.value = {};

  try {
    await auth.passkeyLogin();
    await router.replace(safeNextPath(route.query.next));
  } catch (error) {
    // Dismissing the sheet is a decision, not a failure — say nothing.
    if (isPasskeyCancellation(error)) return;

    banner.value =
      error instanceof ApiError
        ? error.message
        : 'That passkey could not be used. Try your password instead.';
  } finally {
    passkeyBusy.value = false;
  }
}

async function submit(): Promise<void> {
  if (submitting.value) return;

  submitting.value = true;
  banner.value = '';
  fieldErrors.value = {};

  try {
    await auth.login({ email: email.value, password: password.value });
    await router.replace(safeNextPath(route.query.next));
  } catch (error) {
    // The 401 has no field home — it is deliberately vague about which half was
    // wrong — so it lands in the banner.
    const distributed = distributeErrors(error, ['email', 'password']);
    fieldErrors.value = distributed.fields;
    banner.value = distributed.banner;
  } finally {
    submitting.value = false;
  }
}
</script>

<template>
  <main class="auth">
    <h1 class="auth__wordmark">Wopcorn</h1>
    <p class="auth__tagline">Sign in to pick up where your queue left off.</p>

    <p v-if="justReset" class="auth__notice" role="status">
      Your password has been changed. Sign in with it.
    </p>

    <p v-if="banner" class="auth__banner" role="alert">{{ banner }}</p>

    <!--
      Passkey first: it is the shorter path for anyone who has one, and putting it
      under the form would hide it below the fold on a 320×568 screen.
    -->
    <template v-if="passkeysAvailable">
      <BaseButton
        variant="secondary"
        block
        :loading="passkeyBusy"
        @click="signInWithPasskey"
      >
        <IconKey />
        <span>Sign in with a passkey</span>
      </BaseButton>

      <p class="auth__divider"><span>or</span></p>
    </template>

    <form class="auth__form" novalidate @submit.prevent="submit">
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
        autocomplete="current-password"
        required
        :errors="errorsFor('password')"
      />

      <BaseButton type="submit" variant="primary" block :loading="submitting">Sign in</BaseButton>
    </form>

    <p class="auth__forgot">
      <RouterLink :to="{ name: 'forgot-password' }">Forgot your password?</RouterLink>
    </p>

    <p class="auth__alt">
      No account yet?
      <RouterLink :to="{ name: 'register', query: route.query }">Create one</RouterLink>
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

/* Confirmation, not an error — the accent marks the user's own completed action. */
.auth__notice {
  border: 1px solid var(--accent);
  border-radius: var(--radius-sm);
  padding: var(--space-3);
  font-size: var(--text-sm);
}

/* A rule with the word set into it, rather than a word floating over a border. */
.auth__divider {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  font-size: var(--text-xs);
  text-transform: uppercase;
  letter-spacing: 0.16em;
  color: var(--text-muted);
  margin: var(--space-2) 0;
}

.auth__divider::before,
.auth__divider::after {
  content: '';
  flex: 1;
  height: 1px;
  background: var(--border);
}

.auth__forgot {
  font-size: var(--text-sm);
  margin-top: var(--space-3);
}

.auth__alt {
  font-size: var(--text-sm);
  color: var(--text-muted);
  margin-top: var(--space-2);
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
