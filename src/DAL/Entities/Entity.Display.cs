using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace DAL.Entities;

/// <summary>
/// How an entity is written down for a human.
///
/// An entity has no <c>name</c> column: the name is a row in the <c>entities_properties</c> bag, so
/// every caller that wanted to show one repeated the same
/// <c>EntitiesProperties.FirstOrDefault(p =&gt; p.Type == "name")</c> lookup — and any list control
/// bound straight to <see cref="Entity"/> and given no template showed the type name
/// (<c>DAL.Entities.Entity</c>) instead, because that is what the default
/// <see cref="object.ToString"/> returns.
/// </summary>
public partial class Entity
{
    /// <summary>
    /// The entity's human name, or <c>#id</c> when the name property was not loaded or is missing —
    /// never the empty string, so a row is always selectable in a list.
    /// </summary>
    /// <remarks>
    /// Computed from the property bag, so it is only meaningful when
    /// <see cref="EntitiesProperties"/> was loaded with the entity.
    /// </remarks>
    [NotMapped]
    public string DisplayName
    {
        get
        {
            var name = EntitiesProperties.FirstOrDefault(p => p.Type == "name")?.Value;
            return string.IsNullOrWhiteSpace(name) ? $"#{Id}" : name;
        }
    }

    public override string ToString() => DisplayName;
}
