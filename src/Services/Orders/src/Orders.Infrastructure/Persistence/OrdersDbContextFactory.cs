using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Orders.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used exclusively by EF Core tooling (dotnet ef migrations add / update).
/// At design time there is no running host, so this factory reads configuration from
/// environment variables — the same keys the runtime uses, just sourced differently.
///
/// Running migrations:
///
///   PostgreSQL (default):
///     dotnet ef migrations add &lt;Name&gt; --project Orders.Infrastructure --startup-project Orders.Api
///
///   SQL Server:
///     $env:Database__Provider            = "SqlServer"
///     $env:ConnectionStrings__OrdersDb   = "Server=localhost;Database=orders_db;..."
///     dotnet ef migrations add &lt;Name&gt; --project Orders.Infrastructure --startup-project Orders.Api
///
///   Apply to database:
///     dotnet ef database update --project Orders.Infrastructure --startup-project Orders.Api
/// </summary>
public sealed class OrdersDbContextFactory : IDesignTimeDbContextFactory<OrdersDbContext>
{
    public OrdersDbContext CreateDbContext(string[] args)
    {
        var provider         = Environment.GetEnvironmentVariable("Database__Provider") ?? "PostgreSQL";
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__OrdersDb")
                               ?? "Host=localhost;Database=orders_db;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<OrdersDbContext>();

        if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            optionsBuilder.UseSqlServer(connectionString);
        else
            optionsBuilder.UseNpgsql(connectionString);

        return new OrdersDbContext(optionsBuilder.Options);
    }
}
