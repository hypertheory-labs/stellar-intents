using Alba;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

[assembly: AssemblyFixture(typeof(ReferenceApi.Tests.AppFixture))]
// one SQLite file underneath everything: one writer at a time
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ReferenceApi.Tests;

/// <summary>The real app, booted once for every test, on a throwaway SQLite file.</summary>
public sealed class AppFixture : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"stellar-tests-{Guid.NewGuid():N}.db");

    public IAlbaHost Host { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.UseSetting("ConnectionStrings:Holds", $"Data Source={_dbPath}");
            // Decisions never happen on their own in tests: a test decides a hold when it
            // wants to (see Decide), so nothing races the assertions.
            builder.UseSetting("Holds:DecisionDelaySeconds", "3600");
            builder.UseSetting("Events:HeartbeatSeconds", "1");
            builder.ConfigureServices(services =>
            {
                services.RunWolverineInSoloMode();
                // handlers that exist only for tests (see AfterCommitTests)
                services.ConfigureWolverine(opts => opts.Discovery.IncludeAssembly(typeof(AppFixture).Assembly));
            });
        });
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        foreach (var file in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_dbPath) + "*"))
            File.Delete(file);
    }
}
