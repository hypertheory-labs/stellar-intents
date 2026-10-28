using Fisher;
using JasperFx.Events.Projections;

namespace Edge.Errands.Fisher;

public static class FisherErrandsExtensions
{
    /// <summary>Tells Fisher what Edge.Errands stores.</summary>
    /// <remarks>
    /// WHY a package per store: each store's configuration is its own type, with nothing
    /// shared to hang this on. Wolverine's IntegrateWithWolverine() is split the same way.
    /// </remarks>
    public static FisherConfigurationExpression IntegrateWithErrands(this FisherConfigurationExpression fisher)
    {
        fisher.Services.ConfigureFisher(store =>
        {
            store.Projections.Snapshot<Errand>(SnapshotLifecycle.Inline);
            // WHY ErrandOffer is registered: a store that doesn't create tables on demand builds
            // its schema only from what's registered. (The snapshot registers Errand.)
            store.Schema.For<ErrandOffer>();
        });
        return fisher;
    }
}
