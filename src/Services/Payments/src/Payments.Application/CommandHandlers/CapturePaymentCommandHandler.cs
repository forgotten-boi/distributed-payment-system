using BuildingBlocks.Contracts.Commands;
using BuildingBlocks.Persistence;
using MassTransit;
using Microsoft.Extensions.Logging;
using Payments.Domain.Gateways;
using Payments.Domain.Repositories;

namespace Payments.Application.CommandHandlers;

/// <summary>
/// Handles CapturePayment command — the point where money actually moves.
///
/// This is the most critical operation in the payment lifecycle:
///  - Authorization only reserves funds
///  - Capture actually transfers them
///  - Once captured, only refunds can reverse it
///
/// Flow:
///  1. Load payment, verify it's authorized
///  2. Call provider to capture the authorized amount
///  3. On success: mark captured → outbox → PaymentCaptured event
///     The Accounting service will react to create ledger entries
///  4. On failure: mark failed → outbox → PaymentFailed event
///     The Orders service will react to mark the order as failed
/// </summary>
public class CapturePaymentCommandHandler(
    IPaymentRepository paymentRepository,
    IPaymentGateway paymentGateway,
    IUnitOfWork unitOfWork,
    ILogger<CapturePaymentCommandHandler> logger) : IConsumer<CapturePaymentCommand>
{
    public async Task Consume(ConsumeContext<CapturePaymentCommand> context)
    {
        var command = context.Message;

        logger.LogInformation(
            "Processing CapturePayment for payment {PaymentId}, order {OrderId}",
            command.PaymentId, command.OrderId);

        var payment = await paymentRepository.GetByIdAsync(command.PaymentId, context.CancellationToken);
        if (payment is null)
        {
            logger.LogWarning("Payment {PaymentId} not found for capture", command.PaymentId);
            return;
        }

        try
        {
            var captureResult = await paymentGateway.CaptureAsync(
                new GatewayCaptureRequest(payment.ProviderTransactionId!, payment.Amount),
                context.CancellationToken);

            if (captureResult.Success)
            {
                payment.MarkCaptured();
                logger.LogInformation("Payment {PaymentId} captured successfully", payment.Id);
            }
            else
            {
                payment.MarkFailed(
                    captureResult.ErrorMessage ?? "Capture failed",
                    captureResult.ErrorCode ?? "CAPTURE_DECLINED");
                logger.LogWarning(
                    "Payment {PaymentId} capture failed: {Error}",
                    payment.Id, captureResult.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            payment.MarkFailed($"Capture error: {ex.Message}", "CAPTURE_ERROR");
            logger.LogError(ex, "Payment {PaymentId} capture failed with exception", payment.Id);
        }

        paymentRepository.Update(payment);
        await unitOfWork.SaveChangesAsync(context.CancellationToken);
    }
}
