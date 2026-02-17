using System.Text.Json;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Background service that polls the outbox table for unpublished messages
/// and dispatches them to RabbitMQ via IEventBus.
///
/// This is the second half of the Outbox Pattern:
///  1. SaveChangesAsync stores domain events as OutboxMessages (atomic with state)
///  2. This dispatcher reads unpublished messages and publishes them
///  3. On success, marks ProcessedOn timestamp
///  4. On failure, increments retry counter and logs error
///
/// Why a poller instead of CDC? Simplicity. For production scale, consider
/// Debezium or PostgreSQL LISTEN/NOTIFY for lower latency.
///
/// The dispatcher processes messages in order of OccurredOn to preserve causality.
/// </summary>
public class OutboxDispatcher<TContext>(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxDispatcher<TContext>> logger) : BackgroundService
    where TContext : OutboxDbContext
{
    private const int BatchSize = 50;
    private const int MaxRetries = 5;
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox dispatcher started for {Context}", typeof(TContext).Name);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOutboxMessages(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error processing outbox messages");
            }

            await Task.Delay(PollingInterval, stoppingToken);
        }
    }

    private async Task ProcessOutboxMessages(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TContext>();
        var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        var messages = await dbContext.OutboxMessages
            .Where(m => m.ProcessedOn == null && m.Retries < MaxRetries)
            .OrderBy(m => m.OccurredOn)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            try
            {
                var eventType = Type.GetType(message.Type);
                if (eventType is null)
                {
                    logger.LogWarning("Cannot resolve type {Type} for outbox message {Id}", message.Type, message.Id);
                    message.Retries = MaxRetries; // Poison message — stop retrying
                    message.Error = $"Cannot resolve type: {message.Type}";
                    await dbContext.SaveChangesAsync(cancellationToken);
                    continue;
                }

                var domainEvent = JsonSerializer.Deserialize(message.Payload, eventType);
                if (domainEvent is null)
                {
                    logger.LogWarning("Failed to deserialize outbox message {Id}", message.Id);
                    message.Retries = MaxRetries;
                    message.Error = "Deserialization returned null";
                    await dbContext.SaveChangesAsync(cancellationToken);
                    continue;
                }

                await eventBus.PublishAsync(domainEvent, cancellationToken);

                message.ProcessedOn = DateTime.UtcNow;

                logger.LogInformation(
                    "Published outbox message {Id} of type {Type}",
                    message.Id, eventType.Name);
            }
            catch (Exception ex)
            {
                message.Retries++;
                message.Error = ex.Message;

                logger.LogWarning(ex,
                    "Failed to publish outbox message {Id}, retry {Retry}/{MaxRetries}",
                    message.Id, message.Retries, MaxRetries);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
