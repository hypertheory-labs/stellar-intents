using System.Net.ServerSentEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace Edge.Sse.Tests;

public record Succeed(string Group);
public record FailAfterPublishing(string Group);

// Wolverine finds handlers by convention: a class named *Handler.
public static class SucceedHandler
{
    public static ServerSentEvent Handle(Succeed command) => new(command.Group, "done", new { });
}

public static class FailAfterPublishingHandler
{
    public static async Task Handle(FailAfterPublishing command, IMessageContext bus)
    {
        await bus.PublishAsync(new ServerSentEvent(command.Group, "done", new { }));
        throw new InvalidOperationException("the handler failed after publishing");
    }
}

/// <summary>
/// The one guarantee Edge.Sse needs Wolverine for: an event goes out only if its handler
/// succeeds. (Whether a database commit that fails stops it too is tested in the reference
/// app, which has a database.)
/// </summary>
public class AfterCommitTests : IAsyncLifetime
{
    private IHost _host = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddServerSentEvents();
        builder.UseWolverine(opts =>
        {
            opts.ApplicationAssembly = typeof(AfterCommitTests).Assembly;
            opts.UseServerSentEvents();
        });
        _host = builder.Build();
        await _host.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact] // the control: proves the wait below is long enough to see an event
    public async Task an_event_from_a_handler_that_succeeds_goes_out()
    {
        var stream = await Open("g");
        await _host.Services.GetRequiredService<IMessageBus>().InvokeAsync(new Succeed("g"));
        (await Next(stream)).ShouldNotBeNull().EventType.ShouldBe("done");
    }

    [Fact]
    public async Task an_event_from_a_handler_that_fails_never_goes_out()
    {
        var stream = await Open("g");
        await Should.ThrowAsync<InvalidOperationException>(
            _host.Services.GetRequiredService<IMessageBus>().InvokeAsync(new FailAfterPublishing("g")));
        (await Next(stream)).ShouldBeNull();
    }

    private async Task<IAsyncEnumerator<SseItem<object>>> Open(string group)
    {
        var stream = _host.Services.GetRequiredService<SseHub>().Subscribe([group], CancellationToken.None).GetAsyncEnumerator();
        await stream.MoveNextAsync(); // the opening event: it's in its group now
        return stream;
    }

    private static async Task<SseItem<object>?> Next(IAsyncEnumerator<SseItem<object>> stream)
    {
        var read = stream.MoveNextAsync().AsTask();
        return await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(1))) == read && await read
            ? stream.Current
            : null;
    }
}
