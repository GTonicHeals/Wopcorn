import { computed, ref } from 'vue';
import { defineStore } from 'pinia';

import { api, jsonBody } from '@/api/client';
import {
  credentialToJson,
  parseCreationOptions,
  parseRequestOptions
} from '@/lib/webauthn';
import type {
  AvatarResponse,
  LoginRequest,
  Me,
  PasskeyOptionsResponse,
  PasskeySummary,
  PreferencesRequest,
  RegisterRequest,
  ResetPasswordRequest,
  ServicesRequest,
  UserSummary
} from '@/api/types';

/**
 * The session lives in the server's HttpOnly cookie (FR-A4). Nothing about it is
 * mirrored into localStorage — a second copy only goes stale. `GET /api/auth/me`
 * is the one source of truth, asked once at boot.
 */
export const useAuthStore = defineStore('auth', () => {
  const user = ref<UserSummary | null>(null);
  const status = ref<'loading' | 'ready'>('loading');

  /**
   * Where the user watches, and what they pay for (plan 09).
   *
   * This is user identity rather than a fourth cache, so it lives here beside the
   * user rather than in a store of its own. `region` is null until they have been
   * to settings, and every availability surface in the app is hidden while it is
   * — the filter appears when it can do something, not before.
   */
  const region = ref<string | null>(null);
  const providerIds = ref<number[]>([]);

  /**
   * Whether a friend's suggestion goes straight onto the list it names (plan 10).
   *
   * Loaded beside the services because it comes from the same `GET /api/me`, and
   * off until proven on: it is the setting that decides whether other people may
   * write to your queue, so the pessimistic default is the honest one to assume
   * while the answer is in flight.
   */
  const autoAddSuggestions = ref(false);

  const isAuthenticated = computed(() => user.value !== null);

  /** Whether the availability surfaces have anything to say yet. */
  const hasServices = computed(() => region.value !== null && providerIds.value.length > 0);

  /** Deduplicates the boot request across concurrent navigations. */
  let inFlightBoot: Promise<void> | null = null;

  /** Asks the server who we are. Anonymous endpoint, so a 401 is a normal answer. */
  async function boot(): Promise<void> {
    if (status.value === 'ready') return;
    if (inFlightBoot) return inFlightBoot;

    inFlightBoot = (async () => {
      try {
        user.value = await api<UserSummary>('/api/auth/me', { allow401: true });
      } catch {
        // 401 is the normal signed-out answer. A network failure at boot must not
        // wedge the app on a spinner either: land on /login and let the next call
        // report the real problem.
        user.value = null;
      } finally {
        status.value = 'ready';
        inFlightBoot = null;
      }
    })();

    await inFlightBoot;

    // Not awaited into the boot gate above: the app renders without knowing where
    // you watch, and a slow answer here must not hold the first paint.
    if (user.value) void loadServices();
  }

  /**
   * `GET /api/me` — the region and services `UserSummary` deliberately does not
   * carry. Failure is silent: the availability surfaces simply stay hidden, which
   * is what they do before setup anyway.
   */
  async function loadServices(): Promise<void> {
    try {
      const me = await api<Me>('/api/me');
      region.value = me.region;
      providerIds.value = me.providerIds;
      autoAddSuggestions.value = me.autoAddSuggestions;
    } catch {
      region.value = null;
      providerIds.value = [];
      autoAddSuggestions.value = false;
    }
  }

  /** `PUT /api/me/preferences`. Throws on failure — the toggle reverts itself. */
  async function setAutoAddSuggestions(value: boolean): Promise<void> {
    const saved = await api<PreferencesRequest>('/api/me/preferences', {
      method: 'PUT',
      body: jsonBody({ autoAddSuggestions: value })
    });

    autoAddSuggestions.value = saved.autoAddSuggestions;
  }

  /**
   * `PUT /api/me/services` replaces the whole set, like the queue order and the
   * favourites showcase — add, remove and reorder are one write.
   */
  async function setServices(request: ServicesRequest): Promise<void> {
    const saved = await api<ServicesRequest>('/api/me/services', {
      method: 'PUT',
      body: jsonBody(request)
    });

    region.value = saved.region;
    providerIds.value = saved.providerIds;
  }

  async function register(request: RegisterRequest): Promise<UserSummary> {
    const created = await api<UserSummary>('/api/auth/register', {
      method: 'POST',
      body: jsonBody(request)
    });
    user.value = created;
    status.value = 'ready';
    void loadServices();
    return created;
  }

  async function login(request: LoginRequest): Promise<UserSummary> {
    const signedIn = await api<UserSummary>('/api/auth/login', {
      method: 'POST',
      body: jsonBody(request)
    });
    user.value = signedIn;
    status.value = 'ready';
    void loadServices();
    return signedIn;
  }

  // ------------------------------------------------------------------ passkeys

  /**
   * Usernameless sign-in when `email` is omitted, which is the normal path: the
   * browser offers whichever account holds a credential for this site.
   *
   * The two calls are one exchange — the server stashes the challenge in a
   * short-lived cookie on the first and reads it back on the second — so they
   * cannot be reordered or retried independently.
   */
  async function passkeyLogin(email?: string): Promise<UserSummary> {
    const { optionsJson } = await api<PasskeyOptionsResponse>(
      '/api/auth/passkeys/request-options',
      { method: 'POST', body: jsonBody({ email: email ?? null }) }
    );

    const credential = (await navigator.credentials.get({
      publicKey: parseRequestOptions(optionsJson)
    })) as PublicKeyCredential | null;

    if (!credential) {
      // Browsers resolve null rather than rejecting in some cancellation paths.
      throw new DOMException('Passkey sign-in was cancelled.', 'NotAllowedError');
    }

    const signedIn = await api<UserSummary>('/api/auth/passkeys/signin', {
      method: 'POST',
      body: jsonBody({ credentialJson: credentialToJson(credential) })
    });

    user.value = signedIn;
    status.value = 'ready';
    void loadServices();
    return signedIn;
  }

  async function listPasskeys(): Promise<PasskeySummary[]> {
    return api<PasskeySummary[]>('/api/me/passkeys');
  }

  /** Registers a new credential against the signed-in account. */
  async function addPasskey(name?: string): Promise<PasskeySummary> {
    const { optionsJson } = await api<PasskeyOptionsResponse>(
      '/api/me/passkeys/creation-options',
      { method: 'POST' }
    );

    const credential = (await navigator.credentials.create({
      publicKey: parseCreationOptions(optionsJson)
    })) as PublicKeyCredential | null;

    if (!credential) {
      throw new DOMException('Passkey registration was cancelled.', 'NotAllowedError');
    }

    return api<PasskeySummary>('/api/me/passkeys', {
      method: 'POST',
      body: jsonBody({ credentialJson: credentialToJson(credential), name: name ?? null })
    });
  }

  async function removePasskey(id: string): Promise<void> {
    await api<void>(`/api/me/passkeys/${encodeURIComponent(id)}`, { method: 'DELETE' });
  }

  // ------------------------------------------------------------ password reset

  /**
   * Always resolves — the server answers 202 for an unknown address too, so the
   * caller learns nothing about whether the account exists.
   */
  async function forgotPassword(email: string): Promise<void> {
    await api<void>('/api/auth/forgot-password', {
      method: 'POST',
      body: jsonBody({ email })
    });
  }

  /** Does not sign in on success: the user goes back to /login (API-CONTRACT.md). */
  async function resetPassword(request: ResetPasswordRequest): Promise<void> {
    await api<void>('/api/auth/reset-password', {
      method: 'POST',
      body: jsonBody(request)
    });
  }

  async function logout(): Promise<void> {
    try {
      await api<void>('/api/auth/logout', { method: 'POST' });
    } finally {
      clear();
    }
  }

  async function updateProfile(displayName: string): Promise<UserSummary> {
    const updated = await api<UserSummary>('/api/me', {
      method: 'PUT',
      body: jsonBody({ displayName })
    });
    user.value = updated;
    return updated;
  }

  async function uploadAvatar(file: File): Promise<string | null> {
    const form = new FormData();
    // Field name is part of the contract: PUT /api/me/avatar reads `file`.
    form.append('file', file);

    const { avatarUrl } = await api<AvatarResponse>('/api/me/avatar', {
      method: 'PUT',
      body: form
    });

    if (user.value) user.value = { ...user.value, avatarUrl };
    return avatarUrl;
  }

  async function removeAvatar(): Promise<void> {
    await api<void>('/api/me/avatar', { method: 'DELETE' });
    if (user.value) user.value = { ...user.value, avatarUrl: null };
  }

  /** Drops the local view of the session. Called on sign-out and on any 401. */
  function clear(): void {
    user.value = null;
    status.value = 'ready';
    region.value = null;
    providerIds.value = [];
    autoAddSuggestions.value = false;
  }

  return {
    user,
    status,
    region,
    providerIds,
    autoAddSuggestions,
    isAuthenticated,
    hasServices,
    boot,
    loadServices,
    setServices,
    setAutoAddSuggestions,
    register,
    login,
    passkeyLogin,
    listPasskeys,
    addPasskey,
    removePasskey,
    forgotPassword,
    resetPassword,
    logout,
    updateProfile,
    uploadAvatar,
    removeAvatar,
    clear
  };
});
