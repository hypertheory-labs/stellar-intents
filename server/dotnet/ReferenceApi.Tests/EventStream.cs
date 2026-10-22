using System.Net.ServerSentEvents;
using System.Text.Json;
using Alba;

namespace ReferenceApi.Tests;

/// <summary>A test's open connection to GET /events, read the way EventSource would.</summary>
public sealed class EventStream : IAsyncDisposable
{
    private readonly HttpResponseMessage _response;
    private readonly IAsyncEnumerator<SseItem<string>> _items;
    private readonly CancellationTokenSource _closed = new();

    private EventStream(HttpResponseMessage response, Stream body)
    {
        _response = response;
        _items = SseParser.Create(body).EnumerateAsync(_closed.Token).GetAsyncEnumerator();
    }

    public static async Task<EventStream> Open(IAlbaHost host, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/events");
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("Accept", "text/event-stream");
        var response = await host.Server.CreateClient()
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        return new EventStream(response, await response.Content.ReadAsStreamAsync());
    }

    /// <summary>The next event, whatever it is.</summary>
    public async Task<SseItem<string>> Next(TimeSpan? within = null)
    {
        var read = _items.MoveNextAsync().AsTask();
        var winner = await Task.WhenAny(read, Task.Delay(within ?? TimeSpan.FromSeconds(10)));
        if (winner != read) throw new TimeoutException("No event arrived in time.");
        if (!await read) throw new InvalidOperationException("The stream ended.");
        return _items.Current;
    }

    /// <summary>The next event of this type, skipping others (heartbeats, mostly). Its data, parsed.</summary>
    // WHY an overall deadline: heartbeats keep arriving, so a per-event timeout alone would
    // skip them forever while waiting for an event that's never coming.
    public async Task<JsonElement> NextOf(string eventType)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < until)
        {
            var item = await Next();
            if (item.EventType == eventType) return JsonDocument.Parse(item.Data).RootElement;
        }
        throw new TimeoutException($"No '{eventType}' event arrived in time.");
    }

    /// <summary>Every event of this type that arrives within the window. Heartbeats keep it moving.</summary>
    public async Task<List<JsonElement>> AllOf(string eventType, TimeSpan window)
    {
        var found = new List<JsonElement>();
        var until = DateTime.UtcNow + window;
        while (DateTime.UtcNow < until)
        {
            var item = await Next();
            if (item.EventType == eventType) found.Add(JsonDocument.Parse(item.Data).RootElement);
        }
        return found;
    }

    public async ValueTask DisposeAsync()
    {
        await _closed.CancelAsync();
        _response.Dispose();
    }
}
