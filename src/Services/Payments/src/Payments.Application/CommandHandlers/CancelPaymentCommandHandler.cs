using BuildingBlocks.Contracts.Commands;
using BuildingBlocks.Persistence;
using MassTransit;
using Microsoft.Extensions.Logging;
using Payments.Domain.Repositories;

namespace Payments.Application.CommandHandlers;

/// <summary>
/// Handles CancelPayment command — voids an authorized payment.
/// This releases the hold on the customer's funds.
/// Can only cancel payments that are in Authorized state.
/// </summary>
public class CancelPaymentCommandHandler(
    IPaymentRepository paymentRepository,
    IUnitOfWork unitOfWork,
    ILogger<CancelPaymentCommandHandler> logger) : IConsumer<CancelPaymentCommand>
{
    public async Task Consume(ConsumeContext<CancelPaymentCommand> context)
    {
        var command = context.Message;

        logger.LogInformation("Processing CancelPayment for payment {PaymentId}", command.PaymentId);

        var payment = await paymentRepository.GetByIdAsync(command.PaymentId, context.CancellationToken);
        if (payment is null)
        {
            logger.LogWarning("Payment {PaymentId} not found for cancellation", command.PaymentId);
            return;
        }

        payment.Cancel();
        paymentRepository.Update(payment);
        await unitOfWork.SaveChangesAsync(context.CancellationToken);

        logger.LogInformation("Payment {PaymentId} cancelled", payment.Id);
    }
}
