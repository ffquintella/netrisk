using System;

namespace Model.DTO;

/// <summary>
/// One dated entry appended to an incident's timeline (S55, T354): text only. The server stamps the time and the author
/// (the authenticated user); nothing else of the incident changes. Stored as an <c>nr_actions</c> row linked to the
/// incident, whose <c>Message</c> column is <c>varchar(255)</c> — hence <see cref="MaxTextLength"/>.
/// </summary>
public class IncidentTimelineEntryDto
{
    /// <summary>The bound of <c>nr_actions.Message</c>; longer text is refused, never cut.</summary>
    public const int MaxTextLength = 255;

    public int Id { get; set; }

    public int IncidentId { get; set; }

    public DateTime At { get; set; }

    public int? UserId { get; set; }

    public string Text { get; set; } = "";
}

/// <summary>The body of <c>POST /Incidents/{id}/Timeline</c>: the entry's text and nothing a client could stamp itself.</summary>
public class NewIncidentTimelineEntryDto
{
    public string? Text { get; set; }
}
