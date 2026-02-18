namespace WebUI.Services;

/// <summary>
/// In-memory store that tracks orders created during this UI session.
/// In a real app this would query a "list orders" API endpoint.
/// Since the backend only has GET by ID (no list endpoint),
/// we track locally to enable the dashboard and order list views.
/// </summary>
public sealed class OrderTracker
{
    private readonly List<TrackedOrder> _orders = [];
    private readonly Lock _lock = new();

    public void Track(TrackedOrder order)
    {
        lock (_lock)
        {
            var existing = _orders.FindIndex(o => o.OrderId == order.OrderId);
            if (existing >= 0)
                _orders[existing] = order;
            else
                _orders.Add(order);
        }
    }

    public void Update(Guid orderId, Action<TrackedOrder> mutate)
    {
        lock (_lock)
        {
            var order = _orders.Find(o => o.OrderId == orderId);
            if (order is not null)
                mutate(order);
        }
    }

    public List<TrackedOrder> GetAll()
    {
        lock (_lock)
        {
            return [.. _orders.OrderByDescending(o => o.CreatedAt)];
        }
    }

    public TrackedOrder? Get(Guid orderId)
    {
        lock (_lock)
        {
            return _orders.Find(o => o.OrderId == orderId);
        }
    }
}

public sealed class TrackedOrder
{
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string Status { get; set; } = "Unknown";
    public Guid? PaymentId { get; set; }
    public string? FailureReason { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
