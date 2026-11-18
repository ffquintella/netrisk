using System;
using System.Collections.Generic;
using System.Linq;
using DAL.Entities;

namespace GUIClient.Tools.RiskIndex;

/// <summary>
/// The entities a Vision One or SecurityScorecard connection may default to (S40 §6.6 "Entidade padrão da conexão"; T263):
/// the organizational units, processes and applications that own hosts in the index's entity closure (S39 §10.1), never
/// people, teams, data sets or classification levels — a host filed under one of those would sit outside every unit's tree.
/// </summary>
public static class ConnectionEntityChoices
{
    public static readonly IReadOnlySet<string> Types = new HashSet<string>(StringComparer.Ordinal)
    {
        "organization", "organizationUnit", "subOrganizationUnit", "businessProcess", "application"
    };

    public static IReadOnlyList<Entity> Filter(IEnumerable<Entity> entities) =>
        entities.Where(e => Types.Contains(e.DefinitionName))
            .OrderBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
}
