import { getActivePinia } from 'pinia';

import router from '@/router';
import { useAuthStore } from '@/stores/auth';
import type { ApiErrorBody } from '@/api/types';

/**
 * Every non-2xx response arrives as `{ code, message, errors? }` — including
 * 401s, which the server answers as JSON rather than an HTML login redirect.
 * `message` is user-presentable (NFR-10); `code` is the stable machine string.
 */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    message: string,
    readonly errors?: Record<string, string[]>,
    /**
     * The parsed body, when there was one. A few responses carry more than
     * `{ code, message, errors }`: `409 queue_out_of_sync` returns the
     * authoritative `tmdbIds` so an optimistic reorder can reconcile against the
     * response rather than re-fetching the list (FR-D5). Read it through a
     * narrowing helper — nothing may assume a shape here.
     */
    readonly body?: unknown
  ) {
    super(message);
    this.name = 'ApiError';
  }

  /**
   * Field messages for `name`, matched case-insensitively — the server keys the
   * map off .NET model-state names (`DisplayName`) while the client thinks in
   * camelCase.
   */
  fieldErrors(name: string): string[] {
    if (!this.errors) return [];
    const wanted = name.toLowerCase();
    for (const [key, messages] of Object.entries(this.errors)) {
      if (key.toLowerCase() === wanted) return messages;
    }
    return [];
  }
}

const NETWORK_ERROR_MESSAGE = "Wopcorn's server is not responding.";

export type ApiInit = RequestInit & {
  /**
   * Suppresses the sign-out-and-redirect side effect of a 401. Exactly one call
   * needs it: `GET /api/auth/me` during boot, which is allowed to 401.
   */
  allow401?: boolean;
};

function isAbortError(error: unknown): boolean {
  return error instanceof DOMException
    ? error.name === 'AbortError'
    : error instanceof Error && error.name === 'AbortError';
}

/** Clears the session and sends the user to /login, remembering where they were. */
function handleUnauthorized(): void {
  // A 401 can in principle land before Pinia is installed; useAuthStore() would
  // throw and mask the ApiError the caller is waiting for.
  if (getActivePinia()) {
    useAuthStore().clear();
  }

  const current = router.currentRoute.value;
  if (current.name === 'login' || current.name === 'register') return;

  const next = current.fullPath;
  void router.replace({
    name: 'login',
    query: next && next !== '/' ? { next } : {}
  });
}

/**
 * The one way this app talks to its server.
 *
 * - Always sends the session cookie.
 * - String bodies are JSON; `FormData` bodies are left alone so the browser can
 *   set the multipart boundary itself.
 * - Non-2xx throws {@link ApiError}. `204` resolves `undefined`.
 * - `AbortError` propagates untouched so callers can cancel superseded requests.
 */
export async function api<T>(path: string, init: ApiInit = {}): Promise<T> {
  const { allow401 = false, headers, body, ...rest } = init;

  const requestHeaders = new Headers(headers);
  if (body !== undefined && body !== null && !(body instanceof FormData)) {
    if (!requestHeaders.has('Content-Type')) {
      requestHeaders.set('Content-Type', 'application/json');
    }
  }
  if (!requestHeaders.has('Accept')) {
    requestHeaders.set('Accept', 'application/json');
  }

  let response: Response;
  try {
    response = await fetch(path, {
      ...rest,
      body,
      headers: requestHeaders,
      credentials: 'include'
    });
  } catch (error) {
    if (isAbortError(error)) throw error;
    throw new ApiError(0, 'network_error', NETWORK_ERROR_MESSAGE);
  }

  if (!response.ok) {
    if (response.status === 401 && !allow401) {
      handleUnauthorized();
    }
    throw await toApiError(response);
  }

  if (response.status === 204 || response.status === 205) {
    return undefined as T;
  }

  try {
    return (await response.json()) as T;
  } catch (error) {
    if (isAbortError(error)) throw error;
    throw new ApiError(response.status, 'network_error', NETWORK_ERROR_MESSAGE);
  }
}

async function toApiError(response: Response): Promise<ApiError> {
  let parsed: unknown;
  try {
    parsed = await response.json();
  } catch {
    // An HTML error page, an empty body, or a proxy in the way.
    return new ApiError(response.status, 'network_error', NETWORK_ERROR_MESSAGE);
  }

  if (typeof parsed !== 'object' || parsed === null) {
    return new ApiError(response.status, 'network_error', NETWORK_ERROR_MESSAGE);
  }

  const body = parsed as Partial<ApiErrorBody>;
  if (typeof body.code !== 'string' || typeof body.message !== 'string') {
    return new ApiError(response.status, 'network_error', NETWORK_ERROR_MESSAGE);
  }

  return new ApiError(
    response.status,
    body.code,
    body.message,
    normalizeErrors(body.errors),
    parsed
  );
}

function normalizeErrors(errors: unknown): Record<string, string[]> | undefined {
  if (typeof errors !== 'object' || errors === null) return undefined;

  const result: Record<string, string[]> = {};
  for (const [key, value] of Object.entries(errors as Record<string, unknown>)) {
    if (Array.isArray(value)) {
      const messages = value.filter((m): m is string => typeof m === 'string');
      if (messages.length > 0) result[key] = messages;
    } else if (typeof value === 'string') {
      result[key] = [value];
    }
  }

  return Object.keys(result).length > 0 ? result : undefined;
}

/** `JSON.stringify` with the intent spelled out at the call site. */
export function jsonBody(value: unknown): string {
  return JSON.stringify(value);
}
