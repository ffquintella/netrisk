using Gridify;

namespace ServerServices.Filtering;

/// <summary>
/// Supplies the Gridify mapper for an entity: the whitelist of filterable and sortable properties
/// and the external name of each. Names are localized, so the mapper is built per request and
/// this is resolved scoped, exactly as the Sieve processor it replaces was.
/// </summary>
public interface IEntityFilterMapperProvider
{
    /// <summary>
    /// The mapper for <typeparamref name="T"/>. Throws when the entity has no configured map,
    /// rather than falling back to reflecting over every public property — an automatic map would
    /// expose columns the API never meant to be filterable.
    /// </summary>
    IGridifyMapper<T> For<T>();
}
