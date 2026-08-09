/**
 * The translation layer between Wopcorn's JSON and the browser's WebAuthn API.
 *
 * WebAuthn speaks `ArrayBuffer`; JSON speaks base64url. Every id, challenge and
 * signature has to cross that line twice — once on the way to the authenticator
 * and once on the way back — and getting a single one wrong fails as an opaque
 * `NotAllowedError` with no hint as to which field was malformed. That is why
 * the conversion lives here, alone and tested, rather than inline in a view.
 *
 * Browsers that ship the JSON serialisation methods
 * (`PublicKeyCredential.parseCreationOptionsFromJSON` and friends) do this
 * natively and better — they also handle fields added after this file was
 * written. We prefer them and keep the manual path as the fallback.
 */

// ------------------------------------------------------------------- base64url

/**
 * base64url → bytes. Tolerates missing `=` padding, which JSON usually omits.
 *
 * The return type names `ArrayBuffer` rather than the default `ArrayBufferLike`
 * so the result satisfies `BufferSource`: WebAuthn's typings reject a view that
 * might be backed by a `SharedArrayBuffer`, which this never is.
 */
export function base64UrlToBytes(value: string): Uint8Array<ArrayBuffer> {
  const base64 = value.replaceAll('-', '+').replaceAll('_', '/');
  const padded = base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '=');
  const binary = atob(padded);

  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i += 1) {
    bytes[i] = binary.charCodeAt(i);
  }
  return bytes;
}

/** bytes → base64url, unpadded — the form the server decodes. */
export function bytesToBase64Url(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer);

  // String.fromCharCode(...bytes) blows the argument limit on a long
  // attestation object, so build it a chunk at a time.
  let binary = '';
  const chunk = 0x8000;
  for (let i = 0; i < bytes.length; i += chunk) {
    binary += String.fromCharCode(...bytes.subarray(i, i + chunk));
  }

  return btoa(binary).replaceAll('+', '-').replaceAll('/', '_').replaceAll('=', '');
}

// --------------------------------------------------------------- availability

/** Whether this browser can do WebAuthn at all. */
export function isPasskeySupported(): boolean {
  return (
    typeof window !== 'undefined' &&
    typeof window.PublicKeyCredential !== 'undefined' &&
    typeof navigator.credentials?.create === 'function'
  );
}

// -------------------------------------------------------- options conversion

type JsonDescriptor = { id: string; type: string; transports?: string[] };

function toDescriptors(
  list: JsonDescriptor[] | undefined
): PublicKeyCredentialDescriptor[] | undefined {
  return list?.map((entry) => ({
    id: base64UrlToBytes(entry.id),
    type: entry.type as PublicKeyCredentialType,
    transports: entry.transports as AuthenticatorTransport[] | undefined
  }));
}

/**
 * Parses the server's creation options into the shape `navigator.credentials
 * .create` wants. Only `challenge`, `user.id` and the credential id lists are
 * binary; everything else passes through as-is.
 */
export function parseCreationOptions(optionsJson: string): PublicKeyCredentialCreationOptions {
  const native = window.PublicKeyCredential?.parseCreationOptionsFromJSON;
  if (typeof native === 'function') {
    return native.call(window.PublicKeyCredential, JSON.parse(optionsJson));
  }

  const parsed = JSON.parse(optionsJson);
  return {
    ...parsed,
    challenge: base64UrlToBytes(parsed.challenge),
    user: { ...parsed.user, id: base64UrlToBytes(parsed.user.id) },
    excludeCredentials: toDescriptors(parsed.excludeCredentials)
  };
}

/** The sign-in counterpart of {@link parseCreationOptions}. */
export function parseRequestOptions(optionsJson: string): PublicKeyCredentialRequestOptions {
  const native = window.PublicKeyCredential?.parseRequestOptionsFromJSON;
  if (typeof native === 'function') {
    return native.call(window.PublicKeyCredential, JSON.parse(optionsJson));
  }

  const parsed = JSON.parse(optionsJson);
  return {
    ...parsed,
    challenge: base64UrlToBytes(parsed.challenge),
    allowCredentials: toDescriptors(parsed.allowCredentials)
  };
}

// ------------------------------------------------------ credential to the wire

/**
 * Serialises a credential for the server. `toJSON()` is used where the browser
 * has it; the manual branch covers both response kinds, which differ in every
 * field except `clientDataJSON`.
 */
export function credentialToJson(credential: PublicKeyCredential): string {
  const withToJson = credential as PublicKeyCredential & { toJSON?: () => unknown };
  if (typeof withToJson.toJSON === 'function') {
    return JSON.stringify(withToJson.toJSON());
  }

  const response = credential.response;
  const base = {
    id: credential.id,
    rawId: bytesToBase64Url(credential.rawId),
    type: credential.type,
    clientExtensionResults: credential.getClientExtensionResults(),
    authenticatorAttachment: credential.authenticatorAttachment ?? undefined
  };

  if (isAttestation(response)) {
    return JSON.stringify({
      ...base,
      response: {
        clientDataJSON: bytesToBase64Url(response.clientDataJSON),
        attestationObject: bytesToBase64Url(response.attestationObject),
        transports: response.getTransports?.() ?? []
      }
    });
  }

  // Not an attestation, so it is an assertion — the union has exactly two arms,
  // but `in` only narrows the branch it tests.
  const assertion = response as AuthenticatorAssertionResponse;
  return JSON.stringify({
    ...base,
    response: {
      clientDataJSON: bytesToBase64Url(assertion.clientDataJSON),
      authenticatorData: bytesToBase64Url(assertion.authenticatorData),
      signature: bytesToBase64Url(assertion.signature),
      userHandle: assertion.userHandle ? bytesToBase64Url(assertion.userHandle) : null
    }
  });
}

function isAttestation(
  response: AuthenticatorResponse
): response is AuthenticatorAttestationResponse {
  return 'attestationObject' in response;
}

/**
 * True when the user dismissed the passkey sheet rather than hitting a real
 * failure. Cancelling is not an error worth shouting about, and WebAuthn
 * deliberately reports "no credential" and "you said no" identically.
 */
export function isPasskeyCancellation(error: unknown): boolean {
  return (
    error instanceof DOMException &&
    (error.name === 'NotAllowedError' || error.name === 'AbortError')
  );
}
