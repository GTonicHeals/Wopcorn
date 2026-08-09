import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';

// api/client.ts imports the router to handle 401s; the real one would boot auth.
vi.mock('@/router', () => ({
  default: {
    currentRoute: { value: { name: 'friends', fullPath: '/friends' } },
    replace: vi.fn()
  }
}));

import { useFriendsStore } from '@/stores/friends';
import { useListsStore } from '@/stores/lists';
import { useToastStore } from '@/stores/toasts';
import type { Friend, FriendRequest, FriendsResponse, UserSummary } from '@/api/types';

function user(id: string, displayName: string): UserSummary {
  return { id, displayName, avatarUrl: null };
}

function friend(id: string, displayName: string): Friend {
  return {
    user: user(id, displayName),
    friendsSince: '2026-01-12T00:00:00.000Z',
    tasteMatch: { score: 78, sharedCount: 24, qualified: true }
  };
}

function request(id: string, from: UserSummary): FriendRequest {
  return { id, user: from, sentAt: '2026-08-08T00:00:00.000Z' };
}

function response(partial: Partial<FriendsResponse> = {}): FriendsResponse {
  return { friends: [], incoming: [], outgoing: [], ...partial };
}

const fetchMock = vi.fn();

function ok(body: unknown) {
  return { ok: true, status: 200, json: () => Promise.resolve(body) };
}

function noContent() {
  return { ok: true, status: 204, json: () => Promise.reject(new Error('no body')) };
}

function fail(status: number, code: string, message: string) {
  return { ok: false, status, json: () => Promise.resolve({ code, message }) };
}

function urls(): string[] {
  return fetchMock.mock.calls.map((call) => String(call[0]));
}

beforeEach(() => {
  setActivePinia(createPinia());
  fetchMock.mockReset();
  vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('friends store — the pending badge (FR-F4)', () => {
  it('counts incoming requests and nothing else', async () => {
    fetchMock.mockResolvedValueOnce(
      ok(
        response({
          friends: [friend('f1', 'Ada')],
          incoming: [request('r1', user('u2', 'Bo')), request('r2', user('u3', 'Cleo'))],
          outgoing: [request('r3', user('u4', 'Dae'))]
        })
      )
    );

    const friends = useFriendsStore();
    await friends.load();

    expect(friends.pendingCount).toBe(2);
    expect(friends.friends).toHaveLength(1);
    expect(friends.outgoing).toHaveLength(1);
  });

  it('shares one flight between the shell and the friends screen', async () => {
    fetchMock.mockResolvedValue(ok(response()));

    const friends = useFriendsStore();
    await Promise.all([friends.load(), friends.load(), friends.load()]);

    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('does not refetch once ready unless forced', async () => {
    fetchMock.mockResolvedValue(ok(response()));

    const friends = useFriendsStore();
    await friends.load();
    await friends.load();
    expect(fetchMock).toHaveBeenCalledTimes(1);

    await friends.load(true);
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });
});

describe('friends store — answering requests', () => {
  it('drops the badge at once and re-reads the server after an accept', async () => {
    fetchMock
      .mockResolvedValueOnce(ok(response({ incoming: [request('r1', user('u2', 'Bo'))] })))
      .mockResolvedValueOnce(ok({}))
      .mockResolvedValueOnce(ok(response({ friends: [friend('u2', 'Bo')] })));

    const friends = useFriendsStore();
    await friends.load();
    expect(friends.pendingCount).toBe(1);

    await friends.accept('r1');

    expect(urls()[1]).toBe('/api/friends/requests/r1/accept');
    expect(friends.pendingCount).toBe(0);
    expect(friends.friends.map((entry) => entry.user.id)).toEqual(['u2']);
  });

  it('puts the request back when the accept fails', async () => {
    fetchMock
      .mockResolvedValueOnce(ok(response({ incoming: [request('r1', user('u2', 'Bo'))] })))
      .mockResolvedValueOnce(fail(403, 'forbidden', 'Only the person a request was sent to can answer it.'));

    const friends = useFriendsStore();
    const toasts = useToastStore();
    await friends.load();

    expect(await friends.accept('r1')).toBe(false);
    expect(friends.pendingCount).toBe(1);
    expect(toasts.toasts[0]?.message).toBe(
      'Only the person a request was sent to can answer it.'
    );
  });

  it('re-reads after a 404 — the request was answered somewhere else', async () => {
    fetchMock
      .mockResolvedValueOnce(ok(response({ incoming: [request('r1', user('u2', 'Bo'))] })))
      .mockResolvedValueOnce(fail(404, 'not_found', 'That friend request is no longer waiting.'))
      .mockResolvedValueOnce(ok(response()));

    const friends = useFriendsStore();
    await friends.load();
    await friends.decline('r1');

    expect(urls()[1]).toBe('/api/friends/requests/r1/decline');
    expect(urls()[2]).toBe('/api/friends');
    expect(friends.pendingCount).toBe(0);
  });
});

describe('friends store — withdrawing a sent request (FR-F1)', () => {
  it('drops the row at once and reconciles with the server', async () => {
    fetchMock
      .mockResolvedValueOnce(ok(response({ outgoing: [request('r1', user('u2', 'Bo'))] })))
      .mockResolvedValueOnce(noContent())
      .mockResolvedValueOnce(ok(response()));

    const friends = useFriendsStore();
    await friends.load();
    expect(friends.outgoing).toHaveLength(1);

    expect(await friends.cancel('r1')).toBe(true);

    // The sender's verb is DELETE on the request itself — not decline, which
    // belongs to the recipient and would come back 403.
    expect(urls()[1]).toBe('/api/friends/requests/r1');
    expect(fetchMock.mock.calls[1]?.[1]).toMatchObject({ method: 'DELETE' });
    expect(friends.outgoing).toEqual([]);
  });

  it('puts the row back when the withdrawal fails', async () => {
    fetchMock
      .mockResolvedValueOnce(ok(response({ outgoing: [request('r1', user('u2', 'Bo'))] })))
      .mockResolvedValueOnce(
        fail(403, 'forbidden', 'Only the person who sent a request can withdraw it.')
      );

    const friends = useFriendsStore();
    const toasts = useToastStore();
    await friends.load();

    expect(await friends.cancel('r1')).toBe(false);
    expect(friends.outgoing.map((entry) => entry.id)).toEqual(['r1']);
    expect(toasts.toasts[0]?.message).toBe(
      'Only the person who sent a request can withdraw it.'
    );
  });

  it('re-reads after a 404 — it was accepted or withdrawn elsewhere', async () => {
    fetchMock
      .mockResolvedValueOnce(ok(response({ outgoing: [request('r1', user('u2', 'Bo'))] })))
      .mockResolvedValueOnce(fail(404, 'not_found', 'That friend request is no longer waiting.'))
      .mockResolvedValueOnce(ok(response({ friends: [friend('u2', 'Bo')] })));

    const friends = useFriendsStore();
    await friends.load();

    expect(await friends.cancel('r1')).toBe(false);
    expect(urls()[2]).toBe('/api/friends');
    expect(friends.outgoing).toEqual([]);
    expect(friends.friends.map((entry) => entry.user.id)).toEqual(['u2']);
  });
});

describe('friends store — sending a request', () => {
  it('reports a plain success and refreshes the three lists', async () => {
    fetchMock
      .mockResolvedValueOnce(ok({ id: 'r9', user: user('u5', 'Eve'), sentAt: 'now' }))
      .mockResolvedValueOnce(ok(response({ outgoing: [request('r9', user('u5', 'Eve'))] })));

    const friends = useFriendsStore();

    expect(await friends.sendRequest('u5')).toBe('sent');
    expect(friends.relationshipOf('u5')).toBe('request_sent');
  });

  it('treats 409 already_friends as an answer, not a failure', async () => {
    fetchMock
      .mockResolvedValueOnce(fail(409, 'already_friends', 'You are already friends.'))
      .mockResolvedValueOnce(ok(response({ friends: [friend('u2', 'Bo')] })));

    const friends = useFriendsStore();
    const toasts = useToastStore();

    expect(await friends.sendRequest('u2')).toBe('already_friends');
    expect(friends.relationshipOf('u2')).toBe('friends');
    // The store does not toast an ordinary conflict; the view explains it.
    expect(toasts.toasts).toHaveLength(0);
  });

  it('surfaces request_pending so the view can offer the incoming one', async () => {
    // The reverse request already exists — the right move is to accept it.
    fetchMock
      .mockResolvedValueOnce(
        fail(409, 'request_pending', 'There is already a friend request between you two.')
      )
      .mockResolvedValueOnce(ok(response({ incoming: [request('r7', user('u2', 'Bo'))] })));

    const friends = useFriendsStore();

    expect(await friends.sendRequest('u2')).toBe('request_pending');
    expect(friends.relationshipOf('u2')).toBe('request_received');
    expect(friends.incomingRequestFrom('u2')?.id).toBe('r7');
  });

  it('reports a real failure through a toast', async () => {
    fetchMock.mockResolvedValueOnce(fail(404, 'not_found', 'We could not find that person.'));

    const friends = useFriendsStore();
    const toasts = useToastStore();

    expect(await friends.sendRequest('nobody')).toBe('error');
    expect(toasts.toasts[0]?.message).toBe('We could not find that person.');
  });
});

describe('friends store — relationshipOf', () => {
  it('reports only what it positively knows', async () => {
    fetchMock.mockResolvedValueOnce(
      ok(
        response({
          friends: [friend('u1', 'Ada')],
          incoming: [request('r1', user('u2', 'Bo'))],
          outgoing: [request('r2', user('u3', 'Cleo'))]
        })
      )
    );

    const friends = useFriendsStore();
    await friends.load();

    expect(friends.relationshipOf('u1')).toBe('friends');
    expect(friends.relationshipOf('u2')).toBe('request_received');
    expect(friends.relationshipOf('u3')).toBe('request_sent');
    // A stranger is "no opinion", not "none" — an unloaded store must not make
    // someone with a pending request look addable.
    expect(friends.relationshipOf('u9')).toBeNull();
  });
});

describe('friends store — removing (FR-F3)', () => {
  it('drops the row at once and reconciles with the server', async () => {
    fetchMock
      .mockResolvedValueOnce(ok(response({ friends: [friend('u1', 'Ada'), friend('u2', 'Bo')] })))
      .mockResolvedValueOnce(noContent())
      .mockResolvedValueOnce(ok(response({ friends: [friend('u2', 'Bo')] })));

    const friends = useFriendsStore();
    await friends.load();

    expect(await friends.remove('u1')).toBe(true);
    expect(urls()[1]).toBe('/api/friends/u1');
    expect(friends.friends.map((entry) => entry.user.id)).toEqual(['u2']);
  });

  it('restores the row when the removal fails', async () => {
    fetchMock
      .mockResolvedValueOnce(ok(response({ friends: [friend('u1', 'Ada')] })))
      .mockResolvedValueOnce(fail(500, 'server_error', 'Something broke.'));

    const friends = useFriendsStore();
    await friends.load();

    expect(await friends.remove('u1')).toBe(false);
    expect(friends.friends).toHaveLength(1);
  });
});

describe('sign-out wipes the friends store', () => {
  it('lists.clear() takes friends and the badge with it', async () => {
    fetchMock.mockResolvedValueOnce(
      ok(
        response({
          friends: [friend('u1', 'Ada')],
          incoming: [request('r1', user('u2', 'Bo'))]
        })
      )
    );

    const friends = useFriendsStore();
    const lists = useListsStore();
    await friends.load();

    expect(friends.pendingCount).toBe(1);

    // Leaving these would light the next user's badge with someone else's
    // requests, and show them a stranger's friend list.
    lists.clear();

    expect(friends.friends).toEqual([]);
    expect(friends.incoming).toEqual([]);
    expect(friends.outgoing).toEqual([]);
    expect(friends.pendingCount).toBe(0);
    expect(friends.status).toBe('idle');
  });
});
