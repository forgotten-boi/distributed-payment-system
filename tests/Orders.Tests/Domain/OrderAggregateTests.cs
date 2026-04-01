using FluentAssertions;
using Orders.Domain.Aggregates;
using Orders.Domain.ValueObjects;
using BuildingBlocks.Exceptions;
using Xunit;

namespace Orders.Tests.Domain;

/// <summary>
/// Tests for the Order aggregate root — validates business rules
/// and state transition enforcement.
/// </summary>
public class OrderAggregateTests
{
    private static Order CreateValidOrder(decimal amount = 250.00m) =>
        Order.Create(Guid.NewGuid(), amount, "USD", Guid.NewGuid().ToString());

    // ── Creation ──

    [Fact]
    public void Create_WithValidData_ShouldSetCorrectProperties()
    {
        var customerId = Guid.NewGuid();
        var key = Guid.NewGuid().ToString();

        var order = Order.Create(customerId, 100.50m, "eur", key);

        order.CustomerId.Should().Be(customerId);
        order.Amount.Should().Be(100.50m);
        order.Currency.Should().Be("EUR"); // uppercased
        order.IdempotencyKey.Should().Be(key);
        order.Status.Should().Be(OrderStatus.Created);
        order.PaymentId.Should().BeNull();
        order.FailureReason.Should().BeNull();
    }

    [Fact]
    public void Create_WithZeroAmount_ShouldThrowDomainException()
    {
        var act = () => Order.Create(Guid.NewGuid(), 0m, "USD", "key");
        act.Should().Throw<DomainException>().Where(e => e.Code == "INVALID_AMOUNT");
    }

    [Fact]
    public void Create_WithNegativeAmount_ShouldThrowDomainException()
    {
        var act = () => Order.Create(Guid.NewGuid(), -10m, "USD", "key");
        act.Should().Throw<DomainException>().Where(e => e.Code == "INVALID_AMOUNT");
    }

    [Fact]
    public void Create_WithEmptyCurrency_ShouldThrowDomainException()
    {
        var act = () => Order.Create(Guid.NewGuid(), 100m, "", "key");
        act.Should().Throw<DomainException>().Where(e => e.Code == "INVALID_CURRENCY");
    }

    [Fact]
    public void Create_WithEmptyIdempotencyKey_ShouldThrowDomainException()
    {
        var act = () => Order.Create(Guid.NewGuid(), 100m, "USD", "");
        act.Should().Throw<DomainException>().Where(e => e.Code == "MISSING_IDEMPOTENCY_KEY");
    }

    [Fact]
    public void Create_ShouldRaiseOrderCreatedDomainEvent()
    {
        var order = CreateValidOrder();
        order.DomainEvents.Should().HaveCount(1);
        order.DomainEvents[0].Should().BeOfType<Orders.Domain.Events.OrderCreatedDomainEvent>();
    }

    // ── State Transitions ──

    [Fact]
    public void StartPaymentAuthorization_FromCreated_ShouldTransitionToPaymentAuthorizing()
    {
        var order = CreateValidOrder();
        order.StartPaymentAuthorization();
        order.Status.Should().Be(OrderStatus.PaymentAuthorizing);
    }

    [Fact]
    public void StartPaymentAuthorization_FromNonCreated_ShouldThrow()
    {
        var order = CreateValidOrder();
        order.StartPaymentAuthorization();
        order.MarkAuthorized(Guid.NewGuid());

        var act = () => order.StartPaymentAuthorization();
        act.Should().Throw<DomainException>().Where(e => e.Code == "INVALID_STATE_TRANSITION");
    }

    [Fact]
    public void MarkAuthorized_FromPaymentAuthorizing_ShouldTransitionToAuthorized()
    {
        var order = CreateValidOrder();
        var paymentId = Guid.NewGuid();
        order.StartPaymentAuthorization();

        order.MarkAuthorized(paymentId);

        order.Status.Should().Be(OrderStatus.Authorized);
        order.PaymentId.Should().Be(paymentId);
    }

    [Fact]
    public void MarkAuthorized_FromWrongState_ShouldThrow()
    {
        var order = CreateValidOrder(); // Status = Created

        var act = () => order.MarkAuthorized(Guid.NewGuid());
        act.Should().Throw<DomainException>().Where(e => e.Code == "INVALID_STATE_TRANSITION");
    }

    [Fact]
    public void StartCapture_FromAuthorized_ShouldTransitionToCapturing()
    {
        var order = CreateValidOrder();
        order.StartPaymentAuthorization();
        order.MarkAuthorized(Guid.NewGuid());

        order.StartCapture();

        order.Status.Should().Be(OrderStatus.Capturing);
    }

    [Fact]
    public void StartCapture_FromNonAuthorized_ShouldThrow()
    {
        var order = CreateValidOrder();
        order.StartPaymentAuthorization();

        var act = () => order.StartCapture();
        act.Should().Throw<DomainException>().Where(e => e.Code == "INVALID_STATE_TRANSITION");
    }

    [Fact]
    public void MarkCaptured_FromCapturing_ShouldTransitionToCaptured()
    {
        var order = CreateValidOrder();
        order.StartPaymentAuthorization();
        order.MarkAuthorized(Guid.NewGuid());
        order.StartCapture();

        order.MarkCaptured();

        order.Status.Should().Be(OrderStatus.Captured);
    }

    [Fact]
    public void MarkFailed_FromAuthorizing_ShouldTransitionToFailed()
    {
        var order = CreateValidOrder();
        order.StartPaymentAuthorization();

        order.MarkFailed("Card declined");

        order.Status.Should().Be(OrderStatus.Failed);
        order.FailureReason.Should().Be("Card declined");
    }

    [Fact]
    public void MarkFailed_FromCaptured_ShouldThrow()
    {
        var order = CreateValidOrder();
        order.StartPaymentAuthorization();
        order.MarkAuthorized(Guid.NewGuid());
        order.StartCapture();
        order.MarkCaptured();

        var act = () => order.MarkFailed("test");
        act.Should().Throw<DomainException>().Where(e => e.Code == "INVALID_STATE_TRANSITION");
    }

    // ── Cancel ──

    [Fact]
    public void Cancel_FromCreated_ShouldTransitionToCancelled()
    {
        var order = CreateValidOrder();
        order.Cancel();
        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public void Cancel_FromAuthorized_ShouldTransitionToCancelled()
    {
        var order = CreateValidOrder();
        order.StartPaymentAuthorization();
        order.MarkAuthorized(Guid.NewGuid());

        order.Cancel();

        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public void Cancel_FromCaptured_ShouldThrow()
    {
        var order = CreateValidOrder();
        order.StartPaymentAuthorization();
        order.MarkAuthorized(Guid.NewGuid());
        order.StartCapture();
        order.MarkCaptured();

        var act = () => order.Cancel();
        act.Should().Throw<DomainException>().Where(e => e.Code == "CANNOT_CANCEL_CAPTURED");
    }

    [Fact]
    public void Cancel_FromAlreadyCancelled_ShouldThrow()
    {
        var order = CreateValidOrder();
        order.Cancel();

        var act = () => order.Cancel();
        act.Should().Throw<DomainException>().Where(e => e.Code == "ALREADY_TERMINAL");
    }

    [Fact]
    public void Cancel_FromFailed_ShouldThrow()
    {
        var order = CreateValidOrder();
        order.StartPaymentAuthorization();
        order.MarkFailed("declined");

        var act = () => order.Cancel();
        act.Should().Throw<DomainException>().Where(e => e.Code == "ALREADY_TERMINAL");
    }
}
