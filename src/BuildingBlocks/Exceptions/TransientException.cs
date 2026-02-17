namespace BuildingBlocks.Exceptions;

/// <summary>
/// Represents a transient failure that is safe to retry with backoff.
/// Examples: network timeout, temporary database unavailability, rate limiting.
/// The resilience pipeline (Polly) will catch these and apply retry policies.
/// Maps to HTTP 503 with Retry-After header.
/// </summary>
public class TransientException : Exception
{
    public string Code { get; }
    public TimeSpan? RetryAfter { get; }

    public TransientException(string message, string code = "TRANSIENT_ERROR", TimeSpan? retryAfter = null)
        : base(message)
    {
        Code = code;
        RetryAfter = retryAfter;
    }

    public TransientException(string message, Exception innerException, string code = "TRANSIENT_ERROR", TimeSpan? retryAfter = null)
        : base(message, innerException)
    {
        Code = code;
        RetryAfter = retryAfter;
    }
}
