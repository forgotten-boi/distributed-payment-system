using Microsoft.Extensions.Logging;
using Payments.Domain.Gateways;

namespace Payments.Infrastructure.Gateways;
public class StripePaymentGateway : IPaymentGateway
{
    private readonly ILogger<StripePaymentGateway> logger;

    public StripePaymentGateway(ILogger<StripePaymentGateway> logger)
    {
        this.logger = logger;
    }

    public async Task<GatewayAuthorizationResult> AuthorizeAsync(
        GatewayAuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "[Stripe] Authorizing {Amount} {Currency} with idempotency key {IdempotencyKey}",
            request.Amount, request.Currency, request.IdempotencyKey);

        // Simulate network delay
        await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(100, 300)), cancellationToken);

        // Simulate random failure
        if (Random.Shared.Next(100) < 10) // 10% failure rate
        {
            logger.LogWarning("[Stripe] Random authorization failure (simulated)");
            return new GatewayAuthorizationResult(
                Success: false,
                TransactionId: null,
                ErrorCode: "AUTH_FAILED",
                ErrorMessage: "Simulated random authorization failure");
        }
        
        // Simulate successful authorization
        var transactionId = Guid.NewGuid().ToString();
        logger.LogInformation("[Stripe] Authorization successful, transaction ID: {TransactionId}", transactionId);
        return new GatewayAuthorizationResult(
            Success: true,
            TransactionId: transactionId,
            ErrorCode: null,
            ErrorMessage: null);
    }

    public Task<GatewayCaptureResult> CaptureAsync(
        GatewayCaptureRequest request, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "[Stripe] Capturing {Amount} for transaction {TransactionId}",
            request.Amount, request.TransactionId);

        // Simulate capture logic here (omitted for brevity)
        return Task.FromResult(new GatewayCaptureResult(
            Success: true,
            ErrorCode: null,
            ErrorMessage: null));
    }

    public Task<GatewayRefundResult> RefundAsync(
        GatewayRefundRequest request, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "[Stripe] Refunding {Amount} for transaction {TransactionId}",
            request.Amount, request.TransactionId);

        // Simulate refund logic here (omitted for brevity)
        return Task.FromResult(new GatewayRefundResult(
            Success: true,
            RefundId: Guid.NewGuid().ToString(),
            ErrorCode: null,
            ErrorMessage: null));
    }

    public Task<GatewayWebhookResult> HandleWebhookAsync(
        string payload, string signature, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[Stripe] Handling webhook with payload: {Payload}", payload);

        // Simulate webhook handling logic here (omitted for brevity)
        return Task.FromResult(new GatewayWebhookResult(
            EventType: "payment_intent.succeeded",
            TransactionId: Guid.NewGuid().ToString(),
            Metadata: new Dictionary<string, string> { { "example_key", "example_value" } }));
    }
}