using BuildingBlocks.Exceptions;
using BuildingBlocks.Persistence;
using Payments.Domain.Events;
using Payments.Domain.ValueObjects;

namespace Payments.Domain.Aggregates;

/// <summary>
/// Payment aggregate root — encapsulates the payment processing lifecycle.
///
/// Key design decisions:
///  1. The Payment never talks to the provider directly — it only records state changes.
///     The application layer coordinates with IPaymentGateway and then calls domain methods.
///  2. ProviderTransactionId is stored for reconciliation but never exposed to other services.
///  3. Domain events are raised for state changes; they become integration events via outbox.
///  4. Financial amounts use decimal with explicit precision to avoid floating-point errors.
///
/// Security consideration: No card data is stored. The provider handles tokenization.
/// We only store the provider's transaction reference for reconciliation.
/// </summary>
public class Payment : AggregateRoot
{
    public Guid OrderId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public PaymentStatus Status { get; private set; }
    public string? ProviderTransactionId { get; private set; }
    public string? FailureReason { get; private set; }
    public string? FailureCode { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;

    private Payment() { } // EF Core

    public static Payment Create(Guid orderId, decimal amount, string currency, string idempotencyKey)
    {
        if (amount <= 0)
            throw new DomainException("Payment amount must be positive.", "INVALID_AMOUNT");

        return new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            Amount = amount,
            Currency = currency.ToUpperInvariant(),
            Status = PaymentStatus.Pending,
            IdempotencyKey = idempotencyKey
        };
    }

    public void MarkAuthorized(string providerTransactionId)
    {
        if (Status != PaymentStatus.Pending)
            throw new DomainException(
                $"Cannot authorize payment in status {Status}.",
                "INVALID_STATE_TRANSITION");

        Status = PaymentStatus.Authorized;
        ProviderTransactionId = providerTransactionId;

        RaiseDomainEvent(new PaymentAuthorizedDomainEvent(
            Id, OrderId, Amount, Currency, providerTransactionId));
    }

    public void MarkCaptured()
    {
        if (Status != PaymentStatus.Authorized)
            throw new DomainException(
                $"Cannot capture payment in status {Status}. Must be Authorized.",
                "INVALID_STATE_TRANSITION");

        Status = PaymentStatus.Captured;

        RaiseDomainEvent(new PaymentCapturedDomainEvent(
            Id, OrderId, Amount, Currency, ProviderTransactionId!));
    }

    public void MarkFailed(string reason, string failureCode)
    {
        if (Status is PaymentStatus.Captured or PaymentStatus.Settled)
            throw new DomainException(
                $"Cannot fail payment in terminal status {Status}.",
                "INVALID_STATE_TRANSITION");

        Status = PaymentStatus.Failed;
        FailureReason = reason;
        FailureCode = failureCode;

        RaiseDomainEvent(new PaymentFailedDomainEvent(Id, OrderId, reason, failureCode));
    }

    public void Cancel()
    {
        if (Status != PaymentStatus.Authorized)
            throw new DomainException(
                $"Can only cancel authorized payments. Current: {Status}.",
                "INVALID_STATE_TRANSITION");

        Status = PaymentStatus.Cancelled;
        RaiseDomainEvent(new PaymentCancelledDomainEvent(Id, OrderId));
    }

    public void MarkSettled()
    {
        if (Status != PaymentStatus.Captured)
            throw new DomainException(
                $"Can only settle captured payments. Current: {Status}.",
                "INVALID_STATE_TRANSITION");

        Status = PaymentStatus.Settled;
    }
}
