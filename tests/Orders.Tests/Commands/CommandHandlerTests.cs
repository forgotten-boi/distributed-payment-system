using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Orders.Application.Commands;
using Orders.Domain.Aggregates;
using Orders.Domain.Events;
using Orders.Domain.Repositories;
using Orders.Domain.ValueObjects;
using Xunit;

namespace Orders.Tests.Commands;

/// <summary>
/// Tests for the simplified command handlers that delegate orchestration to the saga.
/// </summary>
public class CreateOrderCommandHandlerTests
{
    private readonly IOrderRepository _repository = Substitute.For<IOrderRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ILogger<CreateOrderCommandHandler> _logger = Substitute.For<ILogger<CreateOrderCommandHandler>>();

    private CreateOrderCommandHandler CreateHandler() => new(_repository, _unitOfWork, _eventBus, _logger);

    [Fact]
    public async Task Handle_NewOrder_ShouldCreateOrderAndPublishDomainEvent()
    {
        _repository.GetByIdempotencyKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        var command = new CreateOrderCommand(Guid.NewGuid(), 100m, "USD", "key-123");
        var handler = CreateHandler();

        var result = await handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.Status.Should().Be("PaymentAuthorizing");

        await _repository.Received(1).AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        // Should publish OrderCreatedDomainEvent for the saga
        await _eventBus.Received(1).PublishAsync(
            Arg.Is<OrderCreatedDomainEvent>(e => e.Amount == 100m && e.Currency == "USD"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DuplicateIdempotencyKey_ShouldReturnExistingOrder()
    {
        var existingOrder = Order.Create(Guid.NewGuid(), 100m, "USD", "key-123");
        _repository.GetByIdempotencyKeyAsync("key-123", Arg.Any<CancellationToken>())
            .Returns(existingOrder);

        var command = new CreateOrderCommand(Guid.NewGuid(), 200m, "EUR", "key-123");
        var handler = CreateHandler();

        var result = await handler.Handle(command, CancellationToken.None);

        result.OrderId.Should().Be(existingOrder.Id);
        await _repository.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
        await _eventBus.DidNotReceive().PublishAsync(Arg.Any<OrderCreatedDomainEvent>(), Arg.Any<CancellationToken>());
    }
}

public class ConfirmOrderCommandHandlerTests
{
    private readonly IOrderRepository _repository = Substitute.For<IOrderRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ILogger<ConfirmOrderCommandHandler> _logger = Substitute.For<ILogger<ConfirmOrderCommandHandler>>();

    private ConfirmOrderCommandHandler CreateHandler() => new(_repository, _unitOfWork, _eventBus, _logger);

    [Fact]
    public async Task Handle_AuthorizedOrder_ShouldPublishConfirmOrderRequested()
    {
        var order = Order.Create(Guid.NewGuid(), 100m, "USD", "key");
        order.StartPaymentAuthorization();
        order.MarkAuthorized(Guid.NewGuid());

        _repository.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var handler = CreateHandler();
        var result = await handler.Handle(new ConfirmOrderCommand(order.Id), CancellationToken.None);

        result.Status.Should().Be("Capturing");
        await _eventBus.Received(1).PublishAsync(
            Arg.Is<BuildingBlocks.Contracts.Commands.ConfirmOrderRequested>(e => e.OrderId == order.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NonExistentOrder_ShouldThrow()
    {
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Order?)null);

        var handler = CreateHandler();

        var act = () => handler.Handle(new ConfirmOrderCommand(Guid.NewGuid()), CancellationToken.None);
        await act.Should().ThrowAsync<BuildingBlocks.Exceptions.DomainException>();
    }
}

public class CancelOrderCommandHandlerTests
{
    private readonly IOrderRepository _repository = Substitute.For<IOrderRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ILogger<CancelOrderCommandHandler> _logger = Substitute.For<ILogger<CancelOrderCommandHandler>>();

    private CancelOrderCommandHandler CreateHandler() => new(_repository, _unitOfWork, _eventBus, _logger);

    [Fact]
    public async Task Handle_ActiveOrder_ShouldPublishCancelOrderRequested()
    {
        var order = Order.Create(Guid.NewGuid(), 100m, "USD", "key");
        _repository.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var handler = CreateHandler();
        var result = await handler.Handle(new CancelOrderCommand(order.Id), CancellationToken.None);

        result.Status.Should().Be("Cancelled");
        await _eventBus.Received(1).PublishAsync(
            Arg.Is<BuildingBlocks.Contracts.Commands.CancelOrderRequested>(e => e.OrderId == order.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CapturedOrder_ShouldThrow()
    {
        var order = Order.Create(Guid.NewGuid(), 100m, "USD", "key");
        order.StartPaymentAuthorization();
        order.MarkAuthorized(Guid.NewGuid());
        order.StartCapture();
        order.MarkCaptured();

        _repository.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var handler = CreateHandler();
        var act = () => handler.Handle(new CancelOrderCommand(order.Id), CancellationToken.None);
        await act.Should().ThrowAsync<BuildingBlocks.Exceptions.DomainException>();
    }
}
