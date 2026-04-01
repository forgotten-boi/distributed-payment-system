using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Stripe;
using Payments.Domain.Gateways;

namespace Payments.Infrastructure.Gateways;

public class StripePaymentGateway : IPaymentGateway
{
    private readonly ILogger<StripePaymentGateway> _logger;
    private readonly string _webhookSecret;

    public StripePaymentGateway(ILogger<StripePaymentGateway> logger, IConfiguration config)
    {
        _logger = logger;
        // Initialize the Stripe API Key (usually starts with sk_test_ or sk_live_)
        StripeConfiguration.ApiKey = config["Stripe:SecretKey"];
        _webhookSecret = config["Stripe:WebhookSecret"] ?? string.Empty;
    }

    public async Task<GatewayAuthorizationResult> AuthorizeAsync(
        GatewayAuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        var options = new PaymentIntentCreateOptions
        {
            Amount = ToStripeAmount(request.Amount),
            Currency = request.Currency.ToLower(),
            CaptureMethod = "manual", // This ensures we only "Authorize"
            PaymentMethodTypes = new List<string> { "card" }
        };

        var requestOptions = new RequestOptions { IdempotencyKey = request.IdempotencyKey };
        var service = new PaymentIntentService();

        try
        {
            var intent = await service.CreateAsync(options, requestOptions, cancellationToken);
            return new GatewayAuthorizationResult(true, intent.Id, null, null);
        }
        catch (StripeException e)
        {
            _logger.LogError(e, "[Stripe] Auth Failed: {Message}", e.Message);
            return new GatewayAuthorizationResult(false, null, e.StripeError?.Code, e.Message);
        }
    }

    public async Task<GatewayCaptureResult> CaptureAsync(
        GatewayCaptureRequest request, CancellationToken cancellationToken = default)
    {
        var options = new PaymentIntentCaptureOptions
        {
            AmountToCapture = ToStripeAmount(request.Amount),
        };

        var service = new PaymentIntentService();

        try
        {
            await service.CaptureAsync(request.TransactionId, options, null, cancellationToken);
            return new GatewayCaptureResult(true, null, null);
        }
        catch (StripeException e)
        {
            _logger.LogError(e, "[Stripe] Capture Failed: {Message}", e.Message);
            return new GatewayCaptureResult(false, e.StripeError?.Code, e.Message);
        }
    }

    public async Task<GatewayRefundResult> RefundAsync(
        GatewayRefundRequest request, CancellationToken cancellationToken = default)
    {
        var options = new RefundCreateOptions
        {
            PaymentIntent = request.TransactionId,
            Amount = ToStripeAmount(request.Amount),
        };

        var service = new RefundService();

        try
        {
            var refund = await service.CreateAsync(options, null, cancellationToken);
            return new GatewayRefundResult(true, refund.Id, null, null);
        }
        catch (StripeException e)
        {
            _logger.LogError(e, "[Stripe] Refund Failed: {Message}", e.Message);
            return new GatewayRefundResult(false, null, e.StripeError?.Code, e.Message);
        }
    }

    public async Task<GatewayWebhookResult> HandleWebhookAsync(
        string payload, string signature, CancellationToken cancellationToken = default)
    {
        try
        {
            var stripeEvent = EventUtility.ConstructEvent(payload, signature, _webhookSecret);
            string transactionId = string.Empty;
            var metadata = new Dictionary<string, string>();

            if (stripeEvent.Data.Object is PaymentIntent intent)
            {
                transactionId = intent.Id;
                metadata = intent.Metadata ?? new Dictionary<string, string>();
            }

            return new GatewayWebhookResult(stripeEvent.Type, transactionId, metadata);
        }
        catch (StripeException e)
        {
            _logger.LogCritical(e, "[Stripe] Webhook signature validation failed");
            throw;
        }
    }

    private static long ToStripeAmount(decimal amount) => (long)(amount * 100);
}