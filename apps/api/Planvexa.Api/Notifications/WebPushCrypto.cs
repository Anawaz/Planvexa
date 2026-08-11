namespace Planvexa.Api.Notifications;

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

/// <summary>
/// RFC 8291 payload encryption (the "aes128gcm" content-coding, RFC 8188) and RFC 8292 VAPID JWT signing
/// for Web Push -- see <see cref="LoggingPushSender"/>'s doc comment for why this is stdlib-only
/// (<c>System.Security.Cryptography</c>: ECDiffieHellman/HKDF/AesGcm/ECDsa), no NuGet package. Kept as
/// pure static methods, separate from <see cref="WebPushSender"/>'s HTTP orchestration, so the crypto is
/// unit-testable without mocking <c>HttpClient</c>.
/// </summary>
public static class WebPushCrypto
{
    private const int Aes128KeyLength = 16;
    private const int GcmNonceLength = 12;
    private const int GcmTagLength = 16;
    private const int P256UncompressedPointLength = 65;

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> per RFC 8291 for delivery to a browser PushSubscription,
    /// given its base64url-encoded p256dh (uncompressed P-256 public key) and auth secret. Returns the
    /// full aes128gcm content-coding record (RFC 8188 §2 header + ciphertext+tag) as the raw POST body.
    /// </summary>
    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, string p256dhBase64Url, string authBase64Url)
    {
        var uaPublicKeyBytes = WebEncoders.Base64UrlDecode(p256dhBase64Url);
        var authSecret = WebEncoders.Base64UrlDecode(authBase64Url);
        if (uaPublicKeyBytes.Length != P256UncompressedPointLength || uaPublicKeyBytes[0] != 0x04)
        {
            throw new ArgumentException("p256dh must be an uncompressed 65-byte P-256 point.", nameof(p256dhBase64Url));
        }

        using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var asPublicKeyBytes = ExportUncompressedPoint(ephemeral);

        using var uaPublicKey = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = uaPublicKeyBytes[1..33], Y = uaPublicKeyBytes[33..65] },
        });

        var sharedSecret = ephemeral.DeriveRawSecretAgreement(uaPublicKey.PublicKey);

        // RFC 8291 §3.4: IKM = HKDF-Expand(HKDF-Extract(auth_secret, ecdh_secret), key_info, 32)
        var keyInfo = Concat("WebPush: info\0"u8.ToArray(), uaPublicKeyBytes, asPublicKeyBytes);
        var prkKey = HKDF.Extract(HashAlgorithmName.SHA256, sharedSecret, authSecret);
        var ikm = HKDF.Expand(HashAlgorithmName.SHA256, prkKey, 32, keyInfo);

        // RFC 8188 §2.1: a fresh random salt per message; PRK = HKDF-Extract(salt, IKM).
        var salt = RandomNumberGenerator.GetBytes(16);
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);

        var cek = HKDF.Expand(HashAlgorithmName.SHA256, prk, Aes128KeyLength, "Content-Encoding: aes128gcm\0"u8.ToArray());
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, GcmNonceLength, "Content-Encoding: nonce\0"u8.ToArray());

        // Single-record message: append the 0x02 pad delimiter marking it as the last (and only) record.
        var padded = new byte[plaintext.Length + 1];
        plaintext.CopyTo(padded);
        padded[^1] = 0x02;

        var ciphertext = new byte[padded.Length];
        var tag = new byte[GcmTagLength];
        using (var aes = new AesGcm(cek, GcmTagLength))
        {
            aes.Encrypt(nonce, padded, ciphertext, tag);
        }

        // RFC 8188 §2.1 header: salt(16) || rs(4, big-endian) || idlen(1) || keyid(idlen).
        var recordSize = (uint)(ciphertext.Length + GcmTagLength);
        var header = new byte[16 + 4 + 1 + asPublicKeyBytes.Length];
        salt.CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16, 4), recordSize);
        header[20] = (byte)asPublicKeyBytes.Length;
        asPublicKeyBytes.CopyTo(header, 21);

        return Concat(header, ciphertext, tag);
    }

    /// <summary>
    /// Builds and signs an RFC 8292 VAPID JWT (ES256) authorizing a push to <paramref name="audience"/>
    /// (the push endpoint's origin), expiring after <paramref name="lifetime"/> (default 12h, must stay
    /// under the 24h ceiling RFC 8292 §2 mandates).
    /// </summary>
    public static string CreateVapidJwt(ECDsa signingKey, string audience, string subject, TimeSpan? lifetime = null)
    {
        var exp = DateTimeOffset.UtcNow.Add(lifetime ?? TimeSpan.FromHours(12)).ToUnixTimeSeconds();
        var header = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("""{"typ":"JWT","alg":"ES256"}"""));
        var claims = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(
            $$"""{"aud":"{{audience}}","exp":{{exp}},"sub":"{{subject}}"}"""));

        var signingInput = $"{header}.{claims}";
        var signature = signingKey.SignData(
            Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return $"{signingInput}.{WebEncoders.Base64UrlEncode(signature)}";
    }

    private static byte[] ExportUncompressedPoint(ECDiffieHellman key)
    {
        var parameters = key.ExportParameters(includePrivateParameters: false);
        var point = new byte[P256UncompressedPointLength];
        point[0] = 0x04;
        parameters.Q.X!.CopyTo(point, 1);
        parameters.Q.Y!.CopyTo(point, 33);
        return point;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }
}
