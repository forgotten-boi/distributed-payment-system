using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using BuildingBlocks.Persistence;
using Xunit;

namespace Architecture.Tests.Layers;

/// <summary>
/// Verifies that the Domain layer of every service remains isolated from
/// infrastructure concerns (EF Core, MassTransit, MediatR) and from higher layers.
/// This is the most important architectural boundary — a pure domain model
/// contains only business rules and is independent of any framework.
/// </summary>
public class DomainLayerTests
{
    // ── Test data ───────────────────────────────────────────────────────────────

    public static TheoryData<Assembly, string> AllDomainAssemblies => new()
    {
        { TestAssemblies.OrdersDomain,    "Orders.Domain" },
        { TestAssemblies.PaymentsDomain,  "Payments.Domain" },
        { TestAssemblies.AccountingDomain, "Accounting.Domain" },
    };

    public static TheoryData<Assembly, string> ServicesWithAggregates => new()
    {
        { TestAssemblies.OrdersDomain,   "Orders.Domain.Aggregates" },
        { TestAssemblies.PaymentsDomain, "Payments.Domain.Aggregates" },
    };

    // ── No external infrastructure dependencies ─────────────────────────────────

    [Theory, MemberData(nameof(AllDomainAssemblies))]
    public void Domain_ShouldNotDependOn_EntityFrameworkCore(Assembly assembly, string name)
    {
        var result = Types.InAssembly(assembly)
            .ShouldNot().HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"[{name}] must not depend on EF Core — persistence is an infrastructure concern.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    [Theory, MemberData(nameof(AllDomainAssemblies))]
    public void Domain_ShouldNotDependOn_MassTransit(Assembly assembly, string name)
    {
        var result = Types.InAssembly(assembly)
            .ShouldNot().HaveDependencyOn("MassTransit")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"[{name}] must not depend on MassTransit — messaging is an infrastructure concern.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    [Theory, MemberData(nameof(AllDomainAssemblies))]
    public void Domain_ShouldNotDependOn_MediatR(Assembly assembly, string name)
    {
        var result = Types.InAssembly(assembly)
            .ShouldNot().HaveDependencyOn("MediatR")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"[{name}] must not depend on MediatR — CQRS dispatch belongs to Application.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    // ── No upward or lateral layer dependencies ─────────────────────────────────

    [Theory, MemberData(nameof(AllDomainAssemblies))]
    public void Domain_ShouldNotDependOn_InfrastructureLayer(Assembly assembly, string name)
    {
        var result = Types.InAssembly(assembly)
            .ShouldNot().HaveDependencyOnAny(
                "Orders.Infrastructure",
                "Payments.Infrastructure",
                "Accounting.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"[{name}] must not depend on Infrastructure — this inverts the dependency direction.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    [Theory, MemberData(nameof(AllDomainAssemblies))]
    public void Domain_ShouldNotDependOn_ApplicationLayer(Assembly assembly, string name)
    {
        var result = Types.InAssembly(assembly)
            .ShouldNot().HaveDependencyOnAny(
                "Orders.Application",
                "Payments.Application",
                "Accounting.Application")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"[{name}] must not depend on Application — Domain is the innermost ring.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    // ── Domain event contracts ──────────────────────────────────────────────────

    [Theory, MemberData(nameof(AllDomainAssemblies))]
    public void DomainEvents_ShouldImplement_IDomainEvent(Assembly assembly, string name)
    {
        var result = Types.InAssembly(assembly)
            .That().HaveNameEndingWith("DomainEvent")
            .Should().ImplementInterface(typeof(IDomainEvent))
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"[{name}] All *DomainEvent types must implement IDomainEvent.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    [Theory, MemberData(nameof(AllDomainAssemblies))]
    public void TypesImplementingIDomainEvent_ShouldEndWith_DomainEvent(Assembly assembly, string name)
    {
        var result = Types.InAssembly(assembly)
            .That().ImplementInterface(typeof(IDomainEvent))
            .Should().HaveNameEndingWith("DomainEvent")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"[{name}] All IDomainEvent implementations must end with 'DomainEvent'.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    // ── Aggregate structure ─────────────────────────────────────────────────────

    [Theory, MemberData(nameof(ServicesWithAggregates))]
    public void Aggregates_ShouldInherit_AggregateRoot(Assembly assembly, string aggregateNamespace)
    {
        var result = Types.InAssembly(assembly)
            .That().ResideInNamespace(aggregateNamespace)
            .And().AreClasses()
            .Should().Inherit(typeof(AggregateRoot))
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"All classes in [{aggregateNamespace}] must inherit AggregateRoot to participate in domain-event collection.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    // ── Repository interface contracts ──────────────────────────────────────────

    [Theory, MemberData(nameof(AllDomainAssemblies))]
    public void RepositoryInterfaces_ShouldStartWith_I(Assembly assembly, string name)
    {
        var result = Types.InAssembly(assembly)
            .That().ResideInNamespaceContaining(".Repositories")
            .And().AreInterfaces()
            .Should().HaveNameStartingWith("I")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"[{name}] Repository interfaces must follow the 'I' prefix convention.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    [Theory, MemberData(nameof(AllDomainAssemblies))]
    public void RepositoryInterfaces_ShouldEndWith_Repository(Assembly assembly, string name)
    {
        var result = Types.InAssembly(assembly)
            .That().ResideInNamespaceContaining(".Repositories")
            .And().AreInterfaces()
            .Should().HaveNameEndingWith("Repository")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"[{name}] Repository interfaces must end with 'Repository'.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }
}
