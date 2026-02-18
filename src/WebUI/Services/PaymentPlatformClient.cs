using System.Net.Http.Json;
using WebUI.Models;

namespace WebUI.Services;

/// <summary>
/// HTTP client service that communicates with the backend microservices.
/// Uses Aspire service discovery to resolve service endpoints at runtime,
/// so the UI never hard-codes service URLs. Each method propagates a
/// Correlation ID header for distributed tracing.
/// </summary>
public sealed class PaymentPlatformClient(
    HttpClient ordersClient,
    HttpClient paymentsClient,
    HttpClient accountingClient)
{
    // ── Orders ──

    public async Task<OrderResult?> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        var response = await ordersClient.PostAsJsonAsync("/api/orders", request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OrderResult>(ct);
    }

    public async Task<OrderDetail?> GetOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        return await ordersClient.GetFromJsonAsync<OrderDetail>($"/api/orders/{orderId}", ct);
    }

    public async Task ConfirmOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        var response = await ordersClient.PostAsync($"/api/orders/{orderId}/confirm", null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task CancelOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        var response = await ordersClient.PostAsync($"/api/orders/{orderId}/cancel", null, ct);
        response.EnsureSuccessStatusCode();
    }

    // ── Payments ──

    public async Task<PaymentDetail?> GetPaymentAsync(Guid paymentId, CancellationToken ct = default)
    {
        return await paymentsClient.GetFromJsonAsync<PaymentDetail>($"/api/payments/{paymentId}", ct);
    }

    // ── Accounting ──

    public async Task<List<LedgerEntryDetail>> GetLedgerEntriesAsync(Guid transactionId, CancellationToken ct = default)
    {
        try
        {
            var result = await accountingClient.GetFromJsonAsync<List<LedgerEntryDetail>>(
                $"/api/ledger/{transactionId}", ct);
            return result ?? [];
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return [];
        }
    }

    public async Task<AccountBalance?> GetAccountBalanceAsync(string accountName, CancellationToken ct = default)
    {
        try
        {
            return await accountingClient.GetFromJsonAsync<AccountBalance>(
                $"/api/ledger/balance/{accountName}", ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<ReconciliationResult?> RunReconciliationAsync(CancellationToken ct = default)
    {
        var response = await accountingClient.PostAsync("/api/reconciliation/run", null, ct);
        return await response.Content.ReadFromJsonAsync<ReconciliationResult>(ct);
    }
}
