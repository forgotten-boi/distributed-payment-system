using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Architecture.Tests.Conventions;

/// <summary>
/// Verifies the purity of the BuildingBlocks shared libraries.
///
/// BuildingBlocks.Contracts is the shared message-contract library consumed by
/// all services. Its purity is critical: any framework dependency pulled in here
/// transitively affects every service in the solution.
///
/// BuildingBlocks.Persistence defines domain primitives (AggregateRoot, Entity,
/// IDomainEvent). It may reference EF Core for OutboxDbContext but must not
/// depend on Application or Infrastructure namespaces.
/// </summary>
public class BuildingBlocksTests
{
    // ── Contracts: zero framework dependencies ───────────────────────────────────

    [Fact]
    public void Contracts_ShouldNotDependOn_EntityFrameworkCore()
    {
        var result = Types.InAssembly(TestAssemblies.BuildingBlocksContracts)
            .ShouldNot().HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"BuildingBlocks.Contracts must have zero framework dependencies — it is a pure message-contract library.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    [Fact]
    public void Contracts_ShouldNotDependOn_MassTransit()
    {
        var result = Types.InAssembly(TestAssemblies.BuildingBlocksContracts)
            .ShouldNot().HaveDependencyOn("MassTransit")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"BuildingBlocks.Contracts must not depend on MassTransit — contract records must be framework-agnostic.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    [Fact]
    public void Contracts_ShouldNotDependOn_MediatR()
    {
        var result = Types.InAssembly(TestAssemblies.BuildingBlocksContracts)
            .ShouldNot().HaveDependencyOn("MediatR")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"BuildingBlocks.Contracts must not depend on MediatR.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    [Fact]
    public void Contracts_ShouldNotDependOn_AnyServiceLayer()
    {
        var result = Types.InAssembly(TestAssemblies.BuildingBlocksContracts)
            .ShouldNot().HaveDependencyOnAny(
                "Orders.Domain", "Orders.Application", "Orders.Infrastructure",
                "Payments.Domain", "Payments.Application", "Payments.Infrastructure",
                "Accounting.Domain", "Accounting.Application", "Accounting.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"BuildingBlocks.Contracts must not depend on any service layer — services depend on it, not the reverse.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    // ── Contracts: integration commands end with Command or Requested ────────────

    [Fact]
    public void Contracts_Commands_ShouldEndWith_CommandOrRequested()
    {
        var result = Types.InAssembly(TestAssemblies.BuildingBlocksContracts)
            .That().ResideInNamespace("BuildingBlocks.Contracts.Commands")
            .Should().HaveNameEndingWith("Command").Or().HaveNameEndingWith("Requested")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"Integration command contracts must end with 'Command' or 'Requested'.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    [Fact]
    public void Contracts_Events_ShouldEndWith_EventOrChanged()
    {
        var result = Types.InAssembly(TestAssemblies.BuildingBlocksContracts)
            .That().ResideInNamespace("BuildingBlocks.Contracts.Events")
            .Should().HaveNameEndingWith("Event").Or().HaveNameEndingWith("Changed").Or().HaveNameEndingWith("Expired")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"Integration event contracts must end with 'Event', 'Changed', or 'Expired'.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }

    // ── Persistence: no service-layer coupling ───────────────────────────────────

    [Fact]
    public void Persistence_ShouldNotDependOn_AnyServiceLayer()
    {
        var result = Types.InAssembly(TestAssemblies.BuildingBlocksPersistence)
            .ShouldNot().HaveDependencyOnAny(
                "Orders", "Payments", "Accounting")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"BuildingBlocks.Persistence must not couple to any specific service.\n  Failing: {TestAssemblies.FormatFailures(result)}");
    }
}
