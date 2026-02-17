namespace Payments.Domain.Gateways;

/// <summary>
/// Payment provider abstraction — the system never exposes provider models to the domain.
///
/// Design rationale:
///  - Providers (Stripe, Adyen, etc.) have wildly different APIs
///  - This interface normalizes them into domain-relevant operations
///  - New providers can be added without touching business logic
///  - The adapter pattern isolates third-party SDK dependencies
///
/// Security: Webhook signature validation is the provider adapter's responsibility.
/// The domain trusts that HandleWebhook only passes validated data.
/// </summary>
public interface IPaymentGateway
{
    Task<GatewayAuthorizationResult> AuthorizeAsync(GatewayAuthorizationRequest request, CancellationToken cancellationToken = default);
    Task<GatewayCaptureResult> CaptureAsync(GatewayCaptureRequest request, CancellationToken cancellationToken = default);
    Task<GatewayRefundResult> RefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken = default);
    Task<GatewayWebhookResult> HandleWebhookAsync(string payload, string signature, CancellationToken cancellationToken = default);
}

// Request/Response models for the gateway abstraction
public record GatewayAuthorizationRequest(
    string IdempotencyKey,
    decimal Amount,
    string Currency);

public record GatewayAuthorizationResult(
    bool Success,
    string? TransactionId,
    string? ErrorCode,
    string? ErrorMessage);

public record GatewayCaptureRequest(
    string TransactionId,
    decimal Amount);

public record GatewayCaptureResult(
    bool Success,
    string? ErrorCode,
    string? ErrorMessage);

public record GatewayRefundRequest(
    string TransactionId,
    decimal Amount);

public record GatewayRefundResult(
    bool Success,
    string? RefundId,
    string? ErrorCode,
    string? ErrorMessage);

public record GatewayWebhookResult(
    string EventType,
    string TransactionId,
    Dictionary<string, string> Metadata);
