# Payment Gateway Integration — Developer Guide

> **Audience:** Developers integrating a new payment provider or understanding the gateway flow  
> **Version:** 1.0 | February 2026

---

## Table of Contents

1. [Overview](#1-overview)
2. [Gateway vs Processor — Terminology](#2-gateway-vs-processor--terminology)
3. [Architecture of the Payment Layer](#3-architecture-of-the-payment-layer)
4. [The IPaymentGateway Interface](#4-the-ipaymentgateway-interface)
5. [Payment Flow — Step by Step](#5-payment-flow--step-by-step)
6. [Implementing a New Payment Provider](#6-implementing-a-new-payment-provider)
7. [Webhook Handling](#7-webhook-handling)
8. [Error Handling & Failure Modes](#8-error-handling--failure-modes)
9. [Idempotency](#9-idempotency)
10. [Testing Your Integration](#10-testing-your-integration)
11. [Security Checklist](#11-security-checklist)
12. [Code Reference Map](#12-code-reference-map)

---

## 1. Overview

The payment layer sits between the **Orders service** (business intent) and **external payment providers** (Stripe, Adyen, etc.). Its job is to:

- Accept payment commands from Orders
- Translate them into provider-specific API calls
- Normalize provider responses back into domain events
- Ensure no payment is lost, duplicated, or silently corrupted

The key design principle: **the domain never knows which provider is being used**. All provider-specific code lives behind the `IPaymentGateway` adapter.

---

## 2. Gateway vs Processor — Terminology

| Term | Role | Example |
|------|------|---------|
| **Payment Gateway** | Software interface between merchant and acquirer. Routes transaction data, handles encryption, tokenization, and API translation. | Stripe API, Adyen API, Braintree |
| **Payment Processor** | Financial entity that actually moves money between banks. Communicates with card networks (Visa, Mastercard). | First Data, Worldpay, Chase Paymentech |
| **Payment Provider** | In our system, this is the combined service we integrate with. Most modern providers (Stripe, Adyen) act as both gateway AND processor. | Stripe (gateway + processor) |

### How They Interact

```
Customer → Our System → Payment Gateway API → Payment Processor → Card Network → Issuing Bank
                             (Stripe)          (Stripe/acquirer)    (Visa)      (Customer's bank)
```

**In this codebase**, `IPaymentGateway` represents our contract with whatever payment provider we use. The distinction between gateway and processor is abstracted away.

---

## 3. Architecture of the Payment Layer

```
┌─────────────────────────────────────────────────────────────┐
│  Orders Service                                             │
│  └── CreateOrderCommandHandler                              │
│       └── Sends: AuthorizePaymentCommand → RabbitMQ         │
└─────────────────────────────────────────────────────────────┘
                            │
                    ┌───────▼───────┐
                    │   RabbitMQ    │
                    └───────┬───────┘
                            │
┌───────────────────────────▼─────────────────────────────────┐
│  Payments Service                                           │
│                                                             │
│  ┌─────────────────────────────────────────────────┐        │
│  │  Application Layer                              │        │
│  │  ├── AuthorizePaymentCommandHandler             │        │
│  │  ├── CapturePaymentCommandHandler               │        │
│  │  └── CancelPaymentCommandHandler                │        │
│  └────────────────────┬────────────────────────────┘        │
│                       │ calls                               │
│  ┌────────────────────▼────────────────────────────┐        │
│  │  Domain Layer                                   │        │
│  │  ├── Payment (Aggregate Root)                   │        │
│  │  ├── IPaymentGateway (Interface)                │        │
│  │  └── PaymentStatus (Value Object)               │        │
│  └────────────────────┬────────────────────────────┘        │
│                       │ implemented by                      │
│  ┌────────────────────▼────────────────────────────┐        │
│  │  Infrastructure Layer                           │        │
│  │  ├── SimulatedPaymentGateway (dev/test)         │        │
│  │  ├── StripePaymentGateway (production)          │        │
│  │  └── AdyenPaymentGateway (production)           │        │
│  └─────────────────────────────────────────────────┘        │
│                                                             │
│  Domain Events → Outbox → RabbitMQ → Accounting/Orders      │
└─────────────────────────────────────────────────────────────┘
```

---

## 4. The IPaymentGateway Interface

This is the contract every payment provider must implement:

```csharp
public interface IPaymentGateway
{
    // Reserve funds on the customer's payment
    Task<GatewayAuthorizationResult> AuthorizeAsync(
        GatewayAuthorizationRequest request,
        CancellationToken cancellationToken = default);

    // Transfer previously authorized funds
    Task<GatewayCaptureResult> CaptureAsync(
        GatewayCaptureRequest request,
        CancellationToken cancellationToken = default);

    // Return previously captured funds
    Task<GatewayRefundResult> RefundAsync(
        GatewayRefundRequest request,
        CancellationToken cancellationToken = default);

    // Process incoming webhook from provider
    Task<GatewayWebhookResult> HandleWebhookAsync(
        string payload, string signature,
        CancellationToken cancellationToken = default);
}
```

### Request/Response Records

```csharp
// Authorization
record GatewayAuthorizationRequest(string IdempotencyKey, decimal Amount, string Currency);
record GatewayAuthorizationResult(bool Success, string? TransactionId, string? ErrorCode, string? ErrorMessage);

// Capture
record GatewayCaptureRequest(string TransactionId, decimal Amount);
record GatewayCaptureResult(bool Success, string? ErrorCode, string? ErrorMessage);

// Refund
record GatewayRefundRequest(string TransactionId, decimal Amount);
record GatewayRefundResult(bool Success, string? RefundId, string? ErrorCode, string? ErrorMessage);

// Webhook
record GatewayWebhookResult(string EventType, string TransactionId, Dictionary<string, string> Metadata);
```

**Key design decision:** These records use simple, provider-agnostic types. The adapter is responsible for mapping provider-specific models (e.g., Stripe's `PaymentIntent`) to these records.

---

## 5. Payment Flow — Step by Step

### Authorization Flow

```
┌──────────┐     AuthorizePaymentCommand      ┌──────────────────────────────────────────────┐
│  Orders  ├─────────────────────────────────►│  AuthorizePaymentCommandHandler              │
│  Service │                                  │                                              │
└──────────┘                                  │  1. Check idempotency (existing payment?)    │
                                              │  2. Payment.Create(orderId, amount, etc.)    │
                                              │  3. paymentGateway.AuthorizeAsync(request)   │
                                              │  4a. Success → payment.MarkAuthorized(txnId) │
                                              │  4b. Failure → payment.MarkFailed(error)     │
                                              │  5. paymentRepository.Add(payment)           │
                                              │  6. unitOfWork.SaveChanges()                 │
                                              │     ↓ (saves Payment + OutboxMessage)        │
                                              │  7. OutboxDispatcher publishes event          │
                                              └──────────────────────────────────────────────┘
                                                                    │
                                              ┌─────────────────────▼──────────────────────┐
                                              │  PaymentAuthorizedEvent  OR                │
                                              │  PaymentFailedEvent      → RabbitMQ        │
                                              └────────────────────────────────────────────┘
```

### Capture Flow

```
┌──────────┐     CapturePaymentCommand        ┌──────────────────────────────────────────────┐
│  Orders  ├─────────────────────────────────►│  CapturePaymentCommandHandler                │
│  Service │                                  │                                              │
└──────────┘                                  │  1. Load payment by ID                       │
                                              │  2. Verify status == Authorized              │
                                              │  3. paymentGateway.CaptureAsync(request)     │
                                              │  4a. Success → payment.MarkCaptured()        │
                                              │  4b. Failure → payment.MarkFailed(error)     │
                                              │  5. paymentRepository.Update(payment)        │
                                              │  6. unitOfWork.SaveChanges()                 │
                                              └──────────────────────────────────────────────┘
                                                                    │
                                              ┌─────────────────────▼──────────────────────┐
                                              │  PaymentCapturedEvent → Orders + Accounting │
                                              └────────────────────────────────────────────┘
```

---

## 6. Implementing a New Payment Provider

### Step-by-Step Guide

**Step 1: Create the adapter class**

```
src/Services/Payments/src/Payments.Infrastructure/Gateways/StripePaymentGateway.cs
```

```csharp
using Payments.Domain.Gateways;
using Stripe;

namespace Payments.Infrastructure.Gateways;

public class StripePaymentGateway(
    PaymentIntentService stripeService,
    ILogger<StripePaymentGateway> logger) : IPaymentGateway
{
    public async Task<GatewayAuthorizationResult> AuthorizeAsync(
        GatewayAuthorizationRequest request, CancellationToken ct)
    {
        try
        {
            var options = new PaymentIntentCreateOptions
            {
                Amount = (long)(request.Amount * 100), // Stripe uses cents
                Currency = request.Currency.ToLower(),
                CaptureMethod = "manual", // Auth only, capture later
                IdempotencyKey = request.IdempotencyKey
            };

            var intent = await stripeService.CreateAsync(options, cancellationToken: ct);

            return new GatewayAuthorizationResult(
                Success: intent.Status == "requires_capture",
                TransactionId: intent.Id,
                ErrorCode: null,
                ErrorMessage: null);
        }
        catch (StripeException ex)
        {
            return new GatewayAuthorizationResult(
                Success: false,
                TransactionId: null,
                ErrorCode: ex.StripeError?.Code ?? "STRIPE_ERROR",
                ErrorMessage: ex.StripeError?.Message ?? ex.Message);
        }
    }

    public async Task<GatewayCaptureResult> CaptureAsync(
        GatewayCaptureRequest request, CancellationToken ct)
    {
        try
        {
            var intent = await stripeService.CaptureAsync(
                request.TransactionId,
                new PaymentIntentCaptureOptions
                {
                    AmountToCapture = (long)(request.Amount * 100)
                },
                cancellationToken: ct);

            return new GatewayCaptureResult(
                Success: intent.Status == "succeeded",
                ErrorCode: null,
                ErrorMessage: null);
        }
        catch (StripeException ex)
        {
            return new GatewayCaptureResult(
                Success: false,
                ErrorCode: ex.StripeError?.Code ?? "CAPTURE_ERROR",
                ErrorMessage: ex.StripeError?.Message ?? ex.Message);
        }
    }

    public async Task<GatewayRefundResult> RefundAsync(
        GatewayRefundRequest request, CancellationToken ct)
    {
        // Map to Stripe Refund API...
    }

    public async Task<GatewayWebhookResult> HandleWebhookAsync(
        string payload, string signature, CancellationToken ct)
    {
        // CRITICAL: Validate webhook signature first!
        var stripeEvent = EventUtility.ConstructEvent(
            payload, signature, _webhookSecret);

        return new GatewayWebhookResult(
            EventType: stripeEvent.Type,
            TransactionId: /* extract from event */,
            Metadata: /* extract relevant metadata */);
    }
}
```

**Step 2: Register in DI (Program.cs)**

```csharp
// Development
builder.Services.AddSingleton<IPaymentGateway, SimulatedPaymentGateway>();

// Production
builder.Services.AddSingleton<IPaymentGateway, StripePaymentGateway>();
```

**Step 3: Configure credentials**

```json
// appsettings.json (or environment variables)
{
  "Stripe": {
    "SecretKey": "sk_live_...",
    "WebhookSecret": "whsec_..."
  }
}
```

**What does NOT change:**
- ❌ Domain layer (Payment aggregate, PaymentStatus)
- ❌ Application layer (command handlers)
- ❌ Contracts (commands, events)
- ❌ Other services (Orders, Accounting)

**What changes:**
- ✅ New `IPaymentGateway` implementation in Infrastructure
- ✅ DI registration in Program.cs
- ✅ Provider-specific configuration

---

## 7. Webhook Handling

Payment providers send asynchronous notifications (webhooks) for events like settlements, disputes, and chargebacks.

### Webhook Security

```
Provider → HTTPS POST → Our Webhook Endpoint → HandleWebhookAsync()
```

1. **Signature validation** is the adapter's responsibility
2. The domain trusts that `HandleWebhookAsync` only passes validated data
3. Never trust client-side payment status — only webhooks finalize payments

### Implementation Pattern

```csharp
public async Task<GatewayWebhookResult> HandleWebhookAsync(
    string payload, string signature, CancellationToken ct)
{
    // 1. Validate signature (CRITICAL - prevents spoofing)
    ValidateSignature(payload, signature);  // throws on invalid

    // 2. Parse the event
    var webhookEvent = ParseEvent(payload);

    // 3. Map to our result model
    return new GatewayWebhookResult(
        EventType: webhookEvent.Type,       // e.g., "payment.settled"
        TransactionId: webhookEvent.TxnId,
        Metadata: webhookEvent.Data);
}
```

---

## 8. Error Handling & Failure Modes

### Provider Error Categories

| Category | Example | System Response |
|----------|---------|-----------------|
| **Business Decline** | Insufficient funds | `Payment.MarkFailed()` → `PaymentFailedEvent` |
| **Network Timeout** | Provider unreachable | Caught as exception → `Payment.MarkFailed()` |
| **Invalid Request** | Bad currency code | `DomainException` before gateway call |
| **Rate Limiting** | Too many requests | Polly retry with exponential backoff |
| **Provider Outage** | 500 errors | Circuit breaker opens, fallback to alternate provider |

### Error Flow in Code

```csharp
try
{
    var result = await paymentGateway.AuthorizeAsync(request, ct);

    if (result.Success && result.TransactionId is not null)
    {
        payment.MarkAuthorized(result.TransactionId);
    }
    else
    {
        // Business decline — provider said "no"
        payment.MarkFailed(
            result.ErrorMessage ?? "Authorization declined",
            result.ErrorCode ?? "PROVIDER_DECLINE");
    }
}
catch (Exception ex)
{
    // Infrastructure failure — provider crashed/timed out
    payment.MarkFailed($"Provider error: {ex.Message}", "PROVIDER_ERROR");
}

// Either way, save and emit event via outbox
await paymentRepository.AddAsync(payment, ct);
await unitOfWork.SaveChangesAsync(ct);
```

**Key principle:** No payment is left in an ambiguous state. Every outcome (success, decline, crash) results in an explicit domain event.

---

## 9. Idempotency

### Why It Matters for Payment Integrations

Network failures can cause duplicate requests:
```
Client → Our API (timeout) → Client retries → Double charge!
```

### How We Prevent It

1. **Our side:** Every command carries an `IdempotencyKey`. Before processing, we check if a payment with that key already exists.

2. **Provider side:** We pass the idempotency key to the provider (e.g., Stripe's `IdempotencyKey` option). The provider itself will deduplicate.

3. **Event side:** The Accounting service checks for existing ledger entries by `PaymentId` before creating new ones.

```csharp
// In AuthorizePaymentCommandHandler
var existing = await paymentRepository
    .GetByIdempotencyKeyAsync(command.IdempotencyKey, ct);
if (existing is not null)
{
    logger.LogInformation("Duplicate detected, skipping");
    return; // No double charge
}
```

---

## 10. Testing Your Integration

### Using the Simulated Gateway

The `SimulatedPaymentGateway` provides predictable test scenarios:

| Input | Behavior | Use For Testing |
|-------|----------|-----------------|
| Amount ending in `.99` | Declined | Failure handling flow |
| Amount > 10,000 | Timeout exception | Timeout resilience |
| Normal amount | Success | Happy path |
| Any capture | 5% random failure | Retry/failure recovery |

### End-to-End Test Flow

```
1. POST /orders → Create order ($50.00 USD)
2. Verify: AuthorizePaymentCommand sent to Payments
3. Verify: PaymentAuthorizedEvent received by Orders
4. POST /orders/{id}/confirm → Trigger capture
5. Verify: CapturePaymentCommand sent to Payments
6. Verify: PaymentCapturedEvent received by Orders and Accounting
7. Verify: LedgerEntry pair created (Debit CustomerReceivable / Credit Revenue)
```

### Testing Failure Scenarios

```
1. POST /orders → Create order ($19.99 USD)  // .99 triggers decline
2. Verify: PaymentFailedEvent with code "INSUFFICIENT_FUNDS"
3. Verify: Order status → Failed
```

---

## 11. Security Checklist

When integrating a new provider, verify:

- [ ] **Webhook signatures are validated** before processing any webhook data
- [ ] **No card data is stored** — only provider transaction references
- [ ] **API keys are loaded from environment variables**, not config files
- [ ] **HTTPS is used** for all provider API calls
- [ ] **Idempotency keys are passed** to the provider API
- [ ] **Error responses don't leak** provider internals to clients
- [ ] **PCI DSS scope is minimized** — tokenization is the provider's responsibility
- [ ] **Rate limiting** is respected with appropriate backoff

---

## 12. Code Reference Map

| What | Where |
|------|-------|
| Gateway interface | `Payments.Domain/Gateways/IPaymentGateway.cs` |
| Simulated adapter | `Payments.Infrastructure/Gateways/SimulatedPaymentGateway.cs` |
| Payment aggregate | `Payments.Domain/Aggregates/Payment.cs` |
| Payment states | `Payments.Domain/ValueObjects/PaymentStatus.cs` |
| Authorization handler | `Payments.Application/CommandHandlers/AuthorizePaymentCommandHandler.cs` |
| Capture handler | `Payments.Application/CommandHandlers/CapturePaymentCommandHandler.cs` |
| Authorize command | `BuildingBlocks/Contracts/Commands/AuthorizePaymentCommand.cs` |
| Capture command | `BuildingBlocks/Contracts/Commands/CapturePaymentCommand.cs` |
| Payment events | `BuildingBlocks/Contracts/Events/Payment*.cs` |
| Outbox dispatcher | `BuildingBlocks/Messaging/OutboxDispatcher.cs` |
| Outbox message | `BuildingBlocks/Persistence/OutboxMessage.cs` |
| Accounting handler | `Accounting.Application/EventHandlers/PaymentCapturedEventHandler.cs` |
| Ledger entry | `Accounting.Domain/Entities/LedgerEntry.cs` |
