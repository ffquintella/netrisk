using System;
using System.Collections.Generic;
using System.Linq;
using DAL.Entities;

namespace GUIClient.Tools.Hosts;

/// <summary>One choice of the host dialog's entity picker (M58, T282, S39 D-20); <see cref="Id"/> null is "Unassigned".</summary>
public sealed record EntityOption(int? Id, string Label)
{
    /// <summary>
    /// The picker's options: "Unassigned" first, then every entity the server returned that a host can belong to,
    /// by name. People are left out — a host belongs to an organisation, unit or application, never to a person.
    /// </summary>
    public static List<EntityOption> Build(IEnumerable<Entity> entities, string unassignedLabel) =>
        new[] { new EntityOption(null, unassignedLabel) }
            .Concat(entities
                .Where(e => !string.Equals(e.DefinitionName, "person", StringComparison.OrdinalIgnoreCase))
                .Select(e => new EntityOption(e.Id, $"{NameOf(e)} ({e.Id})"))
                .OrderBy(o => o.Label, StringComparer.CurrentCultureIgnoreCase))
            .ToList();

    private static string NameOf(Entity entity) =>
        entity.EntitiesProperties.FirstOrDefault(p => p.Type == "name")?.Value ?? entity.DefinitionName;
}
