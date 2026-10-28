using Edge.Errands.StoreTests;
using Fisher;
using JasperFx;
using JasperFx.Events.Documents;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Fisher;

namespace Edge.Errands.Fisher.Tests;

public class ErrandsOnFisher(FisherStore store) : ErrandStoreTests<FisherStore>(store);

/// <summary>Edge.Errands on Fisher, in a throwaway SQLite file.</summary>
public class FisherStore : ErrandStore
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"errands-fisher-{Guid.NewGuid():N}.db");

    protected override void AddStore(IServiceCollection services) =>
        services.AddFisher(store =>
            {
                store.Connection($"Data Source={_dbPath}");
                store.AutoCreateSchemaObjects = AutoCreate.None;
            })
            .IntegrateWithWolverine()
            .IntegrateWithErrands();

    public override IDocumentSessionFactory Sessions => Host.Services.GetRequiredService<IDocumentStore>();

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        foreach (var file in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_dbPath) + "*"))
            File.Delete(file);
    }
}
