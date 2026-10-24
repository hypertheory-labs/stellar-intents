namespace Edge.Sse;

/// <summary>How every stream behaves. The defaults are the lessons; change them knowingly.</summary>
public sealed class SseOptions
{
    /// <summary>
    /// Sent first on every connection, including reconnects. It means "you're live now; what
    /// happened while you were away isn't coming, so re-check." Null sends nothing.
    /// </summary>
    /// <remarks>
    /// WHY on by default: without replay, a client can't otherwise tell when to re-check.
    /// A named event nobody listens for costs nothing.
    /// </remarks>
    public string? OpeningEvent { get; set; } = "subscribed";

    /// <summary>How often a "heartbeat" event goes out, so proxies don't close an idle stream.</summary>
    /// <remarks>Proxies commonly close idle connections after 30-60 s; 15-30 s stays under that.</remarks>
    public TimeSpan Heartbeat { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Events one connection may have waiting before its stream is ended (see <see cref="SseHub"/>).</summary>
    /// <remarks>FOR REAL: size it against the burst rate one person can see.</remarks>
    public int PerConnectionCapacity { get; set; } = 64;
}
