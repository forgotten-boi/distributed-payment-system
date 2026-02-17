namespace BuildingBlocks.Exceptions;

/// <summary>
/// Represents a business rule violation within the domain.
/// Examples: "Cannot capture a payment that is not authorized",
///           "Order amount must be positive".
/// These map to HTTP 422 (Unprocessable Entity) or 400 (Bad Request).
/// They are EXPECTED errors and should never be retried — the caller must fix the request.
/// </summary>
public class DomainException : Exception
{
    public string Code { get; }

    public DomainException(string message, string code = "DOMAIN_ERROR")
        : base(message)
    {
        Code = code;
    }

    public DomainException(string message, string code, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }
}
