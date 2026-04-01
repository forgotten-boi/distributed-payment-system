using BuildingBlocks.Messaging;
using BuildingBlocks.Observability;
using BuildingBlocks.Persistence;
using MassTransit;
using MediatR;
using Orders.Application.Commands;
using Orders.Application.EventHandlers;
using Orders.Application.Queries;
using Orders.Application.Saga;
using Orders.Domain.Repositories;
using Orders.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Observability
builder.AddObservability("Orders");

// Database — provider (PostgreSQL / SqlServer) is resolved from "Database:Provider" in config.
// Connection string is resolved from "ConnectionStrings:OrdersDb".
// When running via Aspire the AppHost injects both values automatically.
builder.Services.AddServiceDatabase<OrdersDbContext>(builder.Configuration, "OrdersDb");

builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<OrdersDbContext>());
builder.Services.AddScoped<IOrderRepository, OrderRepository>();

// MediatR
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<CreateOrderCommand>());

// MassTransit + RabbitMQ + Saga
builder.Services.AddMassTransit(x =>
{
    // Saga state machine — orchestrates the full payment lifecycle
    x.AddSagaStateMachine<OrderPaymentStateMachine, OrderPaymentState>()
        .EntityFrameworkRepository(r =>
        {
            r.ConcurrencyMode = ConcurrencyMode.Pessimistic;
            r.ExistingDbContext<OrdersDbContext>();
        });

    // Event handler that syncs saga state changes back to the Order aggregate
    x.AddConsumer<OrderSagaStateChangedHandler>();

    // Use the delayed message scheduler for saga timeouts
    x.AddDelayedMessageScheduler();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration.GetConnectionString("RabbitMq") ?? "rabbitmq://localhost");

        // Enable delayed message scheduler for saga Schedule() calls
        cfg.UseDelayedMessageScheduler();

        cfg.ConfigureEndpoints(context);
    });
});

builder.Services.AddScoped<IEventBus, MassTransitEventBus>();

// Outbox dispatcher
builder.Services.AddHostedService<OutboxDispatcher<OrdersDbContext>>();

// Exception handling
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

// Middleware
app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();

// Auto-migrate in development
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
    await db.Database.EnsureCreatedAsync();
}

// Endpoints
app.MapPost("/api/orders", async (CreateOrderRequest request, IMediator mediator) =>
{
    var result = await mediator.Send(new CreateOrderCommand(
        request.CustomerId, request.Amount, request.Currency, request.IdempotencyKey));
    return Results.Created($"/api/orders/{result.OrderId}", result);
});

app.MapPost("/api/orders/{id:guid}/confirm", async (Guid id, IMediator mediator) =>
{
    var result = await mediator.Send(new ConfirmOrderCommand(id));
    return Results.Ok(result);
});

app.MapPost("/api/orders/{id:guid}/cancel", async (Guid id, IMediator mediator) =>
{
    var result = await mediator.Send(new CancelOrderCommand(id));
    return Results.Ok(result);
});

app.MapGet("/api/orders/{id:guid}", async (Guid id, IMediator mediator) =>
{
    var result = await mediator.Send(new GetOrderQuery(id));
    return result is not null ? Results.Ok(result) : Results.NotFound();
});

// Saga state endpoint — allows UI/clients to query saga state directly
app.MapGet("/api/orders/{id:guid}/saga-state", async (Guid id, OrdersDbContext db) =>
{
    var state = await db.OrderPaymentSagaStates.FindAsync(id);
    if (state is null) return Results.NotFound();
    return Results.Ok(new
    {
        state.CorrelationId,
        state.CurrentState,
        state.CustomerId,
        state.Amount,
        state.Currency,
        state.PaymentId,
        state.ProviderTransactionId,
        state.FailureReason,
        state.CreatedAt,
        state.UpdatedAt
    });
});

app.Run();

// Request DTOs
public record CreateOrderRequest(Guid CustomerId, decimal Amount, string Currency, string IdempotencyKey);
