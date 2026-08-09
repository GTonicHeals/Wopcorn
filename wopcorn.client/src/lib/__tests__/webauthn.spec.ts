import { afterEach, describe, expect, it, vi } from 'vitest';

import {
  base64UrlToBytes,
  bytesToBase64Url,
  credentialToJson,
  isPasskeyCancellation,
  parseCreationOptions,
  parseRequestOptions
} from '@/lib/webauthn';

/**
 * These cover the manual conversion path — the one that runs where the browser
 * has no `parseCreationOptionsFromJSON`/`toJSON`. jsdom has neither, so the
 * fallback is what executes here, which is exactly the code worth pinning: a
 * single mis-encoded field fails in the wild as an opaque `NotAllowedError`.
 */

/** Names `ArrayBuffer` explicitly so `.buffer` satisfies the WebAuthn typings. */
function bytes(...values: number[]): Uint8Array<ArrayBuffer> {
  return new Uint8Array(values);
}

/** Fails the test rather than propagating an `undefined` into an assertion. */
function defined<T>(value: T | undefined | null): T {
  expect(value).toBeDefined();
  return value as T;
}

afterEach(() => {
  vi.unstubAllGlobals();
  // parseCreationOptions reads window.PublicKeyCredential; a test that stubs it
  // must not leak that into the next one.
  Reflect.deleteProperty(window, 'PublicKeyCredential');
});

describe('base64url', () => {
  it('round-trips arbitrary bytes', () => {
    const original = bytes(0, 1, 2, 250, 251, 252, 253, 254, 255);

    const encoded = bytesToBase64Url(original.buffer);

    expect(base64UrlToBytes(encoded)).toEqual(original);
  });

  it('emits the url-safe alphabet and no padding', () => {
    // 0xFB 0xFF 0xBF is `+/+/` territory in standard base64.
    const encoded = bytesToBase64Url(bytes(0xfb, 0xff, 0xbf).buffer);

    expect(encoded).not.toMatch(/[+/=]/);
  });

  it('decodes input whose padding was stripped', () => {
    // One, two and three trailing bytes cover all three padding cases.
    for (const length of [1, 2, 3]) {
      const original = bytes(...Array.from({ length }, (_, i) => i + 1));
      const unpadded = bytesToBase64Url(original.buffer).replace(/=+$/, '');

      expect(base64UrlToBytes(unpadded)).toEqual(original);
    }
  });

  it('handles a buffer past the fromCharCode argument limit', () => {
    // Attestation objects run to tens of kilobytes; the chunked loop exists for
    // exactly this and a naive spread would throw here.
    const big = new Uint8Array(100_000).map((_, i) => i % 256);

    expect(base64UrlToBytes(bytesToBase64Url(big.buffer))).toEqual(big);
  });
});

describe('parseCreationOptions', () => {
  const optionsJson = JSON.stringify({
    rp: { id: 'wopcorn.test', name: 'Wopcorn' },
    user: { id: 'AQID', name: 'ada@example.com', displayName: 'ada' },
    challenge: 'BAUG',
    pubKeyCredParams: [{ type: 'public-key', alg: -7 }],
    excludeCredentials: [{ type: 'public-key', id: 'BwgJ', transports: ['internal'] }]
  });

  it('decodes exactly the binary fields and leaves the rest alone', () => {
    const options = parseCreationOptions(optionsJson);

    expect(new Uint8Array(options.challenge as ArrayBuffer)).toEqual(bytes(4, 5, 6));
    expect(new Uint8Array(options.user.id as ArrayBuffer)).toEqual(bytes(1, 2, 3));

    // Untouched passthrough.
    expect(options.user.name).toBe('ada@example.com');
    expect(options.rp.id).toBe('wopcorn.test');
    expect(options.pubKeyCredParams).toEqual([{ type: 'public-key', alg: -7 }]);
  });

  it('decodes credential descriptor ids and keeps their transports', () => {
    const descriptor = defined(parseCreationOptions(optionsJson).excludeCredentials?.[0]);

    expect(new Uint8Array(descriptor.id as ArrayBuffer)).toEqual(bytes(7, 8, 9));
    expect(descriptor.transports).toEqual(['internal']);
  });

  it('defers to the browser when it can parse the JSON itself', () => {
    const native = vi.fn().mockReturnValue({ challenge: 'native' });
    vi.stubGlobal('PublicKeyCredential', { parseCreationOptionsFromJSON: native });

    const result = parseCreationOptions(optionsJson);

    expect(native).toHaveBeenCalledOnce();
    // Handed the parsed object, not the string.
    expect(defined(native.mock.calls[0])[0]).toMatchObject({ challenge: 'BAUG' });
    expect(result).toEqual({ challenge: 'native' });
  });
});

describe('parseRequestOptions', () => {
  it('decodes the challenge and the allow-list', () => {
    const options = parseRequestOptions(
      JSON.stringify({
        challenge: 'CgsM',
        rpId: 'wopcorn.test',
        userVerification: 'required',
        allowCredentials: [{ type: 'public-key', id: 'DQ4P' }]
      })
    );

    expect(new Uint8Array(options.challenge as ArrayBuffer)).toEqual(bytes(10, 11, 12));
    expect(new Uint8Array(defined(options.allowCredentials?.[0]).id as ArrayBuffer)).toEqual(
      bytes(13, 14, 15)
    );
    expect(options.userVerification).toBe('required');
  });

  it('leaves allowCredentials undefined for a usernameless request', () => {
    // The discoverable path sends no allow-list at all; an empty array would mean
    // something different to the authenticator.
    const options = parseRequestOptions(JSON.stringify({ challenge: 'CgsM' }));

    expect(options.allowCredentials).toBeUndefined();
  });
});

describe('credentialToJson', () => {
  const base = {
    id: 'Y3JlZA',
    rawId: bytes(1, 2, 3).buffer,
    type: 'public-key',
    authenticatorAttachment: 'platform',
    getClientExtensionResults: () => ({})
  };

  it('serialises an attestation, including transports', () => {
    const credential = {
      ...base,
      response: {
        clientDataJSON: bytes(4, 5).buffer,
        attestationObject: bytes(6, 7).buffer,
        getTransports: () => ['internal', 'hybrid']
      }
    } as unknown as PublicKeyCredential;

    const payload = JSON.parse(credentialToJson(credential));

    expect(payload.id).toBe('Y3JlZA');
    expect(payload.rawId).toBe(bytesToBase64Url(bytes(1, 2, 3).buffer));
    expect(payload.response.attestationObject).toBe(bytesToBase64Url(bytes(6, 7).buffer));
    expect(payload.response.transports).toEqual(['internal', 'hybrid']);
    // An attestation carries no signature.
    expect(payload.response.signature).toBeUndefined();
  });

  it('serialises an assertion, with a null userHandle when absent', () => {
    const credential = {
      ...base,
      response: {
        clientDataJSON: bytes(4, 5).buffer,
        authenticatorData: bytes(8, 9).buffer,
        signature: bytes(10, 11).buffer,
        userHandle: null
      }
    } as unknown as PublicKeyCredential;

    const payload = JSON.parse(credentialToJson(credential));

    expect(payload.response.signature).toBe(bytesToBase64Url(bytes(10, 11).buffer));
    expect(payload.response.authenticatorData).toBe(bytesToBase64Url(bytes(8, 9).buffer));
    // Explicitly null rather than dropped — the server distinguishes the two.
    expect(payload.response.userHandle).toBeNull();
    expect(payload.response.attestationObject).toBeUndefined();
  });

  it('prefers the browser toJSON when the credential has one', () => {
    const credential = {
      ...base,
      response: { clientDataJSON: bytes(4, 5).buffer, attestationObject: bytes(6).buffer },
      toJSON: () => ({ id: 'from-tojson' })
    } as unknown as PublicKeyCredential;

    expect(JSON.parse(credentialToJson(credential))).toEqual({ id: 'from-tojson' });
  });
});

describe('isPasskeyCancellation', () => {
  it('treats dismissal and abort as cancellation', () => {
    expect(isPasskeyCancellation(new DOMException('no', 'NotAllowedError'))).toBe(true);
    expect(isPasskeyCancellation(new DOMException('no', 'AbortError'))).toBe(true);
  });

  it('does not swallow a real failure', () => {
    expect(isPasskeyCancellation(new DOMException('bad', 'SecurityError'))).toBe(false);
    expect(isPasskeyCancellation(new Error('network'))).toBe(false);
    expect(isPasskeyCancellation(null)).toBe(false);
  });
});
