namespace Rendlio.Interop.Sweep;

/// <summary>
/// Raised when a run cannot go on: a recipe that does not describe a runnable sweep, a ledger
/// line that is not a record, or a registry answering with something other than its documented
/// shape. Wrapping those in one type keeps a scheduled run's failure legible — the message
/// says which input was wrong, and the inner exception says how.
/// </summary>
public sealed class SweepException : Exception
{
    /// <summary>Creates the exception with a message describing the offending input.</summary>
    public SweepException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception, keeping the lower-level failure that caused it.</summary>
    public SweepException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception with no message. Present for the framework's benefit.</summary>
    public SweepException()
    {
    }
}
