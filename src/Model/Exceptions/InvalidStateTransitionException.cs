namespace Model.Exceptions;

/// <summary>
/// A state machine refused a transition (Track 3 milestone 3.2.1).
///
/// Distinct from <see cref="InvalidParameterException"/> because the request is well-formed — the
/// caller asked for a real state, on a real record — and it is the current state that makes the
/// request impossible. The API maps this to 422 rather than 400 for exactly that reason: nothing
/// about the payload needs fixing, the record needs to be somewhere else first.
/// </summary>
public class InvalidStateTransitionException : Exception
{
    /// <summary>The state the record is in.</summary>
    public string FromState { get; }

    /// <summary>The state the caller asked for.</summary>
    public string ToState { get; }

    public InvalidStateTransitionException(string fromState, string toState, string message) : base(message)
    {
        FromState = fromState;
        ToState = toState;
    }
}
