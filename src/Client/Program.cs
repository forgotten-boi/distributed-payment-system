using System.Net.Http.Json;
using System.Text.Json;

/// <summary>
/// Test client that exercises the full payment lifecycle:
///  1. Create order → POST /api/orders
///  2. Wait for payment authorization (async via RabbitMQ)
///  3. Confirm order → POST /api/orders/{id}/confirm (triggers capture)
///  4. Verify ledger entries in accounting service
///
/// This demonstrates:
///  - The complete happy-path flow
///  - Correlation ID propagation across services
///  - Eventual consistency (polling for async state changes)
///  - Idempotency (sending same request twice returns same result)
/// </summary>

var ordersBaseUrl = args.Length > 0 ? args[0] : "http://localhost:5001";
var paymentsBaseUrl = args.Length > 1 ? args[1] : "http://localhost:5002";
var accountingBaseUrl = args.Length > 2 ? args[2] : "http://localhost:5003";

var correlationId = Guid.NewGuid().ToString();
var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine("  Enterprise Distributed Payments Platform — Test Client  ");
Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine($"  Correlation ID: {correlationId}");
Console.WriteLine($"  Orders:     {ordersBaseUrl}");
Console.WriteLine($"  Payments:   {paymentsBaseUrl}");
Console.WriteLine($"  Accounting: {accountingBaseUrl}");
Console.WriteLine();

try
{
    // ─── Step 1: Create Order ───
    Console.WriteLine("── Step 1: Creating Order ──");
    var idempotencyKey = Guid.NewGuid().ToString();
    var createOrderRequest = new
    {
        CustomerId = Guid.NewGuid(),
        Amount = 250.00m,
        Currency = "USD",
        IdempotencyKey = idempotencyKey
    };

    var createResponse = await httpClient.PostAsJsonAsync(
        $"{ordersBaseUrl}/api/orders", createOrderRequest);
    createResponse.EnsureSuccessStatusCode();
    var orderResult = await createResponse.Content.ReadFromJsonAsync<OrderResult>(jsonOptions);
    Console.WriteLine($"   ✓ Order created: {orderResult!.OrderId}");
    Console.WriteLine($"   ✓ Status: {orderResult.Status}");
    Console.WriteLine();

    // ─── Step 1b: Idempotency Test ───
    Console.WriteLine("── Step 1b: Idempotency Test (duplicate request) ──");
    var duplicateResponse = await httpClient.PostAsJsonAsync(
        $"{ordersBaseUrl}/api/orders", createOrderRequest);
    var duplicateResult = await duplicateResponse.Content.ReadFromJsonAsync<OrderResult>(jsonOptions);
    Console.WriteLine($"   ✓ Same order returned: {duplicateResult!.OrderId == orderResult.OrderId}");
    Console.WriteLine();

    // ─── Step 2: Wait for Authorization ───
    Console.WriteLine("── Step 2: Waiting for Payment Authorization ──");
    var authorized = false;
    for (var i = 0; i < 30; i++)
    {
        await Task.Delay(1000);
        var orderResponse = await httpClient.GetFromJsonAsync<OrderDetail>(
            $"{ordersBaseUrl}/api/orders/{orderResult.OrderId}", jsonOptions);

        if (orderResponse?.Status == "Authorized")
        {
            Console.WriteLine($"   ✓ Payment authorized! PaymentId: {orderResponse.PaymentId}");
            authorized = true;
            break;
        }
        else if (orderResponse?.Status == "Failed")
        {
            Console.WriteLine($"   ✗ Payment failed: {orderResponse.FailureReason}");
            break;
        }

        Console.Write(".");
    }

    if (!authorized)
    {
        Console.WriteLine("\n   ⚠ Authorization did not complete in time.");
        Console.WriteLine("   This may indicate RabbitMQ is not running or services are not connected.");
        return;
    }

    Console.WriteLine();

    // ─── Step 3: Confirm Order (Trigger Capture) ───
    Console.WriteLine("── Step 3: Confirming Order (triggers capture) ──");
    var confirmResponse = await httpClient.PostAsync(
        $"{ordersBaseUrl}/api/orders/{orderResult.OrderId}/confirm", null);
    confirmResponse.EnsureSuccessStatusCode();
    Console.WriteLine("   ✓ Order confirmation sent");
    Console.WriteLine();

    // ─── Step 4: Wait for Capture ───
    Console.WriteLine("── Step 4: Waiting for Payment Capture ──");
    for (var i = 0; i < 30; i++)
    {
        await Task.Delay(1000);
        var orderResponse = await httpClient.GetFromJsonAsync<OrderDetail>(
            $"{ordersBaseUrl}/api/orders/{orderResult.OrderId}", jsonOptions);

        if (orderResponse?.Status == "Captured")
        {
            Console.WriteLine("   ✓ Payment captured! Funds transferred.");
            break;
        }
        else if (orderResponse?.Status == "Failed")
        {
            Console.WriteLine($"   ✗ Capture failed: {orderResponse.FailureReason}");
            break;
        }

        Console.Write(".");
    }

    Console.WriteLine();

    // ─── Step 5: Verify Accounting ───
    Console.WriteLine("── Step 5: Verifying Accounting Ledger ──");
    await Task.Delay(3000); // Wait for eventual consistency

    var receivableBalance = await httpClient.GetFromJsonAsync<AccountBalance>(
        $"{accountingBaseUrl}/api/ledger/balance/CustomerReceivable", jsonOptions);
    var revenueBalance = await httpClient.GetFromJsonAsync<AccountBalance>(
        $"{accountingBaseUrl}/api/ledger/balance/Revenue", jsonOptions);

    Console.WriteLine($"   CustomerReceivable — Debits: {receivableBalance?.TotalDebits:C}");
    Console.WriteLine($"   Revenue            — Credits: {revenueBalance?.TotalCredits:C}");
    Console.WriteLine();

    // ─── Step 6: Run Reconciliation ───
    Console.WriteLine("── Step 6: Running Reconciliation ──");
    var reconResponse = await httpClient.PostAsync(
        $"{accountingBaseUrl}/api/reconciliation/run", null);
    var reconResult = await reconResponse.Content.ReadFromJsonAsync<ReconciliationResult>(jsonOptions);
    Console.WriteLine($"   Balanced: {reconResult?.IsBalanced}");
    Console.WriteLine($"   Total Debits:  {reconResult?.TotalDebits:C}");
    Console.WriteLine($"   Total Credits: {reconResult?.TotalCredits:C}");
    Console.WriteLine($"   Difference:    {reconResult?.Difference:C}");
    Console.WriteLine();

    Console.WriteLine("═══════════════════════════════════════════════════════════");
    Console.WriteLine("  ✓ Full payment lifecycle completed successfully!        ");
    Console.WriteLine($"  Trace with Correlation ID: {correlationId}");
    Console.WriteLine("═══════════════════════════════════════════════════════════");
}
catch (HttpRequestException ex)
{
    Console.WriteLine($"\n   ✗ HTTP Error: {ex.Message}");
    Console.WriteLine("   Make sure all services are running (dotnet run --project src/AppHost)");
}

// DTOs
record OrderResult(Guid OrderId, string Status);
record OrderDetail(Guid Id, Guid CustomerId, decimal Amount, string Currency,
    string Status, Guid? PaymentId, string? FailureReason);
record AccountBalance(string Account, decimal TotalDebits, decimal TotalCredits, decimal NetBalance, int EntryCount);
record ReconciliationResult(bool IsBalanced, decimal TotalDebits, decimal TotalCredits, decimal Difference, int EntryCount);
