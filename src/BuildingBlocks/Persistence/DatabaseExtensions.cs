using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Persistence;

/// <summary>
/// Centralised database registration for all services.
///
/// Supports two providers, selected at runtime via "Database:Provider" in configuration:
///
///   "PostgreSQL"  (default)  →  Npgsql — use locally with Docker, or Azure Database for PostgreSQL
///                                         Flexible Server in production.
///
///   "SqlServer"              →  Microsoft SQL Server — use locally with SQL Server Express / Docker,
///                                         or Azure SQL Database / Azure SQL Managed Instance in production.
///
/// Retry-on-failure is always enabled to handle transient Azure infrastructure faults (throttling,
/// brief network blips, failovers).  Both providers use identical retry parameters so behaviour is
/// consistent across environments.
///
/// Usage (in each service's Program.cs):
/// <code>
///   builder.Services.AddServiceDatabase&lt;OrdersDbContext&gt;(
///       builder.Configuration, "OrdersDb");
/// </code>
///
/// The connection string name must match a key under "ConnectionStrings" in configuration.
/// When running via Aspire the AppHost injects connection strings automatically, so no
/// explicit value is needed in appsettings.json.
///
/// Switching providers for local development:
///   1. Set "Database:Provider": "SqlServer" in appsettings.Development.json, OR
///   2. Set the env var  Database__Provider=SqlServer  before running, OR
///   3. Change the default in AppHost/appsettings.json (affects the Aspire-provisioned container).
/// </summary>
public static class DatabaseExtensions
{
    private const string PostgreSqlProvider = "PostgreSQL";
    private const string SqlServerProvider  = "SqlServer";

    /// <summary>
    /// Registers <typeparamref name="TDbContext"/> with the database provider determined by
    /// <c>Database:Provider</c> in <paramref name="configuration"/> (defaults to "PostgreSQL").
    /// The connection string is read from <c>ConnectionStrings:{connectionStringName}</c>.
    /// </summary>
    public static IServiceCollection AddServiceDatabase<TDbContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName)
        where TDbContext : DbContext
    {
        var provider = configuration["Database:Provider"] ?? PostgreSqlProvider;

        var connectionString = configuration.GetConnectionString(connectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{connectionStringName}' is not configured. " +
                $"Add 'ConnectionStrings:{connectionStringName}' to appsettings.json " +
                $"or set it via an environment variable / Azure App Configuration.");

        services.AddDbContext<TDbContext>(options =>
            ConfigureProvider(options, provider, connectionString));

        return services;
    }

    // ---------------------------------------------------------------------------
    // Internal helpers
    // ---------------------------------------------------------------------------

    private static void ConfigureProvider(
        DbContextOptionsBuilder options,
        string provider,
        string connectionString)
    {
        if (provider.Equals(SqlServerProvider, StringComparison.OrdinalIgnoreCase))
        {
            // Azure SQL Database / SQL Server
            // TrustServerCertificate can be set in the connection string itself for dev:
            //   Server=localhost;Database=...;User Id=sa;Password=...;TrustServerCertificate=true
            // For Azure SQL use managed identity or AAD token authentication in the connection string.
            options.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorNumbersToAdd: null);

                sqlOptions.CommandTimeout(30);
            });
        }
        else
        {
            // Azure Database for PostgreSQL Flexible Server (or local Docker)
            // For Azure: Host=<server>.postgres.database.azure.com;Database=...;
            //            Username=<user>@<server>;Password=...;SslMode=Require;
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorCodesToAdd: null);

                npgsqlOptions.CommandTimeout(30);
            });
        }
    }
}
