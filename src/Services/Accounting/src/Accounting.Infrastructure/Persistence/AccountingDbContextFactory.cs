using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Accounting.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used exclusively by EF Core tooling (dotnet ef migrations add / update).
/// At design time there is no running host, so this factory reads configuration from
/// environment variables — the same keys the runtime uses, just sourced differently.
///
/// Running migrations:
///
///   PostgreSQL (default):
///     dotnet ef migrations add &lt;Name&gt; --project Accounting.Infrastructure --startup-project Accounting.Api
///
///   SQL Server:
///     $env:Database__Provider                = "SqlServer"
///     $env:ConnectionStrings__AccountingDb   = "Server=localhost;Database=accounting_db;..."
///     dotnet ef migrations add &lt;Name&gt; --project Accounting.Infrastructure --startup-project Accounting.Api
///
///   Apply to database:
///     dotnet ef database update --project Accounting.Infrastructure --startup-project Accounting.Api
/// </summary>
public sealed class AccountingDbContextFactory : IDesignTimeDbContextFactory<AccountingDbContext>
{
    public AccountingDbContext CreateDbContext(string[] args)
    {
        var provider         = Environment.GetEnvironmentVariable("Database__Provider") ?? "PostgreSQL";
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__AccountingDb")
                               ?? "Host=localhost;Database=accounting_db;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<AccountingDbContext>();

        if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            optionsBuilder.UseSqlServer(connectionString);
        else
            optionsBuilder.UseNpgsql(connectionString);

        return new AccountingDbContext(optionsBuilder.Options);
    }
}
