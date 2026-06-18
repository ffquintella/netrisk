namespace Model.DTO;

/// <summary>Configuration for the periodic website sync, editable from the GUI.</summary>
public class WebsiteSyncConfigDto
{
    /// <summary>Bulk (display data) sync interval in minutes. Defaults to 60.</summary>
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>Fast lane (password-reset links, sensitive writes) interval in minutes. Defaults to 2.</summary>
    public int FastIntervalMinutes { get; set; } = 2;

    /// <summary>Website base URL the server syncs to, e.g. https://site:6443.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Skip TLS validation when contacting the website (self-signed certs).</summary>
    public bool Insecure { get; set; }
}
