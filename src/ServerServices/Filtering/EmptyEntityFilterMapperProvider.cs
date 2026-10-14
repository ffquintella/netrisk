using Gridify;

namespace ServerServices.Filtering;

/// <summary>
/// The mapper provider for a host that takes no filter from a caller — the Hangfire job host (T237, S39).
///
/// Every entity gets an empty whitelist, so a filter that names any property is refused rather than
/// resolved by reflecting over the entity — the position <see cref="IEntityFilterMapperProvider"/> requires
/// for an entity with no configured map. The job host needs a provider at all only because
/// <c>RisksService</c> takes one (the review cadence job resolves it), and the API's
/// <c>ApplicationEntityFilterMapperProvider</c> lives in the API assembly, which the job host does not
/// reference.
/// </summary>
public sealed class EmptyEntityFilterMapperProvider : IEntityFilterMapperProvider
{
    public IGridifyMapper<T> For<T>() => new GridifyMapper<T>();
}
