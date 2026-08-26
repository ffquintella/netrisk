namespace Tools.Security;

/// <summary>
/// How a single-use link key (password reset, and anything else in the <c>links</c> table) is turned
/// into the value stored in <c>key_hash</c> (Track 7 finding NR-2026-014).
///
/// This exists as one shared helper rather than two call sites because the API and the WebSite have
/// to agree, and they are in different projects with no other connection. The API creates the link
/// and stores the hash; the row is then pushed to the WebSite over <c>/sync</c> **verbatim**, and the
/// WebSite hashes the key a visitor presents in a URL and looks it up. If the two ever compute
/// different digests, every password-reset link silently stops resolving — the failure looks like an
/// expired link, and nothing logs an error.
///
/// That is not hypothetical: moving the API from MD5 to SHA-256 without moving the WebSite would have
/// done exactly that.
///
/// The security of a link rests on the key itself — 40 characters of CSPRNG output, about 240 bits
/// (see <see cref="Tools.RandomGenerator"/>) — not on the digest. MD5 was never the weak link here,
/// but a collision-broken digest guarding password-reset links is not something a security product
/// should ship.
/// </summary>
public static class LinkKeyHash
{
    /// <summary>The digest new links are stored under.</summary>
    public static string Primary(string key) => HashTool.CreateSha256(key);

    /// <summary>
    /// The digest links created before the change were stored under.
    ///
    /// Both sides try <see cref="Primary"/> first and fall back to this, so a link already in flight
    /// when an installation upgrades still resolves. The fallback retires itself: links expire — the
    /// shipped reset window is thirty minutes — and the API deletes expired rows on every access.
    /// </summary>
    public static string Legacy(string key) => HashTool.CreateMD5(key);
}
