namespace SyncContracts;

/// <summary>Canonical paths of the website sync endpoints. Shared so the signing client and
/// the verifying controller agree (the path is part of the signed canonical string).</summary>
public static class SyncRoutes
{
    public const string Enroll = "/sync/enroll";
    public const string RotateKey = "/sync/rotate-key";
    public const string Push = "/sync/push";
    public const string Fast = "/sync/fast";
    public const string Ack = "/sync/ack";
}
