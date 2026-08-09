import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';

// The real router would run the navigation guard — and therefore the auth boot
// fetch — inside these tests. The 401 behaviour under test is "clear the store
// and send the user to /login", so the store stays real and only the router is
// substituted.
vi.mock('@/router', () => ({
  default: {
    currentRoute: { value: { name: 'lists', fullPath: '/watched' } },
    replace: vi.fn()
  }
}));

import router from '@/router';
import { ApiError, api } from '@/api/client';
import { useAuthStore } from '@/stores/auth';

type FakeResponse = {
  ok: boolean;
  status: number;
  json: () => Promise<unknown>;
};

function respond(status: number, body: unknown, malformed = false): FakeResponse {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: malformed
      ? () => Promise.reject(new SyntaxError('Unexpected token < in JSON'))
      : () => Promise.resolve(body)
  };
}

const fetchMock = vi.fn();

function lastRequestInit(): RequestInit {
  return fetchMock.mock.calls.at(-1)?.[1] as RequestInit;
}

beforeEach(() => {
  setActivePinia(createPinia());
  fetchMock.mockReset();
  vi.mocked(router.replace).mockClear();
  vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('api()', () => {
  it('resolves the parsed body on success', async () => {
    fetchMock.mockResolvedValue(respond(200, { id: 'abc', displayName: 'Ada' }));

    await expect(api<{ id: string }>('/api/auth/me')).resolves.toEqual({
      id: 'abc',
      displayName: 'Ada'
    });
  });

  it('always sends the session cookie', async () => {
    fetchMock.mockResolvedValue(respond(200, {}));

    await api('/api/config');

    expect(lastRequestInit().credentials).toBe('include');
  });

  it('resolves undefined for 204', async () => {
    fetchMock.mockResolvedValue(respond(204, undefined));

    await expect(api<void>('/api/auth/logout', { method: 'POST' })).resolves.toBeUndefined();
  });

  it('labels a string body as JSON but leaves FormData alone', async () => {
    fetchMock.mockResolvedValue(respond(200, {}));

    await api('/api/me', { method: 'PUT', body: JSON.stringify({ displayName: 'Ada' }) });
    expect(new Headers(lastRequestInit().headers).get('Content-Type')).toBe('application/json');

    const form = new FormData();
    form.append('file', new Blob(['x']), 'avatar.png');
    await api('/api/me/avatar', { method: 'PUT', body: form });
    // The browser has to set the multipart boundary itself.
    expect(new Headers(lastRequestInit().headers).get('Content-Type')).toBeNull();
  });
});

describe('ApiError parsing', () => {
  it('carries status, code, message, and the errors map', async () => {
    fetchMock.mockResolvedValue(
      respond(400, {
        code: 'validation_failed',
        message: 'Some fields need attention.',
        errors: { DisplayName: ['Display name must be between 2 and 32 characters.'] }
      })
    );

    const error = await api('/api/auth/register', { method: 'POST', body: '{}' }).catch(
      (e: unknown) => e
    );

    expect(error).toBeInstanceOf(ApiError);
    const apiError = error as ApiError;
    expect(apiError.status).toBe(400);
    expect(apiError.code).toBe('validation_failed');
    expect(apiError.message).toBe('Some fields need attention.');
    expect(apiError.errors).toEqual({
      DisplayName: ['Display name must be between 2 and 32 characters.']
    });
  });

  it('matches field names case-insensitively', async () => {
    fetchMock.mockResolvedValue(
      respond(400, {
        code: 'validation_failed',
        message: 'Some fields need attention.',
        errors: { DisplayName: ['Too short.'] }
      })
    );

    const error = (await api('/api/auth/register', { method: 'POST', body: '{}' }).catch(
      (e: unknown) => e
    )) as ApiError;

    expect(error.fieldErrors('displayName')).toEqual(['Too short.']);
    expect(error.fieldErrors('email')).toEqual([]);
  });

  it('falls back to network_error when the error body is not JSON', async () => {
    fetchMock.mockResolvedValue(respond(502, undefined, true));

    const error = (await api('/api/config').catch((e: unknown) => e)) as ApiError;

    expect(error).toBeInstanceOf(ApiError);
    expect(error.code).toBe('network_error');
    expect(error.message).toBe("Wopcorn's server is not responding.");
    expect(error.status).toBe(502);
  });

  it('falls back to network_error when fetch itself fails', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));

    const error = (await api('/api/config').catch((e: unknown) => e)) as ApiError;

    expect(error).toBeInstanceOf(ApiError);
    expect(error.code).toBe('network_error');
    expect(error.status).toBe(0);
  });

  it('lets AbortError through untouched', async () => {
    const aborted = new DOMException('The operation was aborted.', 'AbortError');
    fetchMock.mockRejectedValue(aborted);

    const error = await api('/api/films/search?q=dune').catch((e: unknown) => e);

    expect(error).toBe(aborted);
    expect(error).not.toBeInstanceOf(ApiError);
  });
});

describe('401 handling', () => {
  const unauthenticated = () =>
    respond(401, { code: 'unauthenticated', message: 'You need to sign in.' });

  it('clears the auth store and redirects to /login with the intended path', async () => {
    const auth = useAuthStore();
    auth.user = { id: 'abc', displayName: 'Ada', avatarUrl: null };
    auth.status = 'ready';

    fetchMock.mockResolvedValue(unauthenticated());

    await expect(api('/api/lists/watched')).rejects.toBeInstanceOf(ApiError);

    expect(auth.user).toBeNull();
    expect(router.replace).toHaveBeenCalledWith({
      name: 'login',
      query: { next: '/watched' }
    });
  });

  it('leaves the store alone when the call opted into allow401', async () => {
    const auth = useAuthStore();
    auth.user = { id: 'abc', displayName: 'Ada', avatarUrl: null };
    auth.status = 'ready';

    fetchMock.mockResolvedValue(unauthenticated());

    await expect(api('/api/auth/me', { allow401: true })).rejects.toBeInstanceOf(ApiError);

    expect(auth.user).not.toBeNull();
    expect(router.replace).not.toHaveBeenCalled();
  });
});
