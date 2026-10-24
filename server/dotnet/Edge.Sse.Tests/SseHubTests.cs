using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Edge.Sse.Tests;

/// <summary>The hub on its own, where the app's lifetime can be driven by hand.</summary>
public class SseHubTests
{
    private sealed class Lifetime : IHostApplicationLifetime
    {
        public readonly CancellationTokenSource Stopping = new();
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => Stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() => Stopping.Cancel();
    }

    [Fact] // graceful shutdown must not wait on open streams
    public async Task every_stream_ends_when_the_app_starts_stopping()
    {
        var lifetime = new Lifetime();
        var hub = new SseHub(Options.Create(new SseOptions()), lifetime);
        var stream = hub.Subscribe("P0042", CancellationToken.None).GetAsyncEnumerator();
        (await stream.MoveNextAsync()).ShouldBeTrue(); // the opening event

        var next = stream.MoveNextAsync().AsTask();
        lifetime.StopApplication();

        var ended = await Task.WhenAny(next, Task.Delay(TimeSpan.FromSeconds(5)));
        ended.ShouldBe(next, "the stream should end promptly");
        (await next).ShouldBeFalse("it ends normally, not with an exception");
    }
}
