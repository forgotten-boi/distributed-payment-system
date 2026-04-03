using System.Reflection;

// Anchor types used to resolve assemblies at runtime — one per layer per service.
using Accounting.Application.EventHandlers;
using Accounting.Domain.Entities;
using Accounting.Infrastructure.Persistence;
using BuildingBlocks.Contracts.Commands;
using BuildingBlocks.Persistence;
using Orders.Application.Commands;
using Orders.Domain.Aggregates;
using Orders.Infrastructure.Persistence;
using Payments.Application.CommandHandlers;
using Payments.Domain.Aggregates;

namespace Architecture.Tests;

/// <summary>
/// Provides compiled assembly references used across all architectural tests.
/// Each assembly is resolved via a public anchor type from that layer.
/// </summary>
internal static class TestAssemblies
{
    // ── Orders ─────────────────────────────────────────────────────────────────
    public static readonly Assembly OrdersDomain         = typeof(Order).Assembly;
    public static readonly Assembly OrdersApplication    = typeof(CreateOrderCommand).Assembly;
    public static readonly Assembly OrdersInfrastructure = typeof(OrdersDbContext).Assembly;

    // ── Payments ───────────────────────────────────────────────────────────────
    // Payments.Infrastructure excluded: pre-existing Adyen SDK breaking change
    public static readonly Assembly PaymentsDomain         = typeof(Payment).Assembly;
    public static readonly Assembly PaymentsApplication    = typeof(AuthorizePaymentCommandHandler).Assembly;

    // ── Accounting ─────────────────────────────────────────────────────────────
    public static readonly Assembly AccountingDomain         = typeof(LedgerEntry).Assembly;
    public static readonly Assembly AccountingApplication    = typeof(PaymentCapturedEventHandler).Assembly;
    public static readonly Assembly AccountingInfrastructure = typeof(AccountingDbContext).Assembly;

    // ── BuildingBlocks ─────────────────────────────────────────────────────────
    public static readonly Assembly BuildingBlocksPersistence = typeof(AggregateRoot).Assembly;
    public static readonly Assembly BuildingBlocksContracts   = typeof(AuthorizePaymentCommand).Assembly;

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Returns a bullet list of failing type full names for assertion messages.</summary>
    public static string FormatFailures(NetArchTest.Rules.TestResult result)
        => result.IsSuccessful
            ? "(none)"
            : string.Join("\n  - ", result.FailingTypes.Select(t => t.FullName));
}
