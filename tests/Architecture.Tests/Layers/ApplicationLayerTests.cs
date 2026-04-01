using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Architecture.Tests.Layers;

/// <summary>
/// Verifies that Application layers orchestrate domain logic without coupling
/// to infrastructure concerns such as EF Core or persistence providers.
/// Application code must talk to the Domain through interfaces, never
/// directly through DbContext or concrete repositories.
/// </summary>
public class ApplicationLayerTests
{
    // ── Test data ───────────────────────────────────────────────────────────────

    public static TheoryData<Assembly, string> AllApplicationAssemblies => new()
    {
        { TestAssemblies.OrdersApplication,    "Orders.Application" },
        { TestAssemblies.PaymentsApplication,  "Payments.Application" },
        { TestAssemblies.AccountingApplication, "Accounting.Application" },
    };

    // ── No EF Core in Application (the critical rule) ───────────────────────────

    [Theory, MemberData(nameof(AllApplicationAssemblies))]
    public void Application_ShouldNotDependOn_EntityFrameworkCore(Assembly assembly, string name)
    {
        // The Application layer works exclusively through IUnitOfWork and
        // repository interfaces defined in BuildingBlocks.Persistence.
        // Direct EF Core imports indicate a layer violation.
        var result = Types.InAssembly(assembly)
            .ShouldNot().HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"[{name}] must not use EF Core — use IUnitOfWork and repository interfaces instead.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    // ── No cross-layer violation ────────────────────────────────────────────────

    [Theory, MemberData(nameof(AllApplicationAssemblies))]
    public void Application_ShouldNotDependOn_Infrastructure(Assembly assembly, string name)
    {
        var result = Types.InAssembly(assembly)
            .ShouldNot().HaveDependencyOnAny(
                "Orders.Infrastructure",
                "Payments.Infrastructure",
                "Accounting.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"[{name}] must not depend on Infrastructure — Infrastructure may only depend on Application, not the reverse.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    [Theory, MemberData(nameof(AllApplicationAssemblies))]
    public void Application_ShouldNotDependOn_ApiLayer(Assembly assembly, string name)
    {
        var result = Types.InAssembly(assembly)
            .ShouldNot().HaveDependencyOnAny(
                "Orders.Api",
                "Payments.Api",
                "Accounting.Api")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"[{name}] must not depend on the Api layer — Api is the composition root, not a dependency.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    // ── Orders-specific: CQRS handler conventions ───────────────────────────────

    [Fact]
    public void Orders_CommandHandlers_ShouldEndWith_Handler()
    {
        var result = Types.InAssembly(TestAssemblies.OrdersApplication)
            .That().ResideInNamespace("Orders.Application.Commands")
            .And().AreClasses()
            .And().HaveNameMatching(".*Handler.*")
            .Should().HaveNameEndingWith("Handler")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"Command handler classes must end with 'Handler'.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    [Fact]
    public void Orders_QueryHandlers_ShouldEndWith_Handler()
    {
        var result = Types.InAssembly(TestAssemblies.OrdersApplication)
            .That().ResideInNamespace("Orders.Application.Queries")
            .And().AreClasses()
            .And().HaveNameMatching(".*Handler.*")
            .Should().HaveNameEndingWith("Handler")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"Query handler classes must end with 'Handler'.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    [Fact]
    public void Orders_EventHandlers_ShouldEndWith_Handler()
    {
        var result = Types.InAssembly(TestAssemblies.OrdersApplication)
            .That().ResideInNamespace("Orders.Application.EventHandlers")
            .And().AreClasses()
            .Should().HaveNameEndingWith("Handler")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"Event handler classes must end with 'Handler'.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    // ── Saga layer isolation (Orders-specific) ──────────────────────────────────

    [Fact]
    public void Orders_Saga_ShouldNotDependOn_EntityFrameworkCore()
    {
        // The saga state machine is in the Application layer and must stay free
        // of EF Core. EF Core persistence for saga state is wired in Infrastructure.
        var result = Types.InAssembly(TestAssemblies.OrdersApplication)
            .That().ResideInNamespace("Orders.Application.Saga")
            .ShouldNot().HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"Saga state machine must not use EF Core — saga persistence is wired in Orders.Infrastructure.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }
}
