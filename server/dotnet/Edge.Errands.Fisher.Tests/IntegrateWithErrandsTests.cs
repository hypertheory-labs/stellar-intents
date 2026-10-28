using System.Text.Json;
using Fisher;
using JasperFx;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.Fisher;

namespace Edge.Errands.Fisher.Tests;

/// <summary>
/// IntegrateWithErrands() tells Fisher everything Edge.Errands stores, so an app that never
/// lets Fisher create tables on demand still works.
/// </summary>
/// <remarks>
/// WHY on-demand creation is off here: it's on by default, so a type nobody registered gets
/// its table the first time it's used, and every other test would pass without this line.
/// With it off, Fisher's ApplyAllDatabaseChangesOnStartup() creates nothing either, so the
/// schema is applied the way a deployment's migration step would, from what's registered.
/// </remarks>
public class IntegrateWithErrandsTests : IAsyncLifetime
{
    public record Want(int N);
    public record Instead(int N);

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

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"errands-fisher-{Guid.NewGuid():N}.db");
    private IHost _host = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddFisher(store =>
            {
                store.Connection($"Data Source={_dbPath}");
                store.AutoCreateSchemaObjects = AutoCreate.None;
            })
            .IntegrateWithWolverine()
            .IntegrateWithErrands();
        builder.Services.AddErrands(errands => errands
            .Add<Want, WantDecider>("want", window: TimeSpan.FromHours(1), offers: ["instead"])
            .Add<Instead, InsteadDecider>("instead", window: TimeSpan.FromHours(1)));
        builder.UseWolverine(wolverine => wolverine.UseErrands());
        _host = builder.Build();
        await _host.StartAsync();
        await _host.Services.GetRequiredService<IDocumentStore>().ApplyAllConfiguredChangesToDatabaseAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
        foreach (var file in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_dbPath) + "*"))
            File.Delete(file);
    }

    [Fact]
    public async Task a_decline_with_offers_and_taking_one_need_no_table_created_on_demand()
    {
        var bus = _host.Services.GetRequiredService<IMessageBus>();
        var id = Guid.NewGuid();
        await bus.InvokeAsync<RequestReply>(new RequestErrand(id, "want", "someone", Ask(new Want(1))));
        await bus.InvokeAsync(new DecideErrand(id));

        var declined = (await bus.InvokeAsync<ReadReply>(new ReadErrand(id))).Errand!;
        declined.Outcome.ShouldBe(ErrandOutcome.Declined);
        var offers = declined.Offers.ShouldNotBeNull();

        var taken = await bus.InvokeAsync<RequestReply>(
            new RequestErrand(offers.Id, "instead", "someone", Ask(new Instead(2))));
        taken.Result.ShouldBe(Requested.Created);
        taken.Errand!.OfferedBy.ShouldBe(id);
    }

    // The ask as Edge.Errands stores it.
    private static string Ask<T>(T ask) => JsonSerializer.Serialize(ask, JsonSerializerOptions.Web);
}
