import { describe, expect, it } from "vitest";
import {
  assertionJson,
  attestationJson,
  creationOptions,
  fromBase64Url,
  passkeyProblem,
  requestOptions,
  toBase64Url,
} from "./passkey";

const bytes = (...values: number[]) => new Uint8Array(values).buffer;

describe("base64url", () => {
  it("round-trips values that need the URL-safe characters", () => {
    const value = bytes(251, 255, 254, 0, 1);

    expect(toBase64Url(value)).toBe("-__-AAE");
    expect(new Uint8Array(fromBase64Url(toBase64Url(value)))).toEqual(
      new Uint8Array(value),
    );
  });

  it("leaves off padding", () => {
    expect(toBase64Url(bytes(1))).toBe("AQ");
  });
});

describe("creationOptions", () => {
  it("converts the challenge, user and excluded credentials to binary", () => {
    const options = creationOptions(
      JSON.stringify({
        challenge: "AQID",
        rp: { id: "example.test", name: "Example" },
        user: { id: "BAUG", name: "Sam", displayName: "Sam" },
        pubKeyCredParams: [{ type: "public-key", alg: -7 }],
        excludeCredentials: [{ id: "BwgJ", type: "public-key" }],
      }),
    );

    expect(new Uint8Array(options.challenge as ArrayBuffer)).toEqual(
      new Uint8Array([1, 2, 3]),
    );
    expect(new Uint8Array(options.user.id as ArrayBuffer)).toEqual(
      new Uint8Array([4, 5, 6]),
    );
    expect(
      new Uint8Array(options.excludeCredentials![0].id as ArrayBuffer),
    ).toEqual(new Uint8Array([7, 8, 9]));
    expect(options.rp.id).toBe("example.test");
  });
});

describe("requestOptions", () => {
  it("converts the challenge and copes with no allowed credentials", () => {
    const options = requestOptions(
      JSON.stringify({ challenge: "AQID", rpId: "example.test" }),
    );

    expect(new Uint8Array(options.challenge as ArrayBuffer)).toEqual(
      new Uint8Array([1, 2, 3]),
    );
    expect(options.allowCredentials).toBeUndefined();
  });
});

const credential = (response: object) =>
  ({
    id: "abc",
    rawId: bytes(1, 2),
    type: "public-key",
    authenticatorAttachment: null,
    getClientExtensionResults: () => ({}),
    response,
  }) as unknown as PublicKeyCredential;

describe("credential JSON", () => {
  it("includes what the server needs from a new passkey", () => {
    const json = attestationJson(
      credential({
        clientDataJSON: bytes(1),
        attestationObject: bytes(2),
        getTransports: () => ["internal"],
      }),
    );

    expect(json.rawId).toBe("AQI");
    expect(json.response).toEqual({
      clientDataJSON: "AQ",
      attestationObject: "Ag",
      transports: ["internal"],
    });
  });

  it("includes what the server needs to sign in, with or without a user handle", () => {
    const response = {
      clientDataJSON: bytes(1),
      authenticatorData: bytes(2),
      signature: bytes(3),
    };

    expect(
      assertionJson(credential({ ...response, userHandle: bytes(4) })).response
        .userHandle,
    ).toBe("BA");
    expect(
      assertionJson(credential({ ...response, userHandle: null })).response
        .userHandle,
    ).toBeNull();
  });
});

describe("passkeyProblem", () => {
  it("explains a cancelled passkey in plain words", () => {
    expect(passkeyProblem(new DOMException("no", "NotAllowedError"))).toContain(
      "cancelled",
    );
  });

  it("has a general message for anything else", () => {
    expect(passkeyProblem(new Error("boom"))).toContain("went wrong");
  });
});
