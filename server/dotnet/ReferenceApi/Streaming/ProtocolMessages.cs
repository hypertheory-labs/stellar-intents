using Edge.Sse;

namespace ReferenceApi.Streaming;

/*
  The app's part of the stream's events (PROTOCOL §3): who hears them, and the shape of
  "invalidate". "intent-decided" is Edge.Errands' (ErrandEnded.ToIntentDecided()), because
  its fields are an errand's.

  Both are sent only after a commit: intent-decided as a cascading message (Errands/),
  invalidate from the store's commit listener (Invalidations.cs).
*/

public static class ProtocolEvents
{
    /// <summary>The group of a patron's open streams: every tab they have open.</summary>
    public static string PatronGroup(string patronId) => $"patron:{patronId}";

    /// <summary>Reads under these keys changed.</summary>
    /// <param name="group">Who to tell: a patron's group, or <see cref="SseGroups.Everyone"/>.</param>
    /// <param name="keys">Paths (and query) relative to the API root. See PROTOCOL §3 for how they match.</param>
    public static ServerSentEvent Invalidate(string group, params string[] keys) =>
        new(group, "invalidate", new { keys });
}
