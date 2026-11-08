// Converts between the JSON the server uses and the binary values browsers need.
// Written out rather than using the browser's newer helpers, which older iPhones lack.

export function fromBase64Url(value: string): ArrayBuffer {
  const base64 = value.replace(/-/g, "+").replace(/_/g, "/");
  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);

  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i);
  }

  return bytes.buffer;
}

export function toBase64Url(value: ArrayBuffer): string {
  let binary = "";

  for (const byte of new Uint8Array(value)) {
    binary += String.fromCharCode(byte);
  }

  return btoa(binary)
    .replace(/\+/g, "-")
    .replace(/\//g, "_")
    .replace(/=+$/, "");
}

interface CredentialDescriptorJson {
  id: string;
  type: PublicKeyCredentialType;
  transports?: AuthenticatorTransport[];
}

type CreationOptionsJson = Omit<
  PublicKeyCredentialCreationOptions,
  "challenge" | "user" | "excludeCredentials"
> & {
  challenge: string;
  user: Omit<PublicKeyCredentialUserEntity, "id"> & { id: string };
  excludeCredentials?: CredentialDescriptorJson[];
};

type RequestOptionsJson = Omit<
  PublicKeyCredentialRequestOptions,
  "challenge" | "allowCredentials"
> & {
  challenge: string;
  allowCredentials?: CredentialDescriptorJson[];
};

function descriptors(
  list: CredentialDescriptorJson[] | undefined,
): PublicKeyCredentialDescriptor[] | undefined {
  return list?.map((item) => ({ ...item, id: fromBase64Url(item.id) }));
}

export function creationOptions(
  json: string,
): PublicKeyCredentialCreationOptions {
  const options = JSON.parse(json) as CreationOptionsJson;

  return {
    ...options,
    challenge: fromBase64Url(options.challenge),
    user: { ...options.user, id: fromBase64Url(options.user.id) },
    excludeCredentials: descriptors(options.excludeCredentials),
  };
}

export function requestOptions(
  json: string,
): PublicKeyCredentialRequestOptions {
  const options = JSON.parse(json) as RequestOptionsJson;

  return {
    ...options,
    challenge: fromBase64Url(options.challenge),
    allowCredentials: descriptors(options.allowCredentials),
  };
}

function common(credential: PublicKeyCredential) {
  return {
    id: credential.id,
    rawId: toBase64Url(credential.rawId),
    type: credential.type,
    clientExtensionResults: credential.getClientExtensionResults(),
    authenticatorAttachment: credential.authenticatorAttachment ?? undefined,
  };
}

export function attestationJson(credential: PublicKeyCredential) {
  const response = credential.response as AuthenticatorAttestationResponse;

  return {
    ...common(credential),
    response: {
      clientDataJSON: toBase64Url(response.clientDataJSON),
      attestationObject: toBase64Url(response.attestationObject),
      transports: response.getTransports?.() ?? [],
    },
  };
}

export function assertionJson(credential: PublicKeyCredential) {
  const response = credential.response as AuthenticatorAssertionResponse;

  return {
    ...common(credential),
    response: {
      clientDataJSON: toBase64Url(response.clientDataJSON),
      authenticatorData: toBase64Url(response.authenticatorData),
      signature: toBase64Url(response.signature),
      userHandle: response.userHandle ? toBase64Url(response.userHandle) : null,
    },
  };
}

export async function createPasskey(optionsJson: string) {
  const credential = await navigator.credentials.create({
    publicKey: creationOptions(optionsJson),
  });
  return attestationJson(credential as PublicKeyCredential);
}

export async function usePasskey(optionsJson: string) {
  const credential = await navigator.credentials.get({
    publicKey: requestOptions(optionsJson),
  });
  return assertionJson(credential as PublicKeyCredential);
}

/** Plain words for what went wrong while talking to the phone's passkey system. */
export function passkeyProblem(error: unknown): string {
  return error instanceof DOMException && error.name === "NotAllowedError"
    ? "That was cancelled, or the passkey wasn't available. Please try again."
    : "Something went wrong with the passkey. Please try again.";
}
