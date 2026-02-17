using Microsoft.Extensions.Logging;
using Payments.Domain.Gateways;

namespace Payments.Infrastructure.Gateways;

/// <summary>
/// Simulated payment provider for development and testing.
///
/// This adapter implements IPaymentGateway without connecting to a real provider,
/// allowing the full payment lifecycle to be exercised end-to-end.
///
/// Behavior:
///  - Amounts ending in .99 are declined (simulates provider decline)
///  - Amounts over 10,000 trigger a timeout simulation
///  - All other amounts succeed with a simulated transaction ID
///  - Random 5% failure rate on captures (simulates real-world flakiness)
///
/// In production, this would be replaced with a real provider adapter
/// (e.g., StripePaymentGateway, AdyenPaymentGateway) without touching
/// any business logic — this is the adapter pattern in action.
/// </summary>
public class SimulatedPaymentGateway(ILogger<SimulatedPaymentGateway> logger) : IPaymentGateway
{
    public async Task<GatewayAuthorizationResult> AuthorizeAsync(
        GatewayAuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "[SimulatedProvider] Authorizing {Amount} {Currency}, key={Key}",
            request.Amount, request.Currency, request.IdempotencyKey);

        // Simulate network latency
        await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(50, 300)), cancellationToken);

        // Simulate decline for amounts ending in .99
        if (request.Amount % 1 == 0.99m)
        {
            logger.LogWarning("[SimulatedProvider] Declining amount {Amount} (simulated decline)", request.Amount);
            return new GatewayAuthorizationResult(
                Success: false,
                TransactionId: null,
                ErrorCode: "INSUFFICIENT_FUNDS",
                ErrorMessage: "Simulated decline: insufficient funds");
        }

        // Simulate timeout for large amounts
        if (request.Amount > 10_000)
        {
            logger.LogWarning("[SimulatedProvider] Timeout for amount {Amount} (simulated timeout)", request.Amount);
            throw new TimeoutException("Simulated provider timeout for large amount");
        }

        var transactionId = $"sim_auth_{Guid.NewGuid():N}";
        logger.LogInformation("[SimulatedProvider] Authorized, txn={TransactionId}", transactionId);

        return new GatewayAuthorizationResult(
            Success: true,
            TransactionId: transactionId,
            ErrorCode: null,
            ErrorMessage: null);
    }

    public async Task<GatewayCaptureResult> CaptureAsync(
        GatewayCaptureRequest request, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "[SimulatedProvider] Capturing {Amount} for txn={TransactionId}",
            request.Amount, request.TransactionId);

        await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(50, 200)), cancellationToken);

        // 5% random capture failure
        if (Random.Shared.Next(100) < 5)
        {
            logger.LogWarning("[SimulatedProvider] Random capture failure (simulated)");
            return new GatewayCaptureResult(
                Success: false,
                ErrorCode: "CAPTURE_FAILED",
                ErrorMessage: "Simulated random capture failure");
        }

        logger.LogInformation("[SimulatedProvider] Capture successful for txn={TransactionId}", request.TransactionId);
        return new GatewayCaptureResult(Success: true, ErrorCode: null, ErrorMessage: null);
    }

    public async Task<GatewayRefundResult> RefundAsync(
        GatewayRefundRequest request, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "[SimulatedProvider] Refunding {Amount} for txn={TransactionId}",
            request.Amount, request.TransactionId);

        await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(50, 200)), cancellationToken);

        var refundId = $"sim_ref_{Guid.NewGuid():N}";
        return new GatewayRefundResult(Success: true, RefundId: refundId, ErrorCode: null, ErrorMessage: null);
    }

    public Task<GatewayWebhookResult> HandleWebhookAsync(
        string payload, string signature, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[SimulatedProvider] Processing webhook");

        return Task.FromResult(new GatewayWebhookResult(
            EventType: "payment.settled",
            TransactionId: "simulated",
            Metadata: new Dictionary<string, string> { ["source"] = "simulator" }));
    }
}
