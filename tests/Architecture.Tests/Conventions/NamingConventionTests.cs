using System.Reflection;
using BuildingBlocks.Persistence;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Architecture.Tests.Conventions;

/// <summary>
/// Enforces consistent naming conventions across all service layers.
/// Predictable names are an architectural property, not a style preference:
/// they allow developers and tooling to navigate the codebase by convention.
/// </summary>
public class NamingConventionTests
{
    // ── Test data ───────────────────────────────────────────────────────────────

    public static TheoryData<Assembly, string> AllDomainAssemblies => new()
    {
        { TestAssemblies.OrdersDomain,    "Orders.Domain" },
        { TestAssemblies.PaymentsDomain,  "Payments.Domain" },
        { TestAssemblies.AccountingDomain, "Accounting.Domain" },
    };

    public static TheoryData<Assembly, string> AllApplicationAssemblies => new()
    {
        { TestAssemblies.OrdersApplication,    "Orders.Application" },
        { TestAssemblies.PaymentsApplication,  "Payments.Application" },
        { TestAssemblies.AccountingApplication, "Accounting.Application" },
    };

    // ── Domain naming ───────────────────────────────────────────────────────────

    [Theory, MemberData(nameof(AllDomainAssemblies))]
    public void DomainEvents_ShouldEndWith_DomainEvent(Assembly assembly, string name)
    {
        var result = Types.InAssembly(assembly)
            .That().ImplementInterface(typeof(IDomainEvent))
            .Should().HaveNameEndingWith("DomainEvent")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"[{name}] All domain event types must end with 'DomainEvent'.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    [Theory, MemberData(nameof(AllDomainAssemblies))]
    public void Interfaces_ShouldStartWith_I(Assembly assembly, string name)
    {
        var result = Types.InAssembly(assembly)
            .That().AreInterfaces()
            .Should().HaveNameStartingWith("I")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"[{name}] All interfaces must follow the 'I' prefix convention.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    // ── Application naming ──────────────────────────────────────────────────────

    [Fact]
    public void Orders_Commands_ShouldEndWith_Command()
    {
        var result = Types.InAssembly(TestAssemblies.OrdersApplication)
            .That().ResideInNamespace("Orders.Application.Commands")
            .And().AreClasses()
            .And().DoNotHaveNameEndingWith("Handler")
            .And().DoNotHaveNameEndingWith("Result")
            .Should().HaveNameEndingWith("Command")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"Command types (excluding handlers/results) must end with 'Command'.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    [Fact]
    public void Orders_Queries_ShouldEndWith_Query()
    {
        var result = Types.InAssembly(TestAssemblies.OrdersApplication)
            .That().ResideInNamespace("Orders.Application.Queries")
            .And().AreClasses()
            .And().DoNotHaveNameEndingWith("Handler")
            .And().DoNotHaveNameEndingWith("Dto")
            .Should().HaveNameEndingWith("Query")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"Query types (excluding handlers/DTOs) must end with 'Query'.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    [Theory, MemberData(nameof(AllApplicationAssemblies))]
    public void Handlers_ShouldEndWith_Handler(Assembly assembly, string name)
    {
        var result = Types.InAssembly(assembly)
            .That().HaveNameMatching(".*Handler.*")
            .And().AreClasses()
            .Should().HaveNameEndingWith("Handler")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"[{name}] Any class with 'Handler' in the name must end with 'Handler' (not 'HandlerFactory' etc.).\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    // ── Infrastructure naming ───────────────────────────────────────────────────

    [Fact]
    public void Orders_RepositoryImplementations_ShouldEndWith_Repository()
    {
        var result = Types.InAssembly(TestAssemblies.OrdersInfrastructure)
            .That().AreClasses()
            .And().ImplementInterface(typeof(Orders.Domain.Repositories.IOrderRepository))
            .Should().HaveNameEndingWith("Repository")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"Repository implementations must end with 'Repository'.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    [Fact]
    public void Accounting_RepositoryImplementations_ShouldEndWith_Repository()
    {
        var result = Types.InAssembly(TestAssemblies.AccountingInfrastructure)
            .That().AreClasses()
            .And().ImplementInterface(typeof(Accounting.Domain.Repositories.ILedgerRepository))
            .Should().HaveNameEndingWith("Repository")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"Repository implementations must end with 'Repository'.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    // ── Exception naming ────────────────────────────────────────────────────────

    [Fact]
    public void Exceptions_ShouldEndWith_Exception()
    {
        var result = Types.InAssembly(TestAssemblies.BuildingBlocksPersistence)
            .That().Inherit(typeof(Exception))
            .Should().HaveNameEndingWith("Exception")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"All exception classes must end with 'Exception'.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }
}
