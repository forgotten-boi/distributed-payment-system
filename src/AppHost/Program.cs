/// <summary>
/// Aspire AppHost — the distributed application orchestrator.
///
/// This wires together all infrastructure and services:
///  - Databases (one per service — database-per-service pattern)
///      PostgreSQL containers  when DatabaseProvider = "PostgreSQL" (default, local dev)
///      SQL Server container   when DatabaseProvider = "SqlServer"  (local dev / CI)
///      For Azure, replace AddPostgres/AddSqlServer with the corresponding
///      AddAzurePostgresFlexibleServer / AddAzureSqlServer Aspire resource.
///  - RabbitMQ message broker (shared for inter-service communication)
///  - All three business services (Orders, Payments, Accounting)
///  - Connection strings + Database:Provider env var injected automatically into each service
///
/// Switch the provider by setting "DatabaseProvider" in AppHost/appsettings.json
/// or via an environment variable: DatabaseProvider=SqlServer dotnet run
///
/// Running `dotnet run --project src/AppHost` starts the entire system:
///  - Infrastructure containers
///  - All services with proper configuration
///  - Aspire dashboard for observability
/// </summary>
var builder = DistributedApplication.CreateBuilder(args);

var databaseProvider = builder.Configuration["DatabaseProvider"] ?? "PostgreSQL";

// ---------------------------------------------------------------------------
// Infrastructure
// ---------------------------------------------------------------------------
var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin();

// Provision the correct database containers and expose them through the shared
// IResourceWithConnectionString interface. The explicit cast via object is
// required because IResourceBuilder<T> is invariant — both PostgresDatabaseResource
// and SqlServerDatabaseResource implement IResourceWithConnectionString at runtime.
IResourceBuilder<IResourceWithConnectionString> ordersDb;
IResourceBuilder<IResourceWithConnectionString> paymentsDb;
IResourceBuilder<IResourceWithConnectionString> accountingDb;

if (databaseProvider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
{
    // Single SQL Server container — three logical databases
    var sqlServer = builder.AddSqlServer("sqlserver");
    ordersDb     = AsConnectionString(sqlServer.AddDatabase("OrdersDb"));
    paymentsDb   = AsConnectionString(sqlServer.AddDatabase("PaymentsDb"));
    accountingDb = AsConnectionString(sqlServer.AddDatabase("AccountingDb"));
}
else
{
    // Separate PostgreSQL container per service for stronger resource isolation
    ordersDb     = AsConnectionString(builder.AddPostgres("postgres-orders").WithPgAdmin().AddDatabase("OrdersDb"));
    paymentsDb   = AsConnectionString(builder.AddPostgres("postgres-payments").AddDatabase("PaymentsDb"));
    accountingDb = AsConnectionString(builder.AddPostgres("postgres-accounting").AddDatabase("AccountingDb"));
}

// ---------------------------------------------------------------------------
// Services
// ---------------------------------------------------------------------------
var ordersService = builder.AddProject<Projects.Orders_Api>("orders-api")
    .WithReference(ordersDb)
    .WithReference(rabbitmq)
    // Tell the service which EF Core provider to configure at startup
    .WithEnvironment("Database__Provider", databaseProvider)
    .WaitFor(ordersDb)
    .WaitFor(rabbitmq)
    .WithExternalHttpEndpoints();

var paymentsService = builder.AddProject<Projects.Payments_Api>("payments-api")
    .WithReference(paymentsDb)
    .WithReference(rabbitmq)
    .WithEnvironment("Database__Provider", databaseProvider)
    .WaitFor(paymentsDb)
    .WaitFor(rabbitmq)
    .WithExternalHttpEndpoints();

var accountingService = builder.AddProject<Projects.Accounting_Api>("accounting-api")
    .WithReference(accountingDb)
    .WithReference(rabbitmq)
    .WithEnvironment("Database__Provider", databaseProvider)
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

// ---------------------------------------------------------------------------
// Helper: explicit cast to the shared IResourceWithConnectionString interface.
// IResourceBuilder<T> is invariant so a direct assignment is not possible even
// when T : IResourceWithConnectionString; (object) intermediate is required.
// ---------------------------------------------------------------------------
static IResourceBuilder<IResourceWithConnectionString> AsConnectionString<T>(
    IResourceBuilder<T> resource)
    where T : IResource, IResourceWithConnectionString
    => (IResourceBuilder<IResourceWithConnectionString>)(object)resource;
