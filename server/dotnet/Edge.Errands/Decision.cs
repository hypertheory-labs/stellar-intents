namespace Edge.Errands;

/// <summary>What an errand's decider says: accepted, or declined with a reason.</summary>
/// <remarks>
/// WHY there's no third kind: withdrawn isn't a decision, it's the person taking the
/// errand back before one was made, and only the library records it. And "accepted on
/// other terms" doesn't exist on purpose: accepted means they got what they asked for, or
/// better. Anything less is declined, so the person always consents to getting less
/// (unsettled/errands.md, rule 2).
/// </remarks>
public abstract record Decision
{
    private Decision() { }

    public static Decision Accept() => new Accepted(null);

    /// <param name="made">
    /// The id of what accepting made (a new hold, say), so the person's browser can tell
    /// when the real thing has replaced its guess: memory contains this id.
    /// </param>
    public static Decision Accept(Guid made) => new Accepted(made);

    /// <param name="reason">Shown to the person, so written for one.</param>
    public static Decision Decline(string reason) => new Declined(reason);

    public sealed record Accepted(Guid? Made) : Decision;

    public sealed record Declined : Decision
    {
        // WHY it throws: a decline nobody can explain is a bug, and the person would be
        // left with "no" and nothing to do about it.
        public Declined(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("A decline needs a reason the person can read.", nameof(reason));
            Reason = reason;
        }

        public string Reason { get; }
    }
}
