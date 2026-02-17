using BuildingBlocks.Messaging;
using BuildingBlocks.Observability;
using BuildingBlocks.Persistence;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Orders.Application.Commands;
using Orders.Application.EventHandlers;
using Orders.Application.Queries;
using Orders.Domain.Repositories;
using Orders.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Observability
builder.AddObservability("Orders");

// Database
builder.Services.AddDbContext<OrdersDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("OrdersDb")));

builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<OrdersDbContext>());
builder.Services.AddScoped<IOrderRepository, OrderRepository>();

// MediatR
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<CreateOrderCommand>());

// MassTransit + RabbitMQ
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<PaymentAuthorizedEventHandler>();
    x.AddConsumer<PaymentCapturedEventHandler>();
    x.AddConsumer<PaymentFailedEventHandler>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration.GetConnectionString("RabbitMq") ?? "rabbitmq://localhost");
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

app.Run();

// Request DTOs
public record CreateOrderRequest(Guid CustomerId, decimal Amount, string Currency, string IdempotencyKey);
