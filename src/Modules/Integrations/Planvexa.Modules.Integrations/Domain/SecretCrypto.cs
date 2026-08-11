namespace Planvexa.Modules.Integrations.Domain;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Pure token + signature helpers for the developer platform. Tokens/secrets are stored only as
/// SHA-256 hashes (raw shown once); webhook payloads are signed with HMAC-SHA256. No I/O, no state.
/// </summary>
public static class SecretCrypto
{
    /// <summary>Generates a random hex secret of the given byte length.</summary>
    public static string GenerateSecret(int bytes = 32)
    {
        Span<byte> buffer = stackalloc byte[bytes];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToHexStringLower(buffer);
    }

    /// <summary>SHA-256 hex hash of a raw token (for at-rest storage + lookup).</summary>
    public static string Hash(string raw)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    /// <summary>HMAC-SHA256 hex signature of a payload with a shared secret (webhook signing).</summary>
    public static string Sign(string secret, string payload)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var data = Encoding.UTF8.GetBytes(payload);
        return Convert.ToHexStringLower(HMACSHA256.HashData(key, data));
    }

    /// <summary>
    /// Replay-resistant webhook signature: the same scheme Stripe/GitHub popularised —
    /// the HMAC covers "{unixTimestamp}.{payload}", not just the payload, and the timestamp travels
    /// alongside the signature in the header value itself (<c>t=&lt;unix seconds&gt;,v1=&lt;hex hmac&gt;</c>).
    /// A receiver verifies by recomputing the HMAC over "{t}.{payload}" with the shared secret AND
    /// rejecting the request if <c>|now - t|</c> exceeds a tolerance window (this codebase recommends 5
    /// minutes, matching <see cref="Planvexa.Modules.Integrations.Application.Services.WebhookDispatcher.ReplayToleranceSeconds"/>) —
    /// without the timestamp check, a captured (signature, payload, timestamp) tuple could be replayed to
    /// the receiver indefinitely even though the signature itself is valid.
    /// </summary>
    public static string SignWithTimestamp(string secret, string payload, DateTimeOffset timestamp)
    {
        var unixSeconds = timestamp.ToUnixTimeSeconds();
        var signed = Sign(secret, $"{unixSeconds}.{payload}");
        return $"t={unixSeconds},v1={signed}";
    }

    /// <summary>
    /// Receiver-side counterpart of <see cref="SignWithTimestamp"/>: parses a <c>t=...,v1=...</c> header
    /// value, rejects it outright if malformed, rejects it if <c>t</c> is more than <paramref
    /// name="toleranceSeconds"/> away from <paramref name="now"/> (replay protection), and otherwise
    /// recomputes the HMAC to check <c>v1</c> with a constant-time comparison. Planvexa itself has no
    /// inbound webhook-receiving endpoint today (it only sends), so nothing in this codebase calls this
    /// yet — it exists as the documented, tested verification counterpart a receiver (or a future inbound
    /// endpoint) is expected to implement, and is exercised directly by unit tests.
    /// </summary>
    public static bool VerifyTimestampedSignature(string secret, string payload, string signatureHeader, int toleranceSeconds, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            return false;
        }

        var parts = signatureHeader.Split(',');
        string? timestampPart = null;
        string? signaturePart = null;
        foreach (var part in parts)
        {
            if (part.StartsWith("t=", StringComparison.Ordinal))
            {
                timestampPart = part["t=".Length..];
            }
            else if (part.StartsWith("v1=", StringComparison.Ordinal))
            {
                signaturePart = part["v1=".Length..];
            }
        }

        if (timestampPart is null || signaturePart is null || !long.TryParse(timestampPart, out var unixSeconds))
        {
            return false;
        }

        var timestamp = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if (Math.Abs((now - timestamp).TotalSeconds) > toleranceSeconds)
        {
            return false;
        }

        var expected = Sign(secret, $"{unixSeconds}.{payload}");
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(signaturePart);
        return expectedBytes.Length == actualBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
