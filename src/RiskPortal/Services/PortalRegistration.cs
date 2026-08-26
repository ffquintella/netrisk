using System.Security.Cryptography;

namespace RiskPortal.Services;

/// <summary>The portal's stable identity with the API.</summary>
public interface IPortalRegistration
{
    /// <summary>The client id the API's approved-registration check is keyed on.</summary>
    string ClientId { get; }

    /// <summary>The hostname reported at registration, so an administrator recognises what to approve.</summary>
    string Hostname { get; }
}

/// <summary>
/// Resolves and persists the portal's client id.
///
/// It has to be stable across restarts. Every credential presentation the API accepts is checked
/// against an <em>approved</em> client registration, so a fresh id on each start would ask an
/// administrator to approve the portal again after every deployment — and an operator who has to
/// approve something weekly stops reading what they are approving.
///
/// Order of preference: the configured value, then a previously generated one on disk, then a new
/// one which is written to disk. A directory that cannot be written is not fatal — the portal runs
/// with an in-memory id and says so — because refusing to start over a cache file would be worse
/// than the inconvenience of one extra approval.
/// </summary>
public class PortalRegistration : IPortalRegistration
{
    public const string ClientIdFileName = "portal-client-id";

    public PortalRegistration(PortalOptions options, ILogger<PortalRegistration> logger)
    {
        Hostname = string.IsNullOrWhiteSpace(options.Hostname)
            ? Environment.MachineName
            : options.Hostname.Trim();

        if (!string.IsNullOrWhiteSpace(options.ClientId))
        {
            ClientId = options.ClientId.Trim();
            return;
        }

        var directory = ResolveDataDirectory(options);
        var path = Path.Combine(directory, ClientIdFileName);

        try
        {
            if (File.Exists(path))
            {
                var stored = File.ReadAllText(path).Trim();
                if (!string.IsNullOrWhiteSpace(stored))
                {
                    ClientId = stored;
                    return;
                }
            }

            ClientId = Generate();

            Directory.CreateDirectory(directory);
            File.WriteAllText(path, ClientId);

            logger.LogInformation(
                "Generated a client id for this portal ({ClientId}) and stored it in {Path}. An " +
                "administrator has to approve it in the NetRisk desktop application before anybody " +
                "can sign in.", ClientId, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ClientId = Generate();

            logger.LogWarning(ex,
                "Could not persist a client id under {Directory}; using the in-memory id {ClientId} " +
                "for this process. Set Portal:ClientId to make it stable.", directory, ClientId);
        }
    }

    public string ClientId { get; }

    public string Hostname { get; }

    /// <summary>
    /// 128 bits from a CSPRNG, hex-encoded. Not a GUID: the id is the thing that decides whether a
    /// client may present credentials at all, and <c>Guid.NewGuid</c> makes no cryptographic promise
    /// about its output. This is the rule stated in CLAUDE.md's security conventions table.
    /// </summary>
    private static string Generate() =>
        "portal-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private static string ResolveDataDirectory(PortalOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.DataDirectory)) return options.DataDirectory.Trim();

        // Mirrors where the rest of the product keeps installation state, rather than the working
        // directory, which a systemd unit may not own.
        if (OperatingSystem.IsLinux()) return Path.Combine("/var", "netrisk", "risk-portal");

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "netrisk-risk-portal");
    }
}
