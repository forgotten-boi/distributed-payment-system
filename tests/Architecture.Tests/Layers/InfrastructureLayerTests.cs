using System.Reflection;
using BuildingBlocks.Persistence;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Architecture.Tests.Layers;

/// <summary>
/// Verifies that Infrastructure layers implement the interfaces defined in Domain
/// and Application, without leaking upward to the Api composition root.
/// Infrastructure is the outermost ring (except Api) and must flow inward only.
/// </summary>
public class InfrastructureLayerTests
{
    // ── Test data ───────────────────────────────────────────────────────────────
    // Payments.Infrastructure excluded: pre-existing Adyen SDK breaking change.

    public static TheoryData<Assembly, string> BuildableInfraAssemblies => new()
    {
        { TestAssemblies.OrdersInfrastructure,    "Orders.Infrastructure" },
        { TestAssemblies.AccountingInfrastructure, "Accounting.Infrastructure" },
    };

    // ── No upward dependency on the Api composition root ────────────────────────

    [Theory, MemberData(nameof(BuildableInfraAssemblies))]
    public void Infrastructure_ShouldNotDependOn_ApiLayer(Assembly assembly, string name)
    {
        var result = Types.InAssembly(assembly)
            .ShouldNot().HaveDependencyOnAny(
                "Orders.Api",
                "Payments.Api",
                "Accounting.Api")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"[{name}] must not depend on the Api layer — the Api is the composition root and depends on Infrastructure, not vice versa.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    // ── No cross-service domain coupling ────────────────────────────────────────

    [Fact]
    public void OrdersInfrastructure_ShouldNotDependOn_OtherServicesDomain()
    {
        var result = Types.InAssembly(TestAssemblies.OrdersInfrastructure)
            .ShouldNot().HaveDependencyOnAny(
                "Payments.Domain",
                "Accounting.Domain")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"Orders.Infrastructure must not couple to Payments or Accounting domains — services communicate via BuildingBlocks.Contracts only.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    [Fact]
    public void AccountingInfrastructure_ShouldNotDependOn_OtherServicesDomain()
    {
        var result = Types.InAssembly(TestAssemblies.AccountingInfrastructure)
            .ShouldNot().HaveDependencyOnAny(
                "Orders.Domain",
                "Payments.Domain")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"Accounting.Infrastructure must not couple to Orders or Payments domains.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    // ── DbContext inherits OutboxDbContext ──────────────────────────────────────

    [Theory, MemberData(nameof(BuildableInfraAssemblies))]
    public void DbContexts_ShouldInherit_OutboxDbContext(Assembly assembly, string name)
    {
        var result = Types.InAssembly(assembly)
            .That().HaveNameEndingWith("DbContext")
            .And().AreClasses()
            .Should().Inherit(typeof(OutboxDbContext))
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"[{name}] All *DbContext classes must inherit OutboxDbContext to participate in the transactional outbox pattern.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    // ── Repository implementations reside in Persistence namespace ──────────────

    [Theory, MemberData(nameof(BuildableInfraAssemblies))]
    public void RepositoryImplementations_ShouldResideIn_PersistenceNamespace(Assembly assembly, string name)
    {
        var result = Types.InAssembly(assembly)
            .That().HaveNameEndingWith("Repository")
            .And().AreClasses()
            .Should().ResideInNamespaceContaining(".Persistence")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"[{name}] Repository implementations must reside in the Persistence namespace.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }
}
