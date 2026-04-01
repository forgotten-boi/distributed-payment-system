using Adyen;
using Adyen.Model;
using Adyen.Model.Checkout;
using Adyen.Service;
using Adyen.Util;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Payments.Domain.Gateways;

namespace Payments.Infrastructure.Gateways;

public sealed class AdyenPaymentGateway : IPaymentGateway
{
    private readonly ILogger<AdyenPaymentGateway> _logger;
    private readonly string _merchantAccount;
    private readonly string _hmacKey;
    private readonly Checkout _checkout;

    public AdyenPaymentGateway(
        ILogger<AdyenPaymentGateway> logger,
        IConfiguration config)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _merchantAccount = config["Adyen:MerchantAccount"]
                           ?? throw new ArgumentNullException("Adyen:MerchantAccount");

        _hmacKey = config["Adyen:HmacKey"]
                   ?? throw new ArgumentNullException("Adyen:HmacKey");

        var client = new Client(config["Adyen:ApiKey"], Adyen.Model.Environment.Test);

        _checkout = new Checkout(client);
    }

    // ---------------- AUTHORIZATION ----------------
    public async Task<GatewayAuthorizationResult> AuthorizeAsync(
        GatewayAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        var paymentRequest = new PaymentsRequest
        {
            MerchantAccount = _merchantAccount,
            Reference = request.IdempotencyKey,
            Amount = new Amount
            {
                Currency = request.Currency,
                Value = (long)Math.Round(request.Amount * 100, MidpointRounding.AwayFromZero)
            },
            CaptureDelayHours = -1 // manual capture
        };

        try
        {
            var response = await _checkout.PaymentsAsync(paymentRequest, cancellationToken: cancellationToken);

            bool authorised = response.ResultCode == PaymentsResponse.ResultCodeEnum.Authorised;

            return new GatewayAuthorizationResult(
                Success: authorised,
                TransactionId: response.PspReference,
                ErrorCode: authorised ? null : response.RefusalReasonCode,
                ErrorMessage: authorised ? null : response.RefusalReason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Adyen] Authorization failed");
            return new GatewayAuthorizationResult(
                Success: false,
                TransactionId: null,
                ErrorCode: "ADYEN_AUTH_ERROR",
                ErrorMessage: ex.Message);
        }
    }

    // ---------------- CAPTURE ----------------
    public async Task<GatewayCaptureResult> CaptureAsync(
        GatewayCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        var captureRequest = new PaymentsCaptureRequest
        {
            MerchantAccount = _merchantAccount,
            Amount = new Amount
            {
                Currency = "GBP", // You can make this dynamic if needed
                Value = (long)Math.Round(request.Amount * 100, MidpointRounding.AwayFromZero)
            },
            Reference = Guid.NewGuid().ToString()
        };

        try
        {
            var response = await _checkout.CapturesAsync(request.TransactionId, captureRequest,
                cancellationToken: cancellationToken);

            return new GatewayCaptureResult(
                Success: true,
                ErrorCode: null,
                ErrorMessage: null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Adyen] Capture failed");
            return new GatewayCaptureResult(
                Success: false,
                ErrorCode: "ADYEN_CAPTURE_ERROR",
                ErrorMessage: ex.Message);
        }
    }

    // ---------------- REFUND ----------------
    public async Task<GatewayRefundResult> RefundAsync(
        GatewayRefundRequest request,
        CancellationToken cancellationToken = default)
    {
        var refundRequest = new PaymentsRefundRequest
        {
            MerchantAccount = _merchantAccount,
            Amount = new Amount
            {
                Currency = request.Currency,
                Value = (long)Math.Round(request.Amount * 100, MidpointRounding.AwayFromZero)
            }
        };

        try
        {
            var response = await _checkout.RefundsAsync(request.TransactionId, refundRequest,
                cancellationToken: cancellationToken);

            return new GatewayRefundResult(
                Success: true,
                RefundId: response.PspReference,
                ErrorCode: null,
                ErrorMessage: null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Adyen] Refund failed");
            return new GatewayRefundResult(
                Success: false,
                RefundId: null,
                ErrorCode: "ADYEN_REFUND_ERROR",
                ErrorMessage: ex.Message);
        }
    }

    // ---------------- WEBHOOK ----------------
    public Task<GatewayWebhookResult> HandleWebhookAsync(
        string payload,
        string signature,
        CancellationToken cancellationToken = default)
    {
        var hmacValidator = new HmacValidator();
        var notificationRequest = ResourceLoader.GetNotificationRequest(payload);

        var container = notificationRequest.NotificationItemContainers.FirstOrDefault();
        var item = container?.NotificationItem;

        if (item == null || !hmacValidator.IsValidHmac(item, _hmacKey))
        {
            _logger.LogError("[Adyen] Invalid webhook signature.");
            throw new UnauthorizedAccessException("Invalid Webhook Signature");
        }

        return Task.FromResult(new GatewayWebhookResult(
            EventType: item.EventCode,
            TransactionId: item.PspReference,
            Metadata: item.AdditionalData ?? new Dictionary<string, string>()));
    }
}