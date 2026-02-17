using BuildingBlocks.Exceptions;
using BuildingBlocks.Persistence;
using Orders.Domain.Events;
using Orders.Domain.ValueObjects;

namespace Orders.Domain.Aggregates;

/// <summary>
/// Order aggregate root — the consistency boundary for order state.
///
/// Business rules enforced here:
///  1. Amount must be positive
///  2. State transitions must follow the defined lifecycle
///  3. Cannot capture an unauthorized payment
///  4. Cannot cancel after capture (only refunds, which are a separate flow)
///  5. Every state change raises a domain event for the outbox
///
/// The Order does NOT know about payment providers or messaging.
/// It expresses business intent through domain events.
/// The application layer translates these into integration commands.
/// </summary>
public class Order : AggregateRoot
{
    public Guid CustomerId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public OrderStatus Status { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public Guid? PaymentId { get; private set; }
    public string? FailureReason { get; private set; }

    private Order() { } // EF Core

    public static Order Create(Guid customerId, decimal amount, string currency, string idempotencyKey)
    {
        if (amount <= 0)
            throw new DomainException("Order amount must be positive.", "INVALID_AMOUNT");

        if (string.IsNullOrWhiteSpace(currency))
            throw new DomainException("Currency is required.", "INVALID_CURRENCY");

        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new DomainException("Idempotency key is required.", "MISSING_IDEMPOTENCY_KEY");

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Amount = amount,
            Currency = currency.ToUpperInvariant(),
            Status = OrderStatus.Created,
            IdempotencyKey = idempotencyKey
        };

        order.RaiseDomainEvent(new OrderCreatedDomainEvent(
            order.Id, customerId, amount, order.Currency, idempotencyKey));

        return order;
    }

    public void StartPaymentAuthorization()
    {
        if (Status != OrderStatus.Created)
            throw new DomainException(
                $"Cannot authorize payment for order in status {Status}.",
                "INVALID_STATE_TRANSITION");

        Status = OrderStatus.PaymentAuthorizing;
        RaiseDomainEvent(new OrderPaymentAuthorizingDomainEvent(Id));
    }

    public void MarkAuthorized(Guid paymentId)
    {
        if (Status != OrderStatus.PaymentAuthorizing)
            throw new DomainException(
                $"Cannot mark order as authorized from status {Status}.",
                "INVALID_STATE_TRANSITION");

        Status = OrderStatus.Authorized;
        PaymentId = paymentId;
        RaiseDomainEvent(new OrderAuthorizedDomainEvent(Id, paymentId));
    }

    public void StartCapture()
    {
        if (Status != OrderStatus.Authorized)
            throw new DomainException(
                $"Cannot capture order in status {Status}. Must be Authorized first.",
                "INVALID_STATE_TRANSITION");

        Status = OrderStatus.Capturing;
    }

    public void MarkCaptured()
    {
        if (Status != OrderStatus.Capturing)
            throw new DomainException(
                $"Cannot mark order as captured from status {Status}.",
                "INVALID_STATE_TRANSITION");

        Status = OrderStatus.Captured;
        RaiseDomainEvent(new OrderCapturedDomainEvent(Id, PaymentId!.Value));
    }

    public void MarkFailed(string reason)
    {
        if (Status == OrderStatus.Captured)
            throw new DomainException(
                "Cannot fail a captured order. Use refund flow instead.",
                "INVALID_STATE_TRANSITION");

        Status = OrderStatus.Failed;
        FailureReason = reason;
        RaiseDomainEvent(new OrderFailedDomainEvent(Id, reason));
    }

    public void Cancel()
    {
        if (Status == OrderStatus.Captured)
            throw new DomainException(
                "Cannot cancel a captured order. Use refund flow instead.",
                "CANNOT_CANCEL_CAPTURED");

        if (Status is OrderStatus.Cancelled or OrderStatus.Failed)
            throw new DomainException(
                $"Order is already in terminal state {Status}.",
                "ALREADY_TERMINAL");

        Status = OrderStatus.Cancelled;
        RaiseDomainEvent(new OrderCancelledDomainEvent(Id));
    }
}
