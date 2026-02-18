/// <summary>
/// Aspire AppHost — the distributed application orchestrator.
///
/// This wires together all infrastructure and services:
///  - PostgreSQL databases (one per service — database-per-service pattern)
///  - RabbitMQ message broker (shared for inter-service communication)
///  - All three business services (Orders, Payments, Accounting)
///  - Connection strings injected automatically via Aspire's service discovery
///
/// Running `dotnet run --project src/AppHost` starts the entire system:
///  - Infrastructure containers
///  - All services with proper configuration
///  - Aspire dashboard for observability
///
/// Each service gets its own isolated database, enforcing the principle
/// that no service directly queries another service's database.
/// All inter-service communication flows through RabbitMQ events/commands.
/// </summary>
var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure
var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin();

var ordersDb = builder.AddPostgres("postgres-orders")
    .WithPgAdmin()
    .AddDatabase("OrdersDb");

var paymentsDb = builder.AddPostgres("postgres-payments")
    .AddDatabase("PaymentsDb");

var accountingDb = builder.AddPostgres("postgres-accounting")
    .AddDatabase("AccountingDb");

// Services
var ordersService = builder.AddProject<Projects.Orders_Api>("orders-api")
    .WithReference(ordersDb)
    .WithReference(rabbitmq)
    .WaitFor(ordersDb)
    .WaitFor(rabbitmq)
    .WithExternalHttpEndpoints();

var paymentsService = builder.AddProject<Projects.Payments_Api>("payments-api")
    .WithReference(paymentsDb)
    .WithReference(rabbitmq)
    .WaitFor(paymentsDb)
    .WaitFor(rabbitmq)
    .WithExternalHttpEndpoints();

var accountingService = builder.AddProject<Projects.Accounting_Api>("accounting-api")
    .WithReference(accountingDb)
    .WithReference(rabbitmq)
    .WaitFor(accountingDb)
    .WaitFor(rabbitmq)
    .WithExternalHttpEndpoints();

// Web UI — Blazor Server interactive dashboard for testing the payment lifecycle.
// Uses Aspire service discovery to resolve service endpoints (orders-api, payments-api,
// accounting-api) without hard-coding URLs. The UI communicates with all three services
// via HTTP to create orders, check statuses, and view accounting entries.
builder.AddProject<Projects.WebUI>("web-ui")
    .WithReference(ordersService)
    .WithReference(paymentsService)
    .WithReference(accountingService)
    .WaitFor(ordersService)
    .WaitFor(paymentsService)
    .WaitFor(accountingService)
    .WithExternalHttpEndpoints();

builder.Build().Run();
