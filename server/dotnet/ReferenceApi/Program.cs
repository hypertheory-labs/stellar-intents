using System.Text.Json;
using System.Text.Json.Serialization;
using Edge.Errands;
using Edge.Sse;
using ReferenceApi.Streaming;
using ReferenceApi.Dev;
using ReferenceApi.Holds;
using ReferenceApi.Session;
using Fisher;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Scalar.AspNetCore;
using Wolverine;
using Wolverine.Fisher;
using Wolverine.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFisher(options =>
    {
        // app.db beside the project unless configured (the tests use a throwaway file)
        options.Connection(builder.Configuration.GetConnectionString("Holds") ?? "Data Source=app.db");
        // the hold as it stands now, written in the same transaction as its events
        options.Projections.Snapshot<Hold>(SnapshotLifecycle.Inline);
        options.InitialData.Add(new SeedHolds());
    })
    .IntegrateWithWolverine()
    .ApplyAllDatabaseChangesOnStartup()
    .SeedInitialDataOnStartup()
    .AddAsyncDaemon(DaemonMode.Solo);

builder.Host.UseWolverine(opts =>
{
    // commit each handler's and endpoint's session (and its outbox) as one transaction
    opts.Policies.AutoApplyTransactions();
    // events for the open SSE streams, sent after commit (Edge.Sse)
    opts.UseServerSentEvents();
    // errands' handlers (Edge.Errands)
    opts.UseErrands();
});
builder.Services.AddWolverineHttp();

// What the Edge takes on for a patron (Edge.Errands; unsettled/errands.md). The window is
// how long a patron can take it back for free before it's decided (the same for placing and swapping).
var holdWindow = TimeSpan.FromSeconds(builder.Configuration.GetValue("Errands:Holds:WithdrawWindowSeconds", 3));
builder.Services.AddErrands(errands => errands
    .Add<PlaceHold, PlaceHoldDecider>("place-hold", window: holdWindow, offers: ["swap-hold"])
    .Add<CancelHold, CancelHoldDecider>("cancel-hold")
    .Add<SwapHold, SwapHoldDecider>("swap-hold", window: holdWindow));

// who the user is: a cookie session (see Session/SessionSetup.cs)
builder.Services.AddCookieSession();

// "status": "placed", not "status": 1
builder.Services.ConfigureHttpJsonOptions(json =>
    json.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

// fans committed protocol messages out to open SSE connections (Edge.Sse; the messages
// are in Streaming/). PROTOCOL §3: a heartbeat about every 15-30 s.
builder.Services.AddServerSentEvents(options =>
    options.Heartbeat = TimeSpan.FromSeconds(builder.Configuration.GetValue("Events:HeartbeatSeconds", 20)));

// Which reads go stale when a document is written, from whatever code wrote it (Streaming/Invalidations.cs)
builder.Services.AddSingleton(services => new Invalidations(services.GetRequiredService<SseHub>())
    .When<Hold>(HoldKeys.Changed));
builder.Services.ConfigureFisher((services, store) => store.Listeners.Add(services.GetRequiredService<Invalidations>()));

// The API's documentation: /openapi/v1.json
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "Holds reference API";
    document.Info.Description = "A reference server for the stellar-intents wire protocol (docs/PROTOCOL.md): library holds.";
    return Task.CompletedTask;
}));

// Local Angular dev servers.
builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy => policy
    .WithOrigins("http://localhost:4200", "http://localhost:4201", "http://localhost:4202")
    .AllowAnyMethod()
    .AllowAnyHeader()
    .WithExposedHeaders("ETag", "Location")));

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRequireIdempotencyKey(); // PROTOCOL §1, and the CSRF defence (see Session/)
app.MapOpenApi();
// The same document, as a page to read and try: /scalar. The root redirects there, so
// `dotnet run` (which opens the browser at /) lands on the API's reference.
app.MapScalarApiReference(options => options.WithTitle("Holds reference API"));
app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();
app.MapWolverineEndpoints();
// PROTOCOL §3: the signed-in patron's event stream. "subscribed" first, then
// "intent-decided" and "invalidate" as they happen, and "heartbeat" now and then.
app.MapServerSentEvents("/events", context =>
        context.User.PatronId() is { } patronId ? [ProtocolEvents.PatronGroup(patronId)] : null)
    .RequireAuthorization()
    .WithTags("Events")
    .WithSummary("The signed-in patron's event stream (Server-Sent Events). 401 without a session.");
// PUT asks, GET reads, DELETE withdraws (Edge.Errands). Whoever is signed in is asking.
app.MapErrands("/errands", context => context.User.PatronId())
    .RequireAuthorization()
    .WithTags("Errands");
app.MapDevReset(); // Development only; see Dev/DevReset.cs
app.MapDevSignIn(); // Development only; see Session/DevSignIn.cs
app.Run();

// visible to the tests (Alba boots the real app)
public partial class Program;
