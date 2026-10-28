using System.Text.Json;
using JasperFx.Events.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using JasperFx.Resources;

namespace Edge.Errands.StoreTests;

/// <summary>
/// A host with Edge.Errands on one store, and nothing else: no HTTP, no holds. A store's
/// test project says how to add its store; everything else is the same for all of them.
/// </summary>
/// <remarks>
/// WHY on-demand table creation must be off in <see cref="AddStore"/>: it's on by default, so
/// a type nobody registered gets its table the first time it's used, and a missing
/// registration in IntegrateWithErrands() would never show. The schema is set up once as
/// the host starts, from what's registered, the way a deployment's migration step would.
/// </remarks>
public abstract class ErrandStore : IAsyncLifetime
{
    public record Want(int N);
    public record Instead(int N);
    public record Make(Guid Id);
    public record Now;
    public record Made(Guid Id);

    /// <summary>Declines, offering an <see cref="Instead"/>.</summary>
    public class WantDecider : IDecide<Want>
    {
        public Task<Decision> Decide(Want ask, Errand errand, ErrandSession session, CancellationToken cancellation) =>
            Task.FromResult(Decision.Decline("Not that one.", offers: [new Instead(ask.N + 1)]));
    }

    public class InsteadDecider : IDecide<Instead>
    {
        public Task<Decision> Decide(Instead ask, Errand errand, ErrandSession session, CancellationToken cancellation) =>
            Task.FromResult(Decision.Accept());
    }

    /// <summary>Starts a stream, so it's saved (or not) with the outcome.</summary>
    public class MakeDecider : IDecide<Make>
    {
        public Task<Decision> Decide(Make ask, Errand errand, ErrandSession session, CancellationToken cancellation)
        {
            session.Events.StartStream(ask.Id, new Made(ask.Id));
            return Task.FromResult(Decision.Accept(made: ask.Id));
        }
    }

    public class NowDecider : IDecide<Now>
    {
        public Task<Decision> Decide(Now ask, Errand errand, ErrandSession session, CancellationToken cancellation) =>
            Task.FromResult(Decision.Decline("No."));
    }

    public IHost Host { get; private set; } = null!;

    /// <summary>Adds the store, with on-demand table creation off, and IntegrateWithErrands().</summary>
    protected abstract void AddStore(IServiceCollection services);

    /// <summary>Sessions, as JasperFx's interfaces.</summary>
    public abstract IDocumentSessionFactory Sessions { get; }

    public async ValueTask InitializeAsync()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        AddStore(builder.Services);
        // A window of an hour: a test decides when it wants to, so nothing races it. "now"
        // has none, to race on purpose.
        var hour = TimeSpan.FromHours(1);
        builder.Services.AddErrands(errands => errands
            .Add<Want, WantDecider>("want", window: hour, offers: ["instead"])
            .Add<Instead, InsteadDecider>("instead", window: hour)
            .Add<Make, MakeDecider>("make", window: hour)
            .Add<Now, NowDecider>("now"));
        builder.UseWolverine(wolverine => wolverine.UseErrands());
        // The stores' tables, from what's registered, and Wolverine's own. WHY not each
        // store's ApplyAllDatabaseChangesOnStartup(): with AutoCreate.None, Fisher's creates
        // nothing, and Marten's leaves out Wolverine's tables (store-differences.md).
        builder.Services.AddResourceSetupOnStartup();
        Host = builder.Build();
        await Host.StartAsync();
    }

    public virtual async ValueTask DisposeAsync()
    {
        await Host.StopAsync();
        Host.Dispose();
    }

    /// <summary>The ask as Edge.Errands stores it.</summary>
    public static string Ask<T>(T ask) => JsonSerializer.Serialize(ask, JsonSerializerOptions.Web);
}
