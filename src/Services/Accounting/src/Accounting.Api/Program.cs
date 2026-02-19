using Accounting.Application.EventHandlers;
using Accounting.Application.Services;
using Accounting.Domain.Repositories;
using Accounting.Infrastructure.Persistence;
using BuildingBlocks.Messaging;
using BuildingBlocks.Observability;
using BuildingBlocks.Persistence;
using MassTransit;

var builder = WebApplication.CreateBuilder(args);

// Observability
builder.AddObservability("Accounting");

// Database — provider (PostgreSQL / SqlServer) is resolved from "Database:Provider" in config.
// Connection string is resolved from "ConnectionStrings:AccountingDb".
// When running via Aspire the AppHost injects both values automatically.
builder.Services.AddServiceDatabase<AccountingDbContext>(builder.Configuration, "AccountingDb");

builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AccountingDbContext>());
builder.Services.AddScoped<ILedgerRepository, LedgerRepository>();
builder.Services.AddScoped<ReconciliationService>();

// MassTransit + RabbitMQ
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<PaymentCapturedEventHandler>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration.GetConnectionString("RabbitMq") ?? "rabbitmq://localhost");
        cfg.ConfigureEndpoints(context);
    });
});

builder.Services.AddScoped<IEventBus, MassTransitEventBus>();

// Outbox dispatcher
builder.Services.AddHostedService<OutboxDispatcher<AccountingDbContext>>();

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
    var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
    await db.Database.EnsureCreatedAsync();
}

// Endpoints
app.MapGet("/api/ledger/{transactionId:guid}", async (Guid transactionId, ILedgerRepository repo) =>
{
    var entries = await repo.GetByTransactionIdAsync(transactionId);
    return entries.Count > 0
        ? Results.Ok(entries.Select(e => new
        {
            e.Id,
            e.TransactionId,
            e.PaymentId,
            e.AccountName,
            e.DebitAmount,
            e.CreditAmount,
            e.Currency,
            e.Description,
            e.CreatedAt
        }))
        : Results.NotFound();
});

app.MapGet("/api/ledger/balance/{accountName}", async (string accountName, ILedgerRepository repo) =>
{
    var entries = await repo.GetByAccountNameAsync(accountName);
    var totalDebits = entries.Sum(e => e.DebitAmount);
    var totalCredits = entries.Sum(e => e.CreditAmount);

    return Results.Ok(new
    {
        Account = accountName,
        TotalDebits = totalDebits,
        TotalCredits = totalCredits,
        NetBalance = totalDebits - totalCredits,
        EntryCount = entries.Count
    });
});

app.MapPost("/api/reconciliation/run", async (ReconciliationService reconciliationService) =>
{
    var result = await reconciliationService.RunReconciliationAsync();
    return result.IsBalanced ? Results.Ok(result) : Results.UnprocessableEntity(result);
});

app.Run();
