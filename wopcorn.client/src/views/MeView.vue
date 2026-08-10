<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { RouterLink, useRouter } from 'vue-router';

import BaseButton from '@/components/BaseButton.vue';
import FormField from '@/components/FormField.vue';
import ProviderLogo from '@/components/ProviderLogo.vue';
import ScreenHeader from '@/components/ScreenHeader.vue';
import TmdbAttribution from '@/components/TmdbAttribution.vue';
import UserAvatar from '@/components/UserAvatar.vue';
import IconKey from '@/components/icons/IconKey.vue';
import { ApiError } from '@/api/client';
import { distributeErrors } from '@/api/formErrors';
import { formatShortDate } from '@/lib/format';
import { guessRegion, regionOptions } from '@/lib/regions';
import { isPasskeyCancellation, isPasskeySupported } from '@/lib/webauthn';
import { useAuthStore } from '@/stores/auth';
import { useConfigStore } from '@/stores/config';
import { useListsStore } from '@/stores/lists';
import { THEME_OPTIONS, useThemeStore, type ThemePreference } from '@/stores/theme';
import type { PasskeySummary } from '@/api/types';

/** Matches the server's limit (FR-A7); rejected here so the upload never starts. */
const MAX_AVATAR_BYTES = 2 * 1024 * 1024;

const auth = useAuthStore();
const config = useConfigStore();
const theme = useThemeStore();
const lists = useListsStore();
const router = useRouter();

// ------------------------------------------------------------------- rename

const displayName = ref(auth.user?.displayName ?? '');
const savingName = ref(false);
const nameErrors = ref<string[]>([]);
const nameBanner = ref('');
const nameSaved = ref(false);

watch(
  () => auth.user?.displayName,
  (next) => {
    if (next && !savingName.value) displayName.value = next;
  }
);

const nameChanged = computed(
  () => displayName.value.trim() !== (auth.user?.displayName ?? '').trim()
);

async function saveName(): Promise<void> {
  if (savingName.value) return;

  savingName.value = true;
  nameErrors.value = [];
  nameBanner.value = '';
  nameSaved.value = false;

  try {
    await auth.updateProfile(displayName.value.trim());
    nameSaved.value = true;
  } catch (error) {
    // Same rule as registration: a taken name belongs under the field.
    if (error instanceof ApiError && error.code === 'display_name_taken') {
      nameErrors.value = [error.message];
    } else {
      const distributed = distributeErrors(error, ['displayName']);
      nameErrors.value = distributed.fields.displayName ?? [];
      nameBanner.value = distributed.banner;
    }
  } finally {
    savingName.value = false;
  }
}

// ------------------------------------------------------------------- avatar

const fileInput = ref<HTMLInputElement | null>(null);
const previewUrl = ref<string | null>(null);
const uploadingAvatar = ref(false);
const avatarError = ref('');

function revokePreview(): void {
  if (previewUrl.value) {
    URL.revokeObjectURL(previewUrl.value);
    previewUrl.value = null;
  }
}

onBeforeUnmount(revokePreview);

async function onAvatarChosen(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0];
  input.value = ''; // Let the same file be chosen again after a failure.
  if (!file) return;

  avatarError.value = '';

  if (!file.type.startsWith('image/')) {
    avatarError.value = 'Avatars must be a PNG, JPEG, or WebP image.';
    return;
  }
  if (file.size > MAX_AVATAR_BYTES) {
    avatarError.value = 'That image is larger than 2 MB. Pick a smaller one.';
    return;
  }

  // Show the choice immediately; swap to the server's URL when it lands.
  revokePreview();
  previewUrl.value = URL.createObjectURL(file);
  uploadingAvatar.value = true;

  try {
    await auth.uploadAvatar(file);
  } catch (error) {
    avatarError.value =
      error instanceof ApiError ? error.message : 'That upload did not go through.';
  } finally {
    uploadingAvatar.value = false;
    revokePreview();
  }
}

async function removeAvatar(): Promise<void> {
  avatarError.value = '';
  try {
    await auth.removeAvatar();
  } catch (error) {
    avatarError.value =
      error instanceof ApiError ? error.message : 'That did not go through.';
  }
}

// ----------------------------------------------------------- where you watch

/**
 * Setup nobody completes is a feature nobody has, so two things make it likelier:
 * the region is **pre-selected** from the browser's language for the user to
 * confirm — never applied silently — and the directory is shown in TMDB's own
 * `display_priority` order, so the handful of services someone might actually
 * have are above the fold and the long tail is behind "Show all".
 */
const VISIBLE_PROVIDERS = 8;

const regionChoice = ref(auth.region ?? guessRegion() ?? '');
const chosenServices = ref<number[]>([...auth.providerIds]);
const showAllProviders = ref(false);
const savingServices = ref(false);
const servicesError = ref('');
const servicesSaved = ref(false);

const regions = computed(() => regionOptions(auth.region));
const providers = computed(() => config.providersByRegion.get(auth.region ?? '') ?? []);

const visibleProviders = computed(() =>
  showAllProviders.value ? providers.value : providers.value.slice(0, VISIBLE_PROVIDERS)
);

/** A service chosen before the directory arrived must not vanish from the grid. */
const hiddenChosen = computed(
  () => chosenServices.value.filter((id) => !visibleProviders.value.some((p) => p.id === id)).length
);

const servicesChanged = computed(
  () =>
    regionChoice.value !== (auth.region ?? '') ||
    chosenServices.value.length !== auth.providerIds.length ||
    chosenServices.value.some((id) => !auth.providerIds.includes(id))
);

watch(
  () => auth.region,
  (next) => {
    if (next) {
      regionChoice.value = next;
      void config.loadProviders(next);
    }
  },
  { immediate: true }
);

watch(
  () => auth.providerIds,
  (next) => {
    if (!savingServices.value) chosenServices.value = [...next];
  }
);

function toggleService(id: number): void {
  chosenServices.value = chosenServices.value.includes(id)
    ? chosenServices.value.filter((existing) => existing !== id)
    : [...chosenServices.value, id];
}

async function saveServices(): Promise<void> {
  if (savingServices.value || !regionChoice.value) return;

  savingServices.value = true;
  servicesError.value = '';
  servicesSaved.value = false;

  try {
    await auth.setServices({
      region: regionChoice.value,
      providerIds: chosenServices.value
    });
    await config.loadProviders(auth.region);
    servicesSaved.value = true;
  } catch (error) {
    servicesError.value =
      error instanceof ApiError ? error.message : 'That did not go through.';
  } finally {
    savingServices.value = false;
  }
}

// -------------------------------------------------------------------- theme

const themeLabels: Record<ThemePreference, string> = {
  system: 'System',
  light: 'Light',
  dark: 'Dark'
};

// ----------------------------------------------------------------- passkeys

const passkeysAvailable = isPasskeySupported();
const passkeys = ref<PasskeySummary[]>([]);
const passkeysLoading = ref(false);
const passkeyBusy = ref(false);
const passkeyError = ref('');
const passkeyNote = ref('');
/** The id currently being removed — drives the per-row spinner, not a global one. */
const removingId = ref<string | null>(null);

onMounted(() => {
  if (passkeysAvailable) void loadPasskeys();
});

async function loadPasskeys(): Promise<void> {
  passkeysLoading.value = true;
  try {
    passkeys.value = await auth.listPasskeys();
  } catch (error) {
    passkeyError.value =
      error instanceof ApiError ? error.message : 'Could not load your passkeys.';
  } finally {
    passkeysLoading.value = false;
  }
}

async function addPasskey(): Promise<void> {
  if (passkeyBusy.value) return;

  passkeyBusy.value = true;
  passkeyError.value = '';
  passkeyNote.value = '';

  try {
    const created = await auth.addPasskey();
    // Newest first, matching the server's order — no refetch needed.
    passkeys.value = [created, ...passkeys.value];
    passkeyNote.value = `Added "${created.name}".`;
  } catch (error) {
    // Backing out of the system sheet is a decision, not a failure.
    if (isPasskeyCancellation(error)) return;

    passkeyError.value =
      error instanceof ApiError ? error.message : 'That passkey could not be added.';
  } finally {
    passkeyBusy.value = false;
  }
}

async function removePasskey(passkey: PasskeySummary): Promise<void> {
  if (removingId.value) return;

  removingId.value = passkey.id;
  passkeyError.value = '';
  passkeyNote.value = '';

  try {
    await auth.removePasskey(passkey.id);
    passkeys.value = passkeys.value.filter((entry) => entry.id !== passkey.id);
    passkeyNote.value = `Removed "${passkey.name}".`;
  } catch (error) {
    passkeyError.value =
      error instanceof ApiError ? error.message : 'That passkey could not be removed.';
  } finally {
    removingId.value = null;
  }
}

// ----------------------------------------------------------------- sign out

const signingOut = ref(false);

async function signOut(): Promise<void> {
  signingOut.value = true;
  try {
    await auth.logout();
  } finally {
    // Lists, ratings and the film cache are all per-user; none may survive.
    lists.clear();
    signingOut.value = false;
    await router.replace({ name: 'login' });
  }
}
</script>

<template>
  <div class="me">
    <ScreenHeader title="Settings" eyebrow="Your account">
      <template #actions>
        <RouterLink to="/profile" class="me__profile-link">View your profile</RouterLink>
      </template>
    </ScreenHeader>

    <div class="me__body">
      <section class="me__section" aria-labelledby="me-identity">
        <h2 id="me-identity" class="me__legend">Name and photo</h2>

        <div class="me__avatar-row">
          <UserAvatar :user="auth.user" :src="previewUrl" :size="72" />
          <div class="me__avatar-actions">
            <BaseButton
              variant="secondary"
              :loading="uploadingAvatar"
              @click="fileInput?.click()"
            >
              Change photo
            </BaseButton>
            <BaseButton
              v-if="auth.user?.avatarUrl"
              variant="ghost"
              :disabled="uploadingAvatar"
              @click="removeAvatar"
            >
              Remove
            </BaseButton>
            <input
              ref="fileInput"
              class="sr-only"
              type="file"
              accept="image/png,image/jpeg,image/webp"
              aria-label="Choose a profile photo"
              @change="onAvatarChosen"
            />
          </div>
        </div>
        <p v-if="avatarError" class="me__error" role="alert">{{ avatarError }}</p>

        <form class="me__form" novalidate @submit.prevent="saveName">
          <FormField
            v-model="displayName"
            label="Display name"
            autocomplete="nickname"
            required
            :maxlength="32"
            :errors="nameErrors"
          />
          <p v-if="nameBanner" class="me__error" role="alert">{{ nameBanner }}</p>
          <p v-else-if="nameSaved" class="me__note" role="status">Saved.</p>
          <BaseButton
            type="submit"
            variant="primary"
            :loading="savingName"
            :disabled="!nameChanged"
          >
            Save name
          </BaseButton>
        </form>
      </section>

      <!--
        Passkeys are additive: the account always keeps its password, so removing
        the last one here can never lock anyone out (API-CONTRACT.md).
      -->
      <section v-if="passkeysAvailable" class="me__section" aria-labelledby="me-passkeys">
        <h2 id="me-passkeys" class="me__legend">Passkeys</h2>
        <p class="me__note">
          Sign in with your fingerprint, face, or screen lock instead of typing your
          password. Your password keeps working either way.
        </p>

        <p v-if="passkeysLoading" class="me__note">Loading…</p>

        <ul v-else-if="passkeys.length > 0" class="passkeys">
          <li v-for="passkey in passkeys" :key="passkey.id" class="passkeys__row">
            <span class="passkeys__icon" aria-hidden="true"><IconKey /></span>
            <span class="passkeys__text">
              <span class="passkeys__name">{{ passkey.name }}</span>
              <span class="passkeys__meta">
                Added {{ formatShortDate(passkey.createdAt) }}
                <template v-if="passkey.isBackedUp"> · synced</template>
              </span>
            </span>
            <BaseButton
              variant="ghost"
              :loading="removingId === passkey.id"
              :disabled="removingId !== null"
              @click="removePasskey(passkey)"
            >
              Remove
            </BaseButton>
          </li>
        </ul>

        <p v-else class="me__note">No passkeys yet.</p>

        <p v-if="passkeyError" class="me__error" role="alert">{{ passkeyError }}</p>
        <p v-else-if="passkeyNote" class="me__note" role="status">{{ passkeyNote }}</p>

        <BaseButton variant="secondary" :loading="passkeyBusy" @click="addPasskey">
          Add a passkey
        </BaseButton>
      </section>

      <!--
        Where you watch. Setting this is what turns on the availability badges,
        the "Where to watch" block on a title, and the Streaming filter — none of
        which render at all until it is done.
      -->
      <section class="me__section" aria-labelledby="me-watch">
        <h2 id="me-watch" class="me__legend">Where you watch</h2>
        <p class="me__note">
          Wopcorn will show which of your services carry a title, and let you filter
          your queue by what you can watch tonight.
        </p>

        <label class="me__field">
          <span class="me__label">Region</span>
          <select v-model="regionChoice" class="me__select">
            <option value="" disabled>Choose a region</option>
            <option v-for="option in regions" :key="option.code" :value="option.code">
              {{ option.name }}
            </option>
          </select>
        </label>

        <!--
          Which services exist depends on the region, and the server answers that
          for the region it has stored — so the grid appears once the region is
          saved. Two steps, but each is honest about what it knows.
        -->
        <p v-if="!auth.region" class="me__note">
          Save your region and the services available there will appear here.
        </p>

        <p v-else-if="providers.length === 0" class="me__note">
          We could not load the services for {{ auth.region }} just now.
        </p>

        <template v-else>
          <ul class="services">
            <li v-for="provider in visibleProviders" :key="provider.id">
              <!-- Chosen is the user's own state, so it takes the accent. -->
              <button
                type="button"
                class="services__btn"
                :class="{ 'services__btn--on': chosenServices.includes(provider.id) }"
                :aria-pressed="chosenServices.includes(provider.id)"
                @click="toggleService(provider.id)"
              >
                <ProviderLogo :provider="provider" :size="28" />
                <span class="services__name">{{ provider.name }}</span>
              </button>
            </li>
          </ul>

          <button
            v-if="providers.length > visibleProviders.length"
            type="button"
            class="me__disclosure"
            @click="showAllProviders = true"
          >
            Show all {{ providers.length }} services
            <template v-if="hiddenChosen > 0"> ({{ hiddenChosen }} chosen)</template>
          </button>
        </template>

        <p v-if="servicesError" class="me__error" role="alert">{{ servicesError }}</p>
        <p v-else-if="servicesSaved" class="me__note" role="status">Saved.</p>

        <BaseButton
          variant="primary"
          :loading="savingServices"
          :disabled="!regionChoice || !servicesChanged"
          @click="saveServices"
        >
          Save
        </BaseButton>
      </section>

      <section class="me__section" aria-labelledby="me-theme">
        <h2 id="me-theme" class="me__legend">Appearance</h2>
        <div class="me__segmented" role="radiogroup" aria-labelledby="me-theme">
          <button
            v-for="option in THEME_OPTIONS"
            :key="option"
            type="button"
            role="radio"
            class="me__segment"
            :class="{ 'me__segment--on': theme.preference === option }"
            :aria-checked="theme.preference === option"
            @click="theme.set(option)"
          >
            {{ themeLabels[option] }}
          </button>
        </div>
        <p class="me__note">System follows your device's light or dark setting.</p>
      </section>

      <section class="me__section">
        <BaseButton variant="secondary" :loading="signingOut" @click="signOut">
          Sign out
        </BaseButton>
      </section>

      <TmdbAttribution />
    </div>
  </div>
</template>

<style scoped>
.me__body {
  padding: 0 var(--space-4) var(--space-8);
  display: flex;
  flex-direction: column;
  gap: var(--space-8);
  max-width: 520px;
}

.me__section {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  align-items: flex-start;
}

.me__legend {
  font-size: var(--text-xs);
  letter-spacing: 0.16em;
  text-transform: uppercase;
  color: var(--text-muted);
  font-weight: 600;
}

.me__profile-link {
  display: inline-flex;
  align-items: center;
  min-height: var(--tap-min);
  color: var(--text-muted);
  font-size: var(--text-sm);
  font-weight: 600;
}

.me__avatar-row {
  display: flex;
  align-items: center;
  gap: var(--space-4);
  flex-wrap: wrap;
}

/* ------------------------------------------------------------------ passkeys */

.passkeys {
  display: flex;
  flex-direction: column;
  width: 100%;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
}

.passkeys__row {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  padding: var(--space-2) var(--space-2) var(--space-2) var(--space-3);
  min-height: var(--tap-min);
}

.passkeys__row + .passkeys__row {
  border-top: 1px solid var(--border);
}

/* A registered passkey is the user's own credential, so it takes the accent. */
.passkeys__icon {
  display: flex;
  color: var(--accent);
}

.passkeys__icon :deep(svg) {
  width: 20px;
  height: 20px;
}

.passkeys__text {
  display: flex;
  flex-direction: column;
  gap: 2px;
  flex: 1;
  min-width: 0;
}

.passkeys__name {
  font-size: var(--text-sm);
  font-weight: 600;
  overflow-wrap: anywhere;
}

.passkeys__meta {
  font-size: var(--text-xs);
  color: var(--text-muted);
}

/* -------------------------------------------------------- where you watch */

.me__field {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
  width: 100%;
}

.me__label {
  font-size: var(--text-sm);
  font-weight: 600;
}

.me__select {
  min-height: var(--tap-min);
  width: 100%;
  padding: 0 var(--space-3);
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface-raised);
  color: var(--text);
  font: inherit;
  font-size: var(--text-base);
}

.services {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
  gap: var(--space-2);
  width: 100%;
}

.services__btn {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  width: 100%;
  min-height: var(--tap-min);
  padding: var(--space-1) var(--space-2);
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: none;
  color: var(--text-muted);
  font-size: var(--text-sm);
  text-align: left;
}

/* A service you subscribe to is your own state, so it earns the accent. */
.services__btn--on {
  border-color: var(--accent);
  color: var(--text);
  font-weight: 600;
}

.services__name {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.me__disclosure {
  min-height: var(--tap-min);
  border: 0;
  background: none;
  padding: 0;
  color: var(--text);
  font-size: var(--text-xs);
  font-weight: 600;
  text-decoration: underline;
}

.me__avatar-actions {
  display: flex;
  gap: var(--space-2);
  flex-wrap: wrap;
}

.me__form {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  align-items: flex-start;
  width: 100%;
}

.me__form :deep(.field) {
  width: 100%;
}

.me__error {
  font-size: var(--text-sm);
  font-weight: 600;
}

.me__note {
  font-size: var(--text-xs);
  color: var(--text-muted);
}

.me__segmented {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: var(--space-1);
  background: var(--surface-raised);
  border-radius: var(--radius-full);
  padding: 3px;
  width: 100%;
  max-width: 320px;
}

.me__segment {
  min-height: var(--tap-min);
  border: 0;
  background: none;
  border-radius: var(--radius-full);
  color: var(--text-muted);
  font-size: var(--text-sm);
  font-weight: 600;
}

.me__segment--on {
  background: var(--accent);
  color: var(--accent-ink);
}

@media (min-width: 900px) {
  .me__body {
    padding: 0 var(--space-6) var(--space-8);
  }
}
</style>
