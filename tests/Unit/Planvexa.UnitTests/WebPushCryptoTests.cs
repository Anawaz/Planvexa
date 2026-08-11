namespace Planvexa.UnitTests.Platform;

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Planvexa.Api.Notifications;
using Shouldly;
using Xunit;

/// <summary>
/// RFC 8291 (payload encryption) and RFC 8292 (VAPID JWT) coverage for <see cref="WebPushCrypto"/>. The
/// round-trip test plays the receiving browser: it decrypts <see cref="WebPushCrypto.Encrypt"/>'s output
/// using only the receiver's own private key and auth secret plus the header the sender attached, exactly
/// as a push service/browser would -- without calling any of WebPushCrypto's internals.
/// </summary>
public sealed class WebPushCryptoTests
{
    [Fact]
    public void Encrypt_round_trips_through_an_independent_receiver_side_decrypt()
    {
        // Simulate the browser generating a PushSubscription: its own P-256 keypair (p256dh) plus a
        // random 16-byte auth secret, exactly what PushManager.subscribe() hands the page.
        using var receiverKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var receiverPublicPoint = ExportUncompressedPoint(receiverKey);
        var authSecret = RandomNumberGenerator.GetBytes(16);

        var p256dh = WebEncoders.Base64UrlEncode(receiverPublicPoint);
        var auth = WebEncoders.Base64UrlEncode(authSecret);
        var plaintext = "When I grow up, I want to be a watermelon"u8.ToArray();

        var encrypted = WebPushCrypto.Encrypt(plaintext, p256dh, auth);

        var decrypted = ReceiverDecrypt(encrypted, receiverKey, receiverPublicPoint, authSecret);

        decrypted.ShouldBe(plaintext);
    }

    [Fact]
    public void Encrypt_header_matches_the_rfc8188_aes128gcm_layout()
    {
        using var receiverKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var receiverPublicPoint = ExportUncompressedPoint(receiverKey);
        var p256dh = WebEncoders.Base64UrlEncode(receiverPublicPoint);
        var auth = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16));
        var plaintext = "hi"u8.ToArray();

        var encrypted = WebPushCrypto.Encrypt(plaintext, p256dh, auth);

        // salt(16) || rs(4) || idlen(1) || keyid(65) || ciphertext(plaintext+1 pad byte) || tag(16)
        encrypted.Length.ShouldBe(16 + 4 + 1 + 65 + (plaintext.Length + 1) + 16);
        encrypted[20].ShouldBe((byte)65); // idlen
        var keyid = encrypted[21..86];
        keyid[0].ShouldBe((byte)0x04); // uncompressed EC point marker -- the sender's ephemeral public key
        var recordSize = BinaryPrimitives.ReadUInt32BigEndian(encrypted.AsSpan(16, 4));
        recordSize.ShouldBe((uint)(plaintext.Length + 1 + 16));

        // Two calls must use independent random ephemeral keys and salts (never reuse a nonce/key pair).
        var encryptedAgain = WebPushCrypto.Encrypt(plaintext, p256dh, auth);
        encryptedAgain.ShouldNotBe(encrypted);
    }

    [Fact]
    public void Encrypt_rejects_a_malformed_p256dh()
        => Should.Throw<ArgumentException>(() => WebPushCrypto.Encrypt("x"u8.ToArray(), WebEncoders.Base64UrlEncode([1, 2, 3]), WebEncoders.Base64UrlEncode(new byte[16])));

    [Fact]
    public void CreateVapidJwt_is_a_well_formed_es256_jwt_signed_by_the_given_key()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var jwt = WebPushCrypto.CreateVapidJwt(key, "https://push.example.com", "mailto:admin@planvexa.local");

        var parts = jwt.Split('.');
        parts.Length.ShouldBe(3);

        var header = JsonSerializer.Deserialize<JsonElement>(WebEncoders.Base64UrlDecode(parts[0]));
        header.GetProperty("alg").GetString().ShouldBe("ES256");
        header.GetProperty("typ").GetString().ShouldBe("JWT");

        var claims = JsonSerializer.Deserialize<JsonElement>(WebEncoders.Base64UrlDecode(parts[1]));
        claims.GetProperty("aud").GetString().ShouldBe("https://push.example.com");
        claims.GetProperty("sub").GetString().ShouldBe("mailto:admin@planvexa.local");
        var exp = claims.GetProperty("exp").GetInt64();
        exp.ShouldBeGreaterThan(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        exp.ShouldBeLessThanOrEqualTo(DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeSeconds());

        var signature = WebEncoders.Base64UrlDecode(parts[2]);
        var signingInput = Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}");
        key.VerifyData(signingInput, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
            .ShouldBeTrue();
    }

    // --- Receiver-side RFC 8291 decryption, written independently from WebPushCrypto.Encrypt to prove
    // the wire format and key derivation actually match what a browser would compute. ---
    private static byte[] ReceiverDecrypt(byte[] encrypted, ECDiffieHellman receiverKey, byte[] receiverPublicPoint, byte[] authSecret)
    {
        var salt = encrypted[..16];
        var idlen = encrypted[20];
        var asPublicPoint = encrypted[21..(21 + idlen)];
        var ciphertextWithTag = encrypted[(21 + idlen)..];

        using var senderPublicKey = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = asPublicPoint[1..33], Y = asPublicPoint[33..65] },
        });
        var sharedSecret = receiverKey.DeriveRawSecretAgreement(senderPublicKey.PublicKey);

        var keyInfo = Concat("WebPush: info\0"u8.ToArray(), receiverPublicPoint, asPublicPoint);
        var prkKey = HKDF.Extract(HashAlgorithmName.SHA256, sharedSecret, authSecret);
        var ikm = HKDF.Expand(HashAlgorithmName.SHA256, prkKey, 32, keyInfo);

        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var cek = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, "Content-Encoding: aes128gcm\0"u8.ToArray());
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, "Content-Encoding: nonce\0"u8.ToArray());

        var ciphertext = ciphertextWithTag[..^16];
        var tag = ciphertextWithTag[^16..];
        var padded = new byte[ciphertext.Length];
        using (var aes = new AesGcm(cek, 16))
        {
            aes.Decrypt(nonce, ciphertext, tag, padded);
        }

        // Strip the trailing 0x02 last-record pad delimiter (RFC 8188 §2).
        padded[^1].ShouldBe((byte)0x02);
        return padded[..^1];
    }

    private static byte[] ExportUncompressedPoint(ECDiffieHellman key)
    {
        var parameters = key.ExportParameters(includePrivateParameters: false);
        var point = new byte[65];
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
