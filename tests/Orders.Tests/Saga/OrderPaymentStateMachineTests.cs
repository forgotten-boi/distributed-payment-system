using BuildingBlocks.Contracts.Commands;
using BuildingBlocks.Contracts.Events;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Orders.Application.Saga;
using Orders.Domain.Events;
using Xunit;

namespace Orders.Tests.Saga;

/// <summary>
/// Tests for the OrderPaymentStateMachine saga.
/// Uses MassTransit's built-in test harness to simulate
/// the full saga lifecycle without requiring RabbitMQ.
/// </summary>
public class OrderPaymentStateMachineTests : IAsyncLifetime
{
    private ServiceProvider _provider = null!;
    private ITestHarness _harness = null!;
    private ISagaStateMachineTestHarness<OrderPaymentStateMachine, OrderPaymentState> _sagaHarness = null!;

    public async ValueTask InitializeAsync()
    {
        _provider = new ServiceCollection()
            .AddSingleton(Substitute.For<ILogger<OrderPaymentStateMachine>>())
            .AddMassTransitTestHarness(x =>
            {
                x.AddSagaStateMachine<OrderPaymentStateMachine, OrderPaymentState>()
                    .InMemoryRepository();
            })
            .BuildServiceProvider(true);

        _harness = _provider.GetRequiredService<ITestHarness>();
        await _harness.Start();

        _sagaHarness = _harness.GetSagaStateMachineHarness<OrderPaymentStateMachine, OrderPaymentState>();
    }

    public async ValueTask DisposeAsync()
    {
        await _harness.Stop();
        await _provider.DisposeAsync();
    }

    // ── Helper ──

    private static OrderCreatedDomainEvent CreateOrderEvent(Guid? orderId = null) => new(
        OrderId: orderId ?? Guid.NewGuid(),
        CustomerId: Guid.NewGuid(),
        Amount: 250.00m,
        Currency: "USD",
        IdempotencyKey: Guid.NewGuid().ToString());

    // ═══════════════════════════════════════════════════
    // Happy Path
    // ═══════════════════════════════════════════════════

    [Fact]
    public async Task OrderCreated_ShouldTransitionToAuthorizing_AndSendAuthorizeCommand()
    {
        var orderId = Guid.NewGuid();
        var orderEvent = CreateOrderEvent(orderId);

        await _harness.Bus.Publish(orderEvent);

        // Saga should exist and be in Authorizing state
        var exists = await _sagaHarness.Exists(orderId, s => s.Authorizing);
        exists.HasValue.Should().BeTrue();

        // AuthorizePaymentCommand should have been sent
        (await _harness.Sent.Any<AuthorizePaymentCommand>(x =>
            x.Context.Message.OrderId == orderId)).Should().BeTrue();
    }

    [Fact]
    public async Task PaymentAuthorized_ShouldTransitionToAuthorized()
    {
        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        await _harness.Bus.Publish(CreateOrderEvent(orderId));
        await _sagaHarness.Exists(orderId, s => s.Authorizing);

        await _harness.Bus.Publish(new PaymentAuthorizedEvent(
            PaymentId: paymentId,
            OrderId: orderId,
            Amount: 250.00m,
            Currency: "USD",
            ProviderTransactionId: "txn_123",
            CorrelationId: orderId.ToString(),
            CausationId: orderId.ToString(),
            OccurredOn: DateTime.UtcNow));

        var exists = await _sagaHarness.Exists(orderId, s => s.Authorized);
        exists.HasValue.Should().BeTrue();

        // Verify saga state has payment info
        var instance = _sagaHarness.Sagas.Contains(orderId);
        instance.Should().NotBeNull();
        instance!.PaymentId.Should().Be(paymentId);
        instance.ProviderTransactionId.Should().Be("txn_123");
    }

    [Fact]
    public async Task ConfirmRequested_WhenAuthorized_ShouldTransitionToCapturing_AndSendCaptureCommand()
    {
        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        // Move to Authorized
        await _harness.Bus.Publish(CreateOrderEvent(orderId));
        await _sagaHarness.Exists(orderId, s => s.Authorizing);

        await _harness.Bus.Publish(new PaymentAuthorizedEvent(
            paymentId, orderId, 250.00m, "USD", "txn_123",
            orderId.ToString(), orderId.ToString(), DateTime.UtcNow));
        await _sagaHarness.Exists(orderId, s => s.Authorized);

        // Confirm
        await _harness.Bus.Publish(new ConfirmOrderRequested(
            orderId, orderId.ToString(), orderId.ToString()));

        var exists = await _sagaHarness.Exists(orderId, s => s.Capturing);
        exists.HasValue.Should().BeTrue();

        (await _harness.Sent.Any<CapturePaymentCommand>(x =>
            x.Context.Message.OrderId == orderId &&
            x.Context.Message.PaymentId == paymentId)).Should().BeTrue();
    }

    [Fact]
    public async Task PaymentCaptured_WhenCapturing_ShouldTransitionToCaptured()
    {
        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        // Move to Capturing
        await _harness.Bus.Publish(CreateOrderEvent(orderId));
        await _sagaHarness.Exists(orderId, s => s.Authorizing);

        await _harness.Bus.Publish(new PaymentAuthorizedEvent(
            paymentId, orderId, 250.00m, "USD", "txn_123",
            orderId.ToString(), orderId.ToString(), DateTime.UtcNow));
        await _sagaHarness.Exists(orderId, s => s.Authorized);

        await _harness.Bus.Publish(new ConfirmOrderRequested(
            orderId, orderId.ToString(), orderId.ToString()));
        await _sagaHarness.Exists(orderId, s => s.Capturing);

        // Capture
        await _harness.Bus.Publish(new PaymentCapturedEvent(
            paymentId, orderId, 250.00m, "USD", "txn_123",
            orderId.ToString(), orderId.ToString(), DateTime.UtcNow));

        var exists = await _sagaHarness.Exists(orderId, s => s.Captured);
        exists.HasValue.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════
    // Failure Paths
    // ═══════════════════════════════════════════════════

    [Fact]
    public async Task PaymentFailed_WhenAuthorizing_ShouldTransitionToFailed()
    {
        var orderId = Guid.NewGuid();

        await _harness.Bus.Publish(CreateOrderEvent(orderId));
        await _sagaHarness.Exists(orderId, s => s.Authorizing);

        await _harness.Bus.Publish(new PaymentFailedEvent(
            PaymentId: Guid.NewGuid(),
            OrderId: orderId,
            Reason: "Card declined",
            FailureCode: "DECLINED",
            CorrelationId: orderId.ToString(),
            CausationId: orderId.ToString(),
            OccurredOn: DateTime.UtcNow));

        var exists = await _sagaHarness.Exists(orderId, s => s.Failed);
        exists.HasValue.Should().BeTrue();

        var instance = _sagaHarness.Sagas.Contains(orderId);
        instance!.FailureReason.Should().Be("Card declined");
    }

    [Fact]
    public async Task PaymentFailed_WhenCapturing_ShouldCompensate_AndTransitionToFailed()
    {
        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        // Move to Capturing
        await _harness.Bus.Publish(CreateOrderEvent(orderId));
        await _sagaHarness.Exists(orderId, s => s.Authorizing);

        await _harness.Bus.Publish(new PaymentAuthorizedEvent(
            paymentId, orderId, 250.00m, "USD", "txn_123",
            orderId.ToString(), orderId.ToString(), DateTime.UtcNow));
        await _sagaHarness.Exists(orderId, s => s.Authorized);

        await _harness.Bus.Publish(new ConfirmOrderRequested(
            orderId, orderId.ToString(), orderId.ToString()));
        await _sagaHarness.Exists(orderId, s => s.Capturing);

        // Capture fails
        await _harness.Bus.Publish(new PaymentFailedEvent(
            paymentId, orderId, "Insufficient funds",
            "INSUFFICIENT_FUNDS", orderId.ToString(), orderId.ToString(), DateTime.UtcNow));

        var exists = await _sagaHarness.Exists(orderId, s => s.Failed);
        exists.HasValue.Should().BeTrue();

        // Compensation: CancelPaymentCommand should have been sent to release hold
        (await _harness.Sent.Any<CancelPaymentCommand>(x =>
            x.Context.Message.OrderId == orderId &&
            x.Context.Message.PaymentId == paymentId)).Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════
    // Cancellation
    // ═══════════════════════════════════════════════════

    [Fact]
    public async Task CancelRequested_WhenAuthorized_ShouldSendCancelPayment_AndTransitionToCancelled()
    {
        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        // Move to Authorized
        await _harness.Bus.Publish(CreateOrderEvent(orderId));
        await _sagaHarness.Exists(orderId, s => s.Authorizing);

        await _harness.Bus.Publish(new PaymentAuthorizedEvent(
            paymentId, orderId, 250.00m, "USD", "txn_123",
            orderId.ToString(), orderId.ToString(), DateTime.UtcNow));
        await _sagaHarness.Exists(orderId, s => s.Authorized);

        // Cancel
        await _harness.Bus.Publish(new CancelOrderRequested(
            orderId, orderId.ToString(), orderId.ToString()));

        var exists = await _sagaHarness.Exists(orderId, s => s.Cancelled);
        exists.HasValue.Should().BeTrue();

        (await _harness.Sent.Any<CancelPaymentCommand>(x =>
            x.Context.Message.OrderId == orderId &&
            x.Context.Message.PaymentId == paymentId)).Should().BeTrue();
    }

    [Fact]
    public async Task CancelRequested_WhenAuthorizing_ShouldTransitionToCancelled_WithoutSendingCancelPayment()
    {
        var orderId = Guid.NewGuid();

        await _harness.Bus.Publish(CreateOrderEvent(orderId));
        await _sagaHarness.Exists(orderId, s => s.Authorizing);

        // Cancel while still authorizing (no payment yet)
        await _harness.Bus.Publish(new CancelOrderRequested(
            orderId, orderId.ToString(), orderId.ToString()));

        var exists = await _sagaHarness.Exists(orderId, s => s.Cancelled);
        exists.HasValue.Should().BeTrue();

        // No CancelPaymentCommand should be sent — there's no payment to cancel
        (await _harness.Sent.Any<CancelPaymentCommand>(x =>
            x.Context.Message.OrderId == orderId)).Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════
    // Terminal state ignores
    // ═══════════════════════════════════════════════════

    [Fact]
    public async Task CapturedState_ShouldIgnoreFurtherMessages()
    {
        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        // Drive to Captured
        await _harness.Bus.Publish(CreateOrderEvent(orderId));
        await _sagaHarness.Exists(orderId, s => s.Authorizing);
        await _harness.Bus.Publish(new PaymentAuthorizedEvent(
            paymentId, orderId, 250.00m, "USD", "txn_123",
            orderId.ToString(), orderId.ToString(), DateTime.UtcNow));
        await _sagaHarness.Exists(orderId, s => s.Authorized);
        await _harness.Bus.Publish(new ConfirmOrderRequested(
            orderId, orderId.ToString(), orderId.ToString()));
        await _sagaHarness.Exists(orderId, s => s.Capturing);
        await _harness.Bus.Publish(new PaymentCapturedEvent(
            paymentId, orderId, 250.00m, "USD", "txn_123",
            orderId.ToString(), orderId.ToString(), DateTime.UtcNow));
        await _sagaHarness.Exists(orderId, s => s.Captured);

        // Try to cancel — should be ignored
        await _harness.Bus.Publish(new CancelOrderRequested(
            orderId, orderId.ToString(), orderId.ToString()));

        // Should still be in Captured state
        var instance = _sagaHarness.Sagas.Contains(orderId);
        instance.Should().NotBeNull();
        instance!.CurrentState.Should().Be("Captured");
    }

    // ═══════════════════════════════════════════════════
    // OrderSagaStateChanged events
    // ═══════════════════════════════════════════════════

    [Fact]
    public async Task SagaTransitions_ShouldPublishOrderSagaStateChanged()
    {
        var orderId = Guid.NewGuid();

        await _harness.Bus.Publish(CreateOrderEvent(orderId));
        await _sagaHarness.Exists(orderId, s => s.Authorizing);

        await _harness.Bus.Publish(new PaymentAuthorizedEvent(
            Guid.NewGuid(), orderId, 250.00m, "USD", "txn_123",
            orderId.ToString(), orderId.ToString(), DateTime.UtcNow));
        await _sagaHarness.Exists(orderId, s => s.Authorized);

        // Should have published an OrderSagaStateChanged event when transitioning to Authorized
        (await _harness.Published.Any<OrderSagaStateChanged>(x =>
            x.Context.Message.OrderId == orderId &&
            x.Context.Message.CurrentState == "Authorized")).Should().BeTrue();
    }
}
