# Enterprise Distributed Payments Platform — Architecture Document

> **Version:** 1.0  
> **Date:** February 2026  
> **Stack:** .NET 10, ASP.NET Core, .NET Aspire, RabbitMQ, PostgreSQL, MassTransit

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Architecture Overview](#2-architecture-overview)
3. [Clean Architecture Layers](#3-clean-architecture-layers)
4. [Service Decomposition](#4-service-decomposition)
5. [Payment Lifecycle](#5-payment-lifecycle)
6. [Event-Driven Communication](#6-event-driven-communication)
7. [Outbox Pattern](#7-outbox-pattern)
8. [Payment Gateway Abstraction](#8-payment-gateway-abstraction)
9. [Idempotency & Data Integrity](#9-idempotency--data-integrity)
10. [Double-Entry Accounting](#10-double-entry-accounting)
11. [Resilience & Error Handling](#11-resilience--error-handling)
12. [Observability](#12-observability)
13. [Security](#13-security)
14. [Database Strategy](#14-database-strategy)
15. [Technology Stack](#15-technology-stack)
16. [Repository Structure](#16-repository-structure)
17. [Running the System](#17-running-the-system)
18. [Future Enhancements](#18-future-enhancements)

---

## 1. Executive Summary

This platform is a **production-grade distributed payment processing system** built with .NET and .NET Aspire. It models a real financial lifecycle — **not** simple CRUD microservices.

The core payment flow covers:

```
Authorization → Capture → Settlement → Reconciliation → Accounting → Failure Recovery
```

**Key Guarantees:**
- Payments can never be lost, duplicated, or silently corrupted
- Every financial action produces an audit trail via double-entry accounting
- All services communicate asynchronously — no service directly queries another service's database
- The Outbox Pattern ensures exactly-once observable effects

---

## 2. Architecture Overview

The system is composed of **three core services**, a shared **BuildingBlocks** library, and an **orchestration host**:

```
┌─────────────────────────────────────────────────────────────────────┐
│                        .NET Aspire AppHost                         │
│            (Service discovery, health, dashboard, wiring)          │
└────────┬──────────────────┬──────────────────┬──────────────────────┘
         │                  │                  │
    ┌────▼────┐       ┌─────▼─────┐      ┌────▼──────┐
    │ Orders  │       │ Payments  │      │Accounting │
    │ Service │       │ Service   │      │ Service   │
    └────┬────┘       └─────┬─────┘      └────┬──────┘
         │                  │                  │
    ┌────▼────┐       ┌─────▼─────┐      ┌────▼──────┐
    │Orders DB│       │Payments DB│      │Acct'g DB  │
    │(Postgres)│      │(Postgres) │      │(Postgres) │
    └─────────┘       └───────────┘      └───────────┘
         │                  │                  │
         └──────────┬───────┴──────────────────┘
                    │
              ┌─────▼─────┐
              │ RabbitMQ   │
              │ (Events &  │
              │  Commands) │
              └────────────┘
```

**Communication Rule:** Services communicate **exclusively** through asynchronous messaging via RabbitMQ. No service directly queries another service's database.

---

## 3. Clean Architecture Layers

Each service follows Clean Architecture with four layers:

```
Domain → Application → Infrastructure → API
```

| Layer | Responsibility | Dependencies |
|-------|---------------|-------------|
| **Domain** | Aggregates, entities, value objects, domain events, repository interfaces, gateway interfaces | None (pure business logic) |
| **Application** | Command/event handlers, orchestration logic, use cases | Domain only |
| **Infrastructure** | Database contexts, repositories, gateway adapters, messaging | Domain, Application |
| **API** | HTTP endpoints, middleware, DI configuration | All layers |

**Rules:**
1. Domain **never** depends on infrastructure
2. Services communicate via **events only**
3. Payment provider SDKs are isolated behind **adapters**
4. Database changes and event publishing are **atomic** (Outbox Pattern)
5. Accounting is the **financial source of truth**

---

## 4. Service Decomposition

### 4.1 Orders Service

**Responsibility:** Business workflow & customer intent

The Orders service owns the customer-facing order lifecycle. It creates orders, initiates payment authorization, responds to payment events, and manages order state transitions.

**Order State Machine:**
```
Created → PaymentAuthorizing → Authorized → Capturing → Captured
                                                ↓
                                              Failed
         Any state before Captured → Cancelled
```

**Key Components:**
- `Order` aggregate root — enforces state transitions and business rules
- `CreateOrderCommandHandler` — creates order, sends `AuthorizePaymentCommand` to Payments
- `PaymentAuthorizedEventHandler` — marks order as Authorized
- `PaymentCapturedEventHandler` — marks order as Captured
- `PaymentFailedEventHandler` — marks order as Failed

### 4.2 Payments Service

**Responsibility:** Payment orchestration & provider abstraction

The Payments service owns the payment processing lifecycle. It coordinates with payment providers (gateways) through an abstraction layer and emits events based on provider responses.

**Payment State Machine:**
```
Pending → Authorized → Captured → Settled
              ↓            ↓
            Failed       Failed
              ↓
          Cancelled
```

**Key Components:**
- `Payment` aggregate root — records state changes, never talks to provider directly
- `IPaymentGateway` — provider abstraction (Authorize, Capture, Refund, HandleWebhook)
- `SimulatedPaymentGateway` — test adapter with realistic failure simulation
- `AuthorizePaymentCommandHandler` — idempotency check → create payment → call gateway → emit event
- `CapturePaymentCommandHandler` — load payment → call gateway → emit event

### 4.3 Accounting Service

**Responsibility:** Double-entry ledger & financial truth

The Accounting service is the **financial source of truth**. It listens to payment events and creates immutable double-entry ledger records. It also runs reconciliation to detect discrepancies.

**Key Components:**
- `LedgerEntry` entity — immutable double-entry record with TransactionId linking pairs
- `PaymentCapturedEventHandler` — creates Debit/Credit pair on payment capture
- `ReconciliationService` — nightly comparison of provider settlements vs. captured payments
- `Accounts` value object — defines account names (CustomerReceivable, Revenue)

---

## 5. Payment Lifecycle

### Phase 1: Authorization (Funds Reserved)
```
1. Customer submits order via Client
2. Orders Service creates Order (status: Created)
3. Orders transitions to PaymentAuthorizing
4. Orders sends AuthorizePaymentCommand → RabbitMQ → Payments
5. Payments creates Payment (status: Pending)
6. Payments calls IPaymentGateway.AuthorizeAsync()
7a. Success → Payment.MarkAuthorized() → PaymentAuthorizedEvent emitted
7b. Failure → Payment.MarkFailed() → PaymentFailedEvent emitted
8. Orders receives event, updates order status accordingly
```

### Phase 2: Capture (Funds Transferred)
```
1. Order confirmation triggers capture
2. Orders sends CapturePaymentCommand → RabbitMQ → Payments
3. Payments calls IPaymentGateway.CaptureAsync()
4a. Success → Payment.MarkCaptured() → PaymentCapturedEvent emitted
4b. Failure → Payment.MarkFailed() → PaymentFailedEvent emitted
5. Orders receives event, updates order status
```

### Phase 3: Accounting (Ledger Entries)
```
1. Accounting receives PaymentCapturedEvent
2. Creates double-entry ledger pair:
   - Debit:  CustomerReceivable  $100.00
   - Credit: Revenue             $100.00
3. Both entries share the same TransactionId
```

### Phase 4: Reconciliation
```
1. Nightly job compares provider settlement report with captured payments
2. Discrepancies generate adjustment ledger entries
3. The invariant (sum debits = sum credits) must always hold
```

### Phase 5: Failure Recovery
```
- All failures produce compensating events instead of rollbacks
- No silent failures — every error becomes an explicit event
- Corrections are made via new compensating ledger entries, never by modifying existing ones
```

---

## 6. Event-Driven Communication

### Commands (Point-to-Point)

| Command | Source | Destination | Purpose |
|---------|--------|-------------|---------|
| `AuthorizePaymentCommand` | Orders | Payments | Initiate payment authorization |
| `CapturePaymentCommand` | Orders | Payments | Capture authorized funds |
| `CancelPaymentCommand` | Orders | Payments | Cancel/void authorized payment |

**Command Schema (example AuthorizePaymentCommand):**
```csharp
public record AuthorizePaymentCommand(
    Guid OrderId,
    decimal Amount,
    string Currency,
    string IdempotencyKey,
    string CorrelationId,
    string CausationId);
```

### Events (Publish-Subscribe)

| Event | Publisher | Subscribers | Meaning |
|-------|-----------|-------------|---------|
| `PaymentAuthorizedEvent` | Payments | Orders | Funds reserved |
| `PaymentCapturedEvent` | Payments | Orders, Accounting | Funds transferred |
| `PaymentFailedEvent` | Payments | Orders | Payment failed at any stage |
| `PaymentSettledEvent` | Payments | Accounting | Provider confirmed settlement |
| `LedgerEntryCreatedEvent` | Accounting | (Audit) | Ledger record created |

**Rule:** Services must treat events as **immutable facts**.

---

## 7. Outbox Pattern

The Outbox Pattern ensures **atomic consistency** between database state changes and event publishing.

### How It Works

```
┌─ Single DB Transaction ─────────────────────────┐
│  1. Save domain state change (e.g., Payment)     │
│  2. Save OutboxMessage with event payload         │
└──────────────────────────────────────────────────┘
         ↓ (background poller)
┌─ OutboxDispatcher ───────────────────────────────┐
│  1. Read unprocessed OutboxMessages               │
│  2. Deserialize and publish to RabbitMQ           │
│  3. Mark message ProcessedOn timestamp            │
│  4. On failure: increment Retries, retry later    │
└──────────────────────────────────────────────────┘
```

### OutboxMessage Schema

| Field | Type | Description |
|-------|------|-------------|
| `Id` | GUID | Unique message identifier |
| `OccurredOn` | DateTime | When the event occurred |
| `Type` | string | Fully-qualified .NET type name |
| `Payload` | string | JSON-serialized event |
| `ProcessedOn` | DateTime? | When published (null = pending) |
| `Retries` | int | Retry count (max 5, then poison) |
| `Error` | string? | Last error message |

### OutboxDispatcher Configuration
- **Batch size:** 50 messages per poll
- **Polling interval:** 2 seconds
- **Max retries:** 5 per message
- **Ordering:** Messages processed in `OccurredOn` order to preserve causality
- **Poison messages:** After max retries, marked with error and skipped

**Guarantee:** Exactly-once observable effects.

---

## 8. Payment Gateway Abstraction

### Interface

```csharp
public interface IPaymentGateway
{
    Task<GatewayAuthorizationResult> AuthorizeAsync(
        GatewayAuthorizationRequest request, CancellationToken ct);
    Task<GatewayCaptureResult> CaptureAsync(
        GatewayCaptureRequest request, CancellationToken ct);
    Task<GatewayRefundResult> RefundAsync(
        GatewayRefundRequest request, CancellationToken ct);
    Task<GatewayWebhookResult> HandleWebhookAsync(
        string payload, string signature, CancellationToken ct);
}
```

### Request/Response Models

| Model | Fields |
|-------|--------|
| `GatewayAuthorizationRequest` | IdempotencyKey, Amount, Currency |
| `GatewayAuthorizationResult` | Success, TransactionId, ErrorCode, ErrorMessage |
| `GatewayCaptureRequest` | TransactionId, Amount |
| `GatewayCaptureResult` | Success, ErrorCode, ErrorMessage |
| `GatewayRefundRequest` | TransactionId, Amount |
| `GatewayRefundResult` | Success, RefundId, ErrorCode, ErrorMessage |
| `GatewayWebhookResult` | EventType, TransactionId, Metadata |

### Simulated Provider Behaviors

| Condition | Behavior |
|-----------|----------|
| Amount ends in `.99` | Declined (INSUFFICIENT_FUNDS) |
| Amount > 10,000 | Timeout exception |
| Capture operations | 5% random failure rate |
| All other amounts | Successful with simulated transaction ID |
| Network latency | 50-300ms random delay |

### Adding a New Provider

To add a new payment provider (e.g., Stripe):

1. Create `StripePaymentGateway : IPaymentGateway` in `Payments.Infrastructure/Gateways/`
2. Map Stripe SDK responses to `Gateway*Result` records
3. Implement webhook signature validation in `HandleWebhookAsync`
4. Register in DI — no changes to domain or application layer required

---

## 9. Idempotency & Data Integrity

### Idempotency Key Flow

All commands include an `IdempotencyKey`. Before processing:

1. Handler checks if a record with the same key exists
2. If exists → return stored result (no duplicate processing)
3. If not → process normally and store result

**ProcessedCommands Table:**

| Field | Type | Description |
|-------|------|-------------|
| `Key` | string | Idempotency key |
| `Response` | string | Stored result |
| `CreatedAt` | DateTime | When processed |

**Protection scope:** Prevents double charging, duplicate orders, and duplicate ledger entries.

---

## 10. Double-Entry Accounting

### Principle

Every financial transaction creates exactly **two** entries:
1. **Debit** — money coming in / asset increasing
2. **Credit** — money going out / liability/revenue increasing

### Payment Capture Example

```
┌────────────────────────────────────────────────┐
│  TransactionId: abc-123                         │
├────────────────────┬───────────┬───────────────┤
│  Account           │  Debit    │  Credit       │
├────────────────────┼───────────┼───────────────┤
│  CustomerReceivable│  $100.00  │      —        │
│  Revenue           │     —     │  $100.00      │
├────────────────────┼───────────┼───────────────┤
│  TOTAL             │  $100.00  │  $100.00  ✓   │
└────────────────────┴───────────┴───────────────┘
```

### LedgerEntry Entity

| Field | Description |
|-------|-------------|
| `TransactionId` | Groups debit/credit pair |
| `PaymentId` | Links to originating payment |
| `AccountName` | e.g., CustomerReceivable, Revenue |
| `DebitAmount` | Amount debited (0 for credit entries) |
| `CreditAmount` | Amount credited (0 for debit entries) |
| `Currency` | ISO 4217 currency code |
| `Description` | Human-readable description |

**Immutability:** Ledger entries are never modified. Corrections create new compensating entries, providing a complete audit trail.

**Invariant:** Sum of all debits must ALWAYS equal sum of all credits.

---

## 11. Resilience & Error Handling

### Error Classification

| Type | Description | Handling |
|------|-------------|----------|
| `DomainException` | Business rule violation | Return ProblemDetails |
| `IntegrationException` | Provider/network failure | Log, possibly retry |
| `TransientException` | Retryable error | Auto-retry with backoff |

### Resilience Policies (Polly)

| Policy | Configuration |
|--------|--------------|
| **Retries** | Exponential backoff for transient failures |
| **Circuit Breaker** | Opens on sustained provider failures |
| **Timeouts** | Enforced on external provider calls |
| **Fallback** | Alternate provider routing when primary fails |

### Error Handling Rules
- No unhandled exceptions leave the application layer
- Global middleware converts errors into **ProblemDetails** responses
- All failures produce **compensating events** (no silent rollbacks)
- Provider errors are caught and mapped to `PaymentFailed` events

---

## 12. Observability

### Structured Logging (Serilog)
- All logs are structured with named properties
- Payment operations include paymentId, orderId, transactionId, amount, currency

### Distributed Tracing (OpenTelemetry)

Each request carries three correlation identifiers:

| ID | Purpose |
|----|---------|
| `TraceId` | Distributed trace across all services |
| `CorrelationId` | Business-level correlation (order → payment → accounting) |
| `CausationId` | Identifies the event/command that caused this action |

### Correlation Flow
```
Client → Orders (TraceId=T1, CorrelationId=OrderId)
  → Payments (TraceId=T1, CorrelationId=OrderId, CausationId=OrderId)
    → Accounting (TraceId=T1, CorrelationId=OrderId, CausationId=PaymentId)
```

Allows tracking a single payment from order creation to ledger entry across all services.

---

## 13. Security

| Principle | Implementation |
|-----------|---------------|
| Never trust client payment status | Only webhooks finalize payments |
| Webhook signature validation | Provider adapter verifies signatures |
| No card data storage | Provider handles tokenization |
| Secrets management | Environment variables, not config files |
| HTTPS everywhere | All inter-service and external communication |
| Minimal data exposure | Only transaction references stored for reconciliation |

---

## 14. Database Strategy

- Each service owns its **own PostgreSQL database**
- **No shared schemas** — services are fully decoupled at the data level
- Migrations run at startup in **development only**
- Production uses **migration pipelines** (CI/CD)
- Each database includes the `OutboxMessages` table for the Outbox Pattern

---

## 15. Technology Stack

| Component | Technology |
|-----------|-----------|
| Runtime | .NET 10 |
| Web Framework | ASP.NET Core Web API |
| Orchestration | .NET Aspire |
| Messaging | RabbitMQ via MassTransit |
| Database | PostgreSQL via EF Core |
| CQRS | MediatR (Orders service) |
| Logging | Serilog |
| Tracing | OpenTelemetry |
| Resilience | Polly |

---

## 16. Repository Structure

```
src/
 ├─ AppHost/                          # .NET Aspire orchestration
 ├─ BuildingBlocks/
 │   ├─ Contracts/                    # Shared commands & events
 │   │   ├─ Commands/                 # AuthorizePayment, CapturePayment, CancelPayment
 │   │   └─ Events/                   # PaymentAuthorized, PaymentCaptured, etc.
 │   ├─ Messaging/                    # IEventBus, MassTransitEventBus, OutboxDispatcher
 │   ├─ Persistence/                  # AggregateRoot, Entity, OutboxMessage, IUnitOfWork
 │   ├─ Observability/                # CorrelationIdMiddleware, GlobalExceptionHandler
 │   └─ Exceptions/                   # DomainException, IntegrationException, TransientException
 ├─ Services/
 │   ├─ Orders/
 │   │   ├─ Orders.Api/               # Program.cs, endpoints
 │   │   ├─ Orders.Application/       # Commands, EventHandlers, Queries
 │   │   ├─ Orders.Domain/            # Order aggregate, OrderStatus, events
 │   │   └─ Orders.Infrastructure/    # OrdersDbContext, OrderRepository
 │   ├─ Payments/
 │   │   ├─ Payments.Api/             # Program.cs, endpoints  
 │   │   ├─ Payments.Application/     # AuthorizePayment, CapturePayment handlers
 │   │   ├─ Payments.Domain/          # Payment aggregate, IPaymentGateway, PaymentStatus
 │   │   └─ Payments.Infrastructure/  # PaymentsDbContext, SimulatedPaymentGateway
 │   └─ Accounting/
 │       ├─ Accounting.Api/           # Program.cs, endpoints
 │       ├─ Accounting.Application/   # PaymentCapturedEventHandler, ReconciliationService
 │       ├─ Accounting.Domain/        # LedgerEntry, Accounts, ILedgerRepository
 │       └─ Accounting.Infrastructure/# AccountingDbContext, LedgerRepository
 ├─ Client/                           # Test client
 └─ WebUI/                            # Web dashboard
```

---

## 17. Running the System

```bash
dotnet run --project src/AppHost
```

This launches:
- All three microservices (Orders, Payments, Accounting)
- PostgreSQL databases for each service
- RabbitMQ message broker
- .NET Aspire observability dashboard

### Testing Flow
1. Create order via Client
2. Authorization event dispatched to Payments
3. Payment authorized by simulated provider
4. Capture triggered on order confirmation
5. Accounting ledger updated with double-entry records
6. Check logs to trace correlation ID across all services

---

## 18. Future Enhancements

- **Multi-provider smart routing** — route to cheapest/fastest provider
- **Fraud scoring integration** — pre-authorization risk assessment
- **Refund workflows** — full refund lifecycle with ledger reversal
- **Dispute management** — chargeback handling and evidence submission
- **Settlement batching** — aggregate settlements for efficiency
- **Currency conversion ledger** — multi-currency support with FX entries
