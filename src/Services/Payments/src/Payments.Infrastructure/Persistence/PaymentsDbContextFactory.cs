using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Payments.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used exclusively by EF Core tooling (dotnet ef migrations add / update).
/// At design time there is no running host, so this factory reads configuration from
/// environment variables — the same keys the runtime uses, just sourced differently.
///
/// Running migrations:
///
///   PostgreSQL (default):
///     dotnet ef migrations add &lt;Name&gt; --project Payments.Infrastructure --startup-project Payments.Api
///
///   SQL Server:
///     $env:Database__Provider              = "SqlServer"
///     $env:ConnectionStrings__PaymentsDb   = "Server=localhost;Database=payments_db;..."
///     dotnet ef migrations add &lt;Name&gt; --project Payments.Infrastructure --startup-project Payments.Api
///
///   Apply to database:
///     dotnet ef database update --project Payments.Infrastructure --startup-project Payments.Api
/// </summary>
public sealed class PaymentsDbContextFactory : IDesignTimeDbContextFactory<PaymentsDbContext>
{
    public PaymentsDbContext CreateDbContext(string[] args)
    {
        var provider         = Environment.GetEnvironmentVariable("Database__Provider") ?? "PostgreSQL";
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PaymentsDb")
                               ?? "Host=localhost;Database=payments_db;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<PaymentsDbContext>();

        if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            optionsBuilder.UseSqlServer(connectionString);
        else
            optionsBuilder.UseNpgsql(connectionString);

        return new PaymentsDbContext(optionsBuilder.Options);
    }
}
