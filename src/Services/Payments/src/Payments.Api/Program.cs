using BuildingBlocks.Messaging;
using BuildingBlocks.Observability;
using BuildingBlocks.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Payments.Application.CommandHandlers;
using Payments.Domain.Gateways;
using Payments.Domain.Repositories;
using Payments.Infrastructure.Gateways;
using Payments.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Observability
builder.AddObservability("Payments");

// Database
builder.Services.AddDbContext<PaymentsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PaymentsDb")));

builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PaymentsDbContext>());
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();

// Payment Gateway — simulated in development, real adapter in production
builder.Services.AddScoped<IPaymentGateway, SimulatedPaymentGateway>();

// MassTransit + RabbitMQ
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<AuthorizePaymentCommandHandler>();
    x.AddConsumer<CapturePaymentCommandHandler>();
    x.AddConsumer<CancelPaymentCommandHandler>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration.GetConnectionString("RabbitMq") ?? "rabbitmq://localhost");
        cfg.ConfigureEndpoints(context);
    });
});

builder.Services.AddScoped<IEventBus, MassTransitEventBus>();

// Outbox dispatcher
builder.Services.AddHostedService<OutboxDispatcher<PaymentsDbContext>>();

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
    var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
    await db.Database.EnsureCreatedAsync();
}

// Endpoints
app.MapGet("/api/payments/{id:guid}", async (Guid id, IPaymentRepository repo) =>
{
    var payment = await repo.GetByIdAsync(id);
    return payment is not null
        ? Results.Ok(new
        {
            payment.Id,
            payment.OrderId,
            payment.Amount,
            payment.Currency,
            Status = payment.Status.ToString(),
            payment.ProviderTransactionId,
            payment.FailureReason,
            payment.CreatedAt
        })
        : Results.NotFound();
});

app.MapPost("/api/payments/webhook", async (HttpRequest request, IPaymentGateway gateway) =>
{
    using var reader = new StreamReader(request.Body);
    var payload = await reader.ReadToEndAsync();
    var signature = request.Headers["X-Webhook-Signature"].FirstOrDefault() ?? "";

    var result = await gateway.HandleWebhookAsync(payload, signature);
    return Results.Ok(new { result.EventType, result.TransactionId });
});

app.Run();
