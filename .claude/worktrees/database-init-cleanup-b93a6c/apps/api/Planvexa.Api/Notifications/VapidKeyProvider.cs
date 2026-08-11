namespace Planvexa.Api.Notifications;

using System.Security.Cryptography;

/// <summary>
/// Gap-closer (see <see cref="LoggingPushSender"/>'s doc comment): a VAPID (RFC 8292) ECDSA
/// P-256 keypair generated ONCE per process at startup and kept in memory only -- never persisted, so it
/// regenerates on every restart and any subscription created against a prior process's key becomes
/// unaddressable (the frontend re-subscribes against the new key on its next push permission check).
/// This is intentionally dev-scope: production should persist the keypair via configuration/secret store
/// (never source control -- AGENTS.md rule 14) so subscriptions survive a restart.
/// </summary>
public sealed class VapidKeyProvider
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>Base64url-encoded uncompressed public key point (0x04 || X || Y, 65 bytes) -- the exact
    /// format the browser's <c>PushManager.subscribe({ applicationServerKey })</c> expects.</summary>
    public string PublicKeyBase64Url
    {
        get
        {
            var parameters = _key.ExportParameters(includePrivateParameters: false);
            var point = new byte[65];
            point[0] = 0x04;
            parameters.Q.X!.CopyTo(point, 1);
            parameters.Q.Y!.CopyTo(point, 33);
            return Convert.ToBase64String(point).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }
    }
}
