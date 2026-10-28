using Edge.Errands.StoreTests;
using JasperFx;
using JasperFx.Events.Documents;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Marten;

namespace Edge.Errands.Marten.Tests;

public class ErrandsOnMarten(MartenStore store) : ErrandStoreTests<MartenStore>(store);

/// <summary>Edge.Errands on Marten, in a schema of its own in a throwaway Postgres.</summary>
/// <remarks>
/// The database comes from ERRANDS_POSTGRES, or a local container:
/// <c>docker run -d --name errands-pg -e POSTGRES_PASSWORD=errands -e POSTGRES_DB=errands -p 5499:5432 postgres:17</c>
/// </remarks>
public class MartenStore : ErrandStore
{
    private static readonly string Connection = Environment.GetEnvironmentVariable("ERRANDS_POSTGRES")
        ?? "Host=localhost;Port=5499;Database=errands;Username=postgres;Password=errands";

    protected override void AddStore(IServiceCollection services) =>
        services.AddMarten(store =>
            {
                store.Connection(Connection);
                store.DatabaseSchemaName = $"errands_{Guid.NewGuid():N}";
                store.AutoCreateSchemaObjects = AutoCreate.None;
            })
            .IntegrateWithWolverine()
            .IntegrateWithErrands();

    public override IDocumentSessionFactory Sessions => Host.Services.GetRequiredService<IDocumentStore>();
}
