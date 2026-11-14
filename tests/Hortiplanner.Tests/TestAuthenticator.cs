using System.Buffers.Binary;
using System.Buffers.Text;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hortiplanner.Tests;

/// <summary>A software passkey, so tests exercise the real WebAuthn checks without a browser.</summary>
internal sealed class TestAuthenticator(string origin = "https://localhost") : IDisposable
{
    private const byte UserPresentAndVerified = 0x05;
    private const byte AttestedCredentialData = 0x40;
    private static readonly string[] Transports = ["internal"];

    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly byte[] credentialId = RandomNumberGenerator.GetBytes(32);
    private string? userHandle;
    private uint counter;

    public string Register(string optionsJson)
    {
        using var options = JsonDocument.Parse(optionsJson);
        var root = options.RootElement;
        userHandle = root.GetProperty("user").GetProperty("id").GetString();

        var clientData = ClientData("webauthn.create", root.GetProperty("challenge").GetString()!);
        var authenticatorData = AuthenticatorData(
            root.GetProperty("rp").GetProperty("id").GetString()!,
            (byte)(UserPresentAndVerified | AttestedCredentialData),
            AttestedData());

        return Credential(new
        {
            clientDataJSON = Base64Url.EncodeToString(clientData),
            attestationObject = Base64Url.EncodeToString(AttestationObject(authenticatorData)),
            transports = Transports,
        });
    }

    public string Authenticate(string optionsJson)
    {
        using var options = JsonDocument.Parse(optionsJson);
        var root = options.RootElement;

        var clientData = ClientData("webauthn.get", root.GetProperty("challenge").GetString()!);
        var authenticatorData = AuthenticatorData(root.GetProperty("rpId").GetString()!, UserPresentAndVerified, []);
        var signature = key.SignData(
            [.. authenticatorData, .. SHA256.HashData(clientData)],
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);

        return Credential(new
        {
            clientDataJSON = Base64Url.EncodeToString(clientData),
            authenticatorData = Base64Url.EncodeToString(authenticatorData),
            signature = Base64Url.EncodeToString(signature),
            userHandle,
        });
    }

    public void Dispose() => key.Dispose();

    private string Credential(object response)
    {
        var id = Base64Url.EncodeToString(credentialId);

        return JsonSerializer.Serialize(new
        {
            id,
            rawId = id,
            type = "public-key",
            response,
            clientExtensionResults = new { },
        });
    }

    private byte[] ClientData(string type, string challenge) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type, challenge, origin, crossOrigin = false }));

    private byte[] AuthenticatorData(string relyingPartyId, byte flags, byte[] attestedData)
    {
        var count = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(count, ++counter);

        return [.. SHA256.HashData(Encoding.UTF8.GetBytes(relyingPartyId)), flags, .. count, .. attestedData];
    }

    private byte[] AttestedData()
    {
        var length = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)credentialId.Length);

        return [.. new byte[16], .. length, .. credentialId, .. PublicKey()];
    }

    private byte[] PublicKey()
    {
        var parameters = key.ExportParameters(includePrivateParameters: false);
        var writer = new CborWriter();
        writer.WriteStartMap(5);
        writer.WriteInt32(1); // Key type: EC2.
        writer.WriteInt32(2);
        writer.WriteInt32(3); // Algorithm: ES256.
        writer.WriteInt32(-7);
        writer.WriteInt32(-1); // Curve: P-256.
        writer.WriteInt32(1);
        writer.WriteInt32(-2);
        writer.WriteByteString(parameters.Q.X!);
        writer.WriteInt32(-3);
        writer.WriteByteString(parameters.Q.Y!);
        writer.WriteEndMap();
        return writer.Encode();
    }

    private static byte[] AttestationObject(byte[] authenticatorData)
    {
        var writer = new CborWriter();
        writer.WriteStartMap(3);
        writer.WriteTextString("fmt");
        writer.WriteTextString("none");
        writer.WriteTextString("attStmt");
        writer.WriteStartMap(0);
        writer.WriteEndMap();
        writer.WriteTextString("authData");
        writer.WriteByteString(authenticatorData);
        writer.WriteEndMap();
        return writer.Encode();
    }
}
