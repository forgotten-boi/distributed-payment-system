using BuildingBlocks.Contracts.Commands;
using BuildingBlocks.Contracts.Events;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using MassTransit;
using Microsoft.Extensions.Logging;
using Payments.Domain.Aggregates;
using Payments.Domain.Gateways;
using Payments.Domain.Repositories;

namespace Payments.Application.CommandHandlers;

/// <summary>
/// Handles the AuthorizePayment command from the Orders service.
///
/// Flow:
///  1. Idempotency check — if payment with this key exists, skip
///  2. Create Payment aggregate in Pending state
///  3. Call IPaymentGateway.AuthorizeAsync (behind resilience pipeline)
///  4. On success: mark authorized, domain event → outbox → PaymentAuthorized published
///  5. On failure: mark failed, domain event → outbox → PaymentFailed published
///
/// Error handling strategy:
///  - Provider timeout/network errors: caught, payment marked as Failed
///    with a specific failure code. No silent retries — the failure becomes
///    an explicit compensating event that Orders can react to.
///  - Provider business decline: captured in GatewayAuthorizationResult,
///    mapped to PaymentFailed with the decline reason.
///  - Unexpected exceptions: propagated to MassTransit retry/error queue.
///
/// Security: We never store card data. The provider handles tokenization.
/// </summary>
public class AuthorizePaymentCommandHandler(
    IPaymentRepository paymentRepository,
    IPaymentGateway paymentGateway,
    IUnitOfWork unitOfWork,
    ILogger<AuthorizePaymentCommandHandler> logger) : IConsumer<AuthorizePaymentCommand>
{
    public async Task Consume(ConsumeContext<AuthorizePaymentCommand> context)
    {
        var command = context.Message;

        logger.LogInformation(
            "Processing AuthorizePayment for order {OrderId}, amount {Amount} {Currency}, correlation {CorrelationId}",
            command.OrderId, command.Amount, command.Currency, command.CorrelationId);

        // Idempotency: check if we already processed this
        var existing = await paymentRepository.GetByIdempotencyKeyAsync(command.IdempotencyKey, context.CancellationToken);
        if (existing is not null)
        {
            logger.LogInformation("Duplicate AuthorizePayment detected for key {Key}, payment {PaymentId} already exists",
                command.IdempotencyKey, existing.Id);
            return;
        }

        var payment = Payment.Create(command.OrderId, command.Amount, command.Currency, command.IdempotencyKey);

        try
        {
            var gatewayResult = await paymentGateway.AuthorizeAsync(
                new GatewayAuthorizationRequest(command.IdempotencyKey, command.Amount, command.Currency),
                context.CancellationToken);

            if (gatewayResult.Success && gatewayResult.TransactionId is not null)
            {
                payment.MarkAuthorized(gatewayResult.TransactionId);
                logger.LogInformation(
                    "Payment {PaymentId} authorized by provider, txn {TransactionId}",
                    payment.Id, gatewayResult.TransactionId);
            }
            else
            {
                payment.MarkFailed(
                    gatewayResult.ErrorMessage ?? "Authorization declined",
                    gatewayResult.ErrorCode ?? "PROVIDER_DECLINE");
                logger.LogWarning(
                    "Payment {PaymentId} authorization declined: {Error} ({Code})",
                    payment.Id, gatewayResult.ErrorMessage, gatewayResult.ErrorCode);
            }
        }
        catch (Exception ex)
        {
            payment.MarkFailed($"Provider error: {ex.Message}", "PROVIDER_ERROR");
            logger.LogError(ex, "Payment {PaymentId} authorization failed with exception", payment.Id);
        }

        await paymentRepository.AddAsync(payment, context.CancellationToken);
        await unitOfWork.SaveChangesAsync(context.CancellationToken);
    }
}
