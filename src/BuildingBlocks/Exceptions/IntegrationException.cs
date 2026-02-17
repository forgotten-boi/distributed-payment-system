namespace BuildingBlocks.Exceptions;

/// <summary>
/// Represents a failure in communication with an external system (payment provider, 
/// third-party API, database in a different service boundary).
/// These map to HTTP 502 (Bad Gateway) or 503 (Service Unavailable).
/// They MAY be retryable depending on the specific failure mode.
/// </summary>
public class IntegrationException : Exception
{
    public string Code { get; }
    public string? ProviderName { get; }

    public IntegrationException(string message, string code = "INTEGRATION_ERROR", string? providerName = null)
        : base(message)
    {
        Code = code;
        ProviderName = providerName;
    }

    public IntegrationException(string message, Exception innerException, string code = "INTEGRATION_ERROR", string? providerName = null)
        : base(message, innerException)
    {
        Code = code;
        ProviderName = providerName;
    }
}
