namespace ElBruno.AI.Decisions;

/// <summary>A decision provider failed or returned a response that could not be interpreted.</summary>
public class DecisionException : Exception
{
    /// <summary>Creates an exception with a message.</summary>
    public DecisionException(string message) : base(message) { }

    /// <summary>Creates an exception with a message and cause.</summary>
    public DecisionException(string message, Exception? innerException) : base(message, innerException) { }

    /// <summary>Gets the HTTP status code when the failure came from an HTTP response.</summary>
    public int? StatusCode { get; init; }
}
