using BuildingBlocks.Contracts.Commands;
using BuildingBlocks.Contracts.Events;
using MassTransit;
using Microsoft.Extensions.Logging;
using Orders.Domain.Events;

namespace Orders.Application.Saga;

/// <summary>
/// MassTransit Saga State Machine that orchestrates the full payment lifecycle:
///
///   Created ──► Authorizing ──► Authorized ──► Capturing ──► Captured (final)
///                   │                │              │
///                   ▼                ▼              ▼
///                Failed          Cancelled       Failed → (compensate: cancel auth hold)
///
/// This replaces the previous choreography-based event handlers in Orders.Application.
///
/// The saga receives events from the Payments service and sends commands back,
/// acting as the central orchestrator for the order-payment lifecycle.
/// The Payments and Accounting services remain unchanged — they are participants.
///
/// Compensation strategy:
///   - Authorization failure: terminal — no funds were reserved
///   - Capture failure: send CancelPaymentCommand to release the authorized hold
///   - Timeout on authorization: transition to Failed
/// </summary>
public class OrderPaymentStateMachine : MassTransitStateMachine<OrderPaymentState>
{
    private readonly ILogger<OrderPaymentStateMachine> _logger;

    // ── States ──
    public State Authorizing { get; private set; } = null!;
    public State Authorized { get; private set; } = null!;
    public State Capturing { get; private set; } = null!;
    public State Captured { get; private set; } = null!;
    public State Failed { get; private set; } = null!;
    public State Cancelled { get; private set; } = null!;

    // ── Events (message triggers) ──
    public Event<OrderCreatedDomainEvent> OrderCreated { get; private set; } = null!;
    public Event<ConfirmOrderRequested> ConfirmRequested { get; private set; } = null!;
    public Event<CancelOrderRequested> CancelRequested { get; private set; } = null!;
    public Event<PaymentAuthorizedEvent> PaymentAuthorized { get; private set; } = null!;
    public Event<PaymentCapturedEvent> PaymentCaptured { get; private set; } = null!;
    public Event<PaymentFailedEvent> PaymentFailed { get; private set; } = null!;

    // ── Schedule (authorization timeout) ──
    public Schedule<OrderPaymentState, AuthorizationTimeoutExpired> AuthorizationTimeout { get; private set; } = null!;

    public OrderPaymentStateMachine(ILogger<OrderPaymentStateMachine> logger)
    {
        _logger = logger;

        // Tell MassTransit which property holds the serialized state name
        InstanceState(x => x.CurrentState);

        // ── Event Correlation ──
        // Every message is correlated to a saga instance by OrderId
        Event(() => OrderCreated, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => ConfirmRequested, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => CancelRequested, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => PaymentAuthorized, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => PaymentCaptured, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => PaymentFailed, x => x.CorrelateById(m => m.Message.OrderId));

        // ── Authorization Timeout Schedule ──
        Schedule(() => AuthorizationTimeout, instance => instance.AuthorizationTimeoutTokenId, s =>
        {
            s.Delay = TimeSpan.FromMinutes(5);
            s.Received = r => r.CorrelateById(m => m.Message.OrderId);
        });

        // ═══════════════════════════════════════════════════════════
        // State Machine Definition
        // ═══════════════════════════════════════════════════════════

        // ── Initial: Order Created → Send AuthorizePayment → Authorizing ──
        Initially(
            When(OrderCreated)
                .Then(context =>
                {
                    var msg = context.Message;
                    context.Saga.CustomerId = msg.CustomerId;
                    context.Saga.Amount = msg.Amount;
                    context.Saga.Currency = msg.Currency;
                    context.Saga.IdempotencyKey = msg.IdempotencyKey;
                    context.Saga.CreatedAt = DateTime.UtcNow;
                    context.Saga.UpdatedAt = DateTime.UtcNow;

                    _logger.LogInformation(
                        "Saga started for Order {OrderId}: {Amount} {Currency}",
                        msg.OrderId, msg.Amount, msg.Currency);
                })
                .SendAsync(
                    new Uri("queue:AuthorizePaymentCommand"),
                    context => Task.FromResult(new AuthorizePaymentCommand(
                        OrderId: context.Saga.CorrelationId,
                        Amount: context.Saga.Amount,
                        Currency: context.Saga.Currency,
                        IdempotencyKey: context.Saga.IdempotencyKey,
                        CorrelationId: context.Saga.CorrelationId.ToString(),
                        CausationId: context.Saga.CorrelationId.ToString())))
                .Schedule(AuthorizationTimeout, context =>
                    Task.FromResult(new AuthorizationTimeoutExpired(
                        OrderId: context.Saga.CorrelationId,
                        CorrelationId: context.Saga.CorrelationId.ToString(),
                        OccurredOn: DateTime.UtcNow)))
                .TransitionTo(Authorizing)
        );

        // ── Authorizing: waiting for payment provider response ──
        During(Authorizing,
            When(PaymentAuthorized)
                .Unschedule(AuthorizationTimeout)
                .Then(context =>
                {
                    context.Saga.PaymentId = context.Message.PaymentId;
                    context.Saga.ProviderTransactionId = context.Message.ProviderTransactionId;
                    context.Saga.UpdatedAt = DateTime.UtcNow;

                    _logger.LogInformation(
                        "Saga Order {OrderId}: Payment {PaymentId} authorized",
                        context.Saga.CorrelationId, context.Message.PaymentId);
                })
                .PublishAsync(context => Task.FromResult(new OrderSagaStateChanged(
                    OrderId: context.Saga.CorrelationId,
                    PreviousState: "Authorizing",
                    CurrentState: "Authorized",
                    PaymentId: context.Saga.PaymentId?.ToString(),
                    FailureReason: null,
                    CorrelationId: context.Saga.CorrelationId.ToString(),
                    OccurredOn: DateTime.UtcNow)))
                .TransitionTo(Authorized),

            When(PaymentFailed)
                .Unschedule(AuthorizationTimeout)
                .Then(context =>
                {
                    context.Saga.FailureReason = context.Message.Reason;
                    context.Saga.UpdatedAt = DateTime.UtcNow;

                    _logger.LogWarning(
                        "Saga Order {OrderId}: Authorization failed — {Reason}",
                        context.Saga.CorrelationId, context.Message.Reason);
                })
                .PublishAsync(context => Task.FromResult(new OrderSagaStateChanged(
                    OrderId: context.Saga.CorrelationId,
                    PreviousState: "Authorizing",
                    CurrentState: "Failed",
                    PaymentId: context.Saga.PaymentId?.ToString(),
                    FailureReason: context.Message.Reason,
                    CorrelationId: context.Saga.CorrelationId.ToString(),
                    OccurredOn: DateTime.UtcNow)))
                .TransitionTo(Failed),

            When(AuthorizationTimeout.Received)
                .Then(context =>
                {
                    context.Saga.FailureReason = "Authorization timed out after 5 minutes";
                    context.Saga.UpdatedAt = DateTime.UtcNow;

                    _logger.LogWarning(
                        "Saga Order {OrderId}: Authorization timed out",
                        context.Saga.CorrelationId);
                })
                .PublishAsync(context => Task.FromResult(new OrderSagaStateChanged(
                    OrderId: context.Saga.CorrelationId,
                    PreviousState: "Authorizing",
                    CurrentState: "Failed",
                    PaymentId: null,
                    FailureReason: "Authorization timed out after 5 minutes",
                    CorrelationId: context.Saga.CorrelationId.ToString(),
                    OccurredOn: DateTime.UtcNow)))
                .TransitionTo(Failed),

            // Ignore cancel while authorizing — no payment to cancel yet
            When(CancelRequested)
                .Then(context =>
                {
                    context.Saga.FailureReason = "Cancelled during authorization";
                    context.Saga.UpdatedAt = DateTime.UtcNow;
                })
                .Unschedule(AuthorizationTimeout)
                .PublishAsync(context => Task.FromResult(new OrderSagaStateChanged(
                    OrderId: context.Saga.CorrelationId,
                    PreviousState: "Authorizing",
                    CurrentState: "Cancelled",
                    PaymentId: null,
                    FailureReason: "Cancelled during authorization",
                    CorrelationId: context.Saga.CorrelationId.ToString(),
                    OccurredOn: DateTime.UtcNow)))
                .TransitionTo(Cancelled)
        );

        // ── Authorized: waiting for user to confirm or cancel ──
        During(Authorized,
            When(ConfirmRequested)
                .Then(context =>
                {
                    context.Saga.UpdatedAt = DateTime.UtcNow;

                    _logger.LogInformation(
                        "Saga Order {OrderId}: Confirmed, sending capture for Payment {PaymentId}",
                        context.Saga.CorrelationId, context.Saga.PaymentId);
                })
                .SendAsync(
                    new Uri("queue:CapturePaymentCommand"),
                    context => Task.FromResult(new CapturePaymentCommand(
                        PaymentId: context.Saga.PaymentId!.Value,
                        OrderId: context.Saga.CorrelationId,
                        IdempotencyKey: $"capture-{context.Saga.CorrelationId}",
                        CorrelationId: context.Saga.CorrelationId.ToString(),
                        CausationId: context.Saga.CorrelationId.ToString())))
                .PublishAsync(context => Task.FromResult(new OrderSagaStateChanged(
                    OrderId: context.Saga.CorrelationId,
                    PreviousState: "Authorized",
                    CurrentState: "Capturing",
                    PaymentId: context.Saga.PaymentId?.ToString(),
                    FailureReason: null,
                    CorrelationId: context.Saga.CorrelationId.ToString(),
                    OccurredOn: DateTime.UtcNow)))
                .TransitionTo(Capturing),

            When(CancelRequested)
                .Then(context =>
                {
                    context.Saga.UpdatedAt = DateTime.UtcNow;

                    _logger.LogInformation(
                        "Saga Order {OrderId}: Cancel requested, releasing auth hold on Payment {PaymentId}",
                        context.Saga.CorrelationId, context.Saga.PaymentId);
                })
                .SendAsync(
                    new Uri("queue:CancelPaymentCommand"),
                    context => Task.FromResult(new CancelPaymentCommand(
                        PaymentId: context.Saga.PaymentId!.Value,
                        OrderId: context.Saga.CorrelationId,
                        IdempotencyKey: $"cancel-{context.Saga.CorrelationId}",
                        CorrelationId: context.Saga.CorrelationId.ToString(),
                        CausationId: context.Saga.CorrelationId.ToString())))
                .PublishAsync(context => Task.FromResult(new OrderSagaStateChanged(
                    OrderId: context.Saga.CorrelationId,
                    PreviousState: "Authorized",
                    CurrentState: "Cancelled",
                    PaymentId: context.Saga.PaymentId?.ToString(),
                    FailureReason: null,
                    CorrelationId: context.Saga.CorrelationId.ToString(),
                    OccurredOn: DateTime.UtcNow)))
                .TransitionTo(Cancelled)
        );

        // ── Capturing: waiting for capture result ──
        During(Capturing,
            When(PaymentCaptured)
                .Then(context =>
                {
                    context.Saga.UpdatedAt = DateTime.UtcNow;

                    _logger.LogInformation(
                        "Saga Order {OrderId}: Payment captured! Lifecycle complete.",
                        context.Saga.CorrelationId);
                })
                .PublishAsync(context => Task.FromResult(new OrderSagaStateChanged(
                    OrderId: context.Saga.CorrelationId,
                    PreviousState: "Capturing",
                    CurrentState: "Captured",
                    PaymentId: context.Saga.PaymentId?.ToString(),
                    FailureReason: null,
                    CorrelationId: context.Saga.CorrelationId.ToString(),
                    OccurredOn: DateTime.UtcNow)))
                .TransitionTo(Captured),

            When(PaymentFailed)
                .Then(context =>
                {
                    context.Saga.FailureReason = context.Message.Reason;
                    context.Saga.UpdatedAt = DateTime.UtcNow;

                    _logger.LogWarning(
                        "Saga Order {OrderId}: Capture failed — {Reason}. Compensating: cancelling auth hold.",
                        context.Saga.CorrelationId, context.Message.Reason);
                })
                // Compensation: release the authorized hold
                .SendAsync(
                    new Uri("queue:CancelPaymentCommand"),
                    context => Task.FromResult(new CancelPaymentCommand(
                        PaymentId: context.Saga.PaymentId!.Value,
                        OrderId: context.Saga.CorrelationId,
                        IdempotencyKey: $"compensate-cancel-{context.Saga.CorrelationId}",
                        CorrelationId: context.Saga.CorrelationId.ToString(),
                        CausationId: context.Saga.CorrelationId.ToString())))
                .PublishAsync(context => Task.FromResult(new OrderSagaStateChanged(
                    OrderId: context.Saga.CorrelationId,
                    PreviousState: "Capturing",
                    CurrentState: "Failed",
                    PaymentId: context.Saga.PaymentId?.ToString(),
                    FailureReason: context.Message.Reason,
                    CorrelationId: context.Saga.CorrelationId.ToString(),
                    OccurredOn: DateTime.UtcNow)))
                .TransitionTo(Failed)
        );

        // ── Terminal states: ignore further messages ──
        During(Captured,
            Ignore(ConfirmRequested),
            Ignore(CancelRequested),
            Ignore(PaymentAuthorized),
            Ignore(PaymentCaptured),
            Ignore(PaymentFailed)
        );

        During(Failed,
            Ignore(ConfirmRequested),
            Ignore(CancelRequested),
            Ignore(PaymentAuthorized),
            Ignore(PaymentCaptured),
            Ignore(PaymentFailed),
            Ignore(AuthorizationTimeout.Received)
        );

        During(Cancelled,
            Ignore(ConfirmRequested),
            Ignore(CancelRequested),
            Ignore(PaymentAuthorized),
            Ignore(PaymentCaptured),
            Ignore(PaymentFailed),
            Ignore(AuthorizationTimeout.Received)
        );
    }
}
