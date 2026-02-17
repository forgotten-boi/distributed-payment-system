using BuildingBlocks.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Observability;

/// <summary>
/// Global exception handler that converts all exceptions into RFC 7807 ProblemDetails.
/// This ensures no unhandled exception leaks internal details to clients.
///
/// Mapping strategy:
///  - DomainException → 422 Unprocessable Entity (business rule violation)
///  - IntegrationException → 502 Bad Gateway (external system failure)
///  - TransientException → 503 Service Unavailable with Retry-After
///  - Everything else → 500 Internal Server Error
///
/// The error code from the exception is included in the ProblemDetails extensions
/// so clients can programmatically handle specific error types.
/// </summary>
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problemDetails = exception switch
        {
            DomainException domainEx => new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "Domain Rule Violation",
                Detail = domainEx.Message,
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                Extensions = { ["code"] = domainEx.Code }
            },
            IntegrationException integrationEx => new ProblemDetails
            {
                Status = StatusCodes.Status502BadGateway,
                Title = "Integration Failure",
                Detail = integrationEx.Message,
                Type = "https://tools.ietf.org/html/rfc7231#section-6.6.3",
                Extensions =
                {
                    ["code"] = integrationEx.Code,
                    ["provider"] = integrationEx.ProviderName ?? "unknown"
                }
            },
            TransientException transientEx => new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Temporary Failure",
                Detail = transientEx.Message,
                Type = "https://tools.ietf.org/html/rfc7231#section-6.6.4",
                Extensions = { ["code"] = transientEx.Code }
            },
            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred. Please try again later.",
                Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1"
            }
        };

        logger.LogError(exception,
            "Request failed: {Title} ({Code}) — {Detail}",
            problemDetails.Title,
            problemDetails.Extensions.TryGetValue("code", out var code) ? code : "UNKNOWN",
            exception.Message);

        if (exception is TransientException { RetryAfter: not null } transient)
        {
            httpContext.Response.Headers.RetryAfter = transient.RetryAfter.Value.TotalSeconds.ToString("F0");
        }

        httpContext.Response.StatusCode = problemDetails.Status ?? 500;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }
}
