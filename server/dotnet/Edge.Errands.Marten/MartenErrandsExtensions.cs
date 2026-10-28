using JasperFx.Events.Projections;
using Marten;

namespace Edge.Errands.Marten;

public static class MartenErrandsExtensions
{
    /// <summary>Tells Marten what Edge.Errands stores.</summary>
    /// <remarks>The same as Edge.Errands.Fisher's; see there for why it's a package per store.</remarks>
    public static MartenServiceCollectionExtensions.MartenConfigurationExpression IntegrateWithErrands(
        this MartenServiceCollectionExtensions.MartenConfigurationExpression marten)
    {
        marten.Services.ConfigureMarten(store =>
        {
            store.Projections.Snapshot<Errand>(SnapshotLifecycle.Inline);
            // WHY ErrandOffer is registered: a store that doesn't create tables on demand builds
            // its schema only from what's registered. (The snapshot registers Errand.)
            store.Schema.For<ErrandOffer>();
        });
        return marten;
    }
}
