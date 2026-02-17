namespace BuildingBlocks.Persistence;

/// <summary>
/// Tracks processed commands for idempotency.
/// When a command arrives, we check if its IdempotencyKey exists in this table.
/// If yes, we return the stored response without re-executing.
/// This prevents double-charging customers on network retries.
/// </summary>
public class ProcessedCommand
{
    public string Key { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
