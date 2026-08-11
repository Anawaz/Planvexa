namespace Planvexa.BuildingBlocks.Platform;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Derives a stable Guid from a seed string — used by background sweeps (due-date/scheduled/SLA
/// automation triggers) that synthesize a <c>WorkspaceEvent</c> with no natural event id of its own. The
/// SAME seed always yields the SAME Guid, so keying the seed on (entity id, calendar day) gives "fires at
/// most once per day" for free via the existing AutomationRun (rule, event id) uniqueness — no separate
/// dedup table needed. Not used for any security property — SHA-256 is used only because it's the
/// non-deprecated hash already reused elsewhere in this codebase (see Integrations' SecretCrypto), not
/// because collision-resistance matters here; the first 16 bytes of the digest become the Guid.
/// </summary>
public static class DeterministicGuid
{
    public static Guid From(string seed) => new(SHA256.HashData(Encoding.UTF8.GetBytes(seed))[..16]);
}
