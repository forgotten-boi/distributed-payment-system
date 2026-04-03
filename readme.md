# Enterprise Distributed Payments Platform (.NET + Aspire)

This repository implements a production‑grade distributed payment processing system designed using Clean Architecture, domain events, eventual consistency and financial correctness principles.

The goal of this project is NOT to demonstrate CRUD microservices — it models a real financial lifecycle:

Authorization → Capture → Settlement → Reconciliation → Accounting impact → Failure recovery

---

# 1. System Overview

## Services

| Service | Responsibility |
|-------|------|
| Orders | Business workflow & customer intent |
| Payments | Payment orchestration & provider abstraction |
| Accounting | Double‑entry ledger & financial truth |
| AppHost | Distributed runtime & infrastructure wiring |
| Client | Test client |

All services communicate through asynchronous messaging.
No service directly queries another service database.

---

# 2. Architecture Principles

This solution follows Clean Architecture:

Domain → Application → Infrastructure → API

Rules:

1. Domain never depends on infrastructure
2. Services communicate via events only
3. Payment provider SDKs are isolated behind adapters
4. Database changes and event publishing are atomic (Outbox Pattern)
5. Accounting is the financial source of truth

---

# 3. Technology Stack

.NET 9
ASP.NET Core Web API
Distributed orchestration (Aspire)
RabbitMQ messaging
MassTransit (Saga State Machine, consumers, outbox)
Entity Framework Core
PostgreSQL
Serilog logging
OpenTelemetry tracing
Polly resilience policies

---

# 4. Repository Structure

```
src/
 ├─ AppHost
 ├─ BuildingBlocks
 │   ├─ Contracts
 │   ├─ Messaging
 │   ├─ Persistence
 │   ├─ Observability
 │   └─ Exceptions
 ├─ Services
 │   ├─ Orders
 │   ├─ Payments
 │   └─ Accounting
 └─ Client
```

BuildingBlocks contains all shared packages. No shared code exists between services outside this folder.

---

# 5. Event Driven Communication

## Commands

AuthorizePayment
CapturePayment
CancelPayment
ConfirmOrderRequested (API → Saga)
CancelOrderRequested (API → Saga)

## Events

PaymentAuthorized
PaymentCaptured
PaymentFailed
PaymentSettled
LedgerEntryCreated
OrderSagaStateChanged
AuthorizationTimeoutExpired

Services must treat events as immutable facts.

---

# 5.1 Saga Orchestration Pattern

The system uses a MassTransit `SagaStateMachine` to orchestrate the payment lifecycle.

## Why Saga over Choreography

The original choreography approach had services react to each other's events independently.
The saga replaces this with a centralized state machine that:

1. Makes the full payment flow visible in one place
2. Handles compensation on capture failure (releases authorized hold)
3. Schedules timeouts for stuck authorizations
4. Publishes state change events for observability
5. Persists saga state in EF Core alongside order data

## State Machine

```
  Created ──► Authorizing ──► Authorized ──► Capturing ──► Captured (final)
                  │                │              │
                  ▼                ▼              ▼
               Failed          Cancelled       Failed → (compensate: cancel auth hold)
```

## Saga State Persistence

The `OrderPaymentState` table in the Orders database stores:
- CorrelationId (= OrderId)
- CurrentState
- PaymentId, ProviderTransactionId
- FailureReason
- Timestamps

Pessimistic concurrency prevents race conditions between concurrent messages.

## API

`GET /api/orders/{id}/saga-state` returns the current saga state for any order.

---

# 6. Outbox Pattern (Critical)

Every state change stores domain events in the same database transaction.
A background dispatcher publishes events to RabbitMQ.

Table schema:

```
OutboxMessages
--------------
Id
OccurredOn
Type
Payload
ProcessedOn
Retries
```

Guarantee: exactly‑once observable effects.

---

# 7. Payment Lifecycle

## Authorization
Customer initiates payment
Orders sends AuthorizePayment command
Payments calls provider
Payments emits PaymentAuthorized or PaymentFailed

## Capture
Triggered by order confirmation
Payments captures funds
PaymentCaptured event emitted

## Accounting
Accounting listens to PaymentCaptured
Creates double‑entry ledger records

Debit: Customer Receivable
Credit: Revenue

## Reconciliation
Nightly job compares provider settlement report with captured payments
Discrepancies generate adjustment ledger entries

## Failure Handling
All failures produce compensating events instead of rollbacks

---

# 8. Idempotency

All commands include IdempotencyKey

Table:

```
ProcessedCommands
Key
Response
CreatedAt
```

Duplicate requests return stored response
Prevents double charging

---

# 9. Payment Provider Abstraction

The system never exposes provider models to domain.

Adapters implement:

```
IPaymentGateway
 - Authorize
 - Capture
 - Refund
 - HandleWebhook
```

Providers can be added without touching business logic.

---

# 10. Observability

Includes:

Structured logging
Distributed tracing
Correlation IDs across services
Metrics

Each request carries:

TraceId
CorrelationId
CausationId

Allows tracking a payment across all services.

---

# 11. Error Handling Strategy

No unhandled exceptions leave application layer.
Use Errors for expected, and Exceptions for unexpected failures.
Errors classified as:

DomainException – business rule violation
IntegrationException – provider/network failure
TransientException – retryable

Global middleware converts errors into ProblemDetails responses.

---

# 12. Resilience Policies

Retries: exponential backoff
Circuit breaker: provider failures
Timeouts: external calls
Fallback: alternate provider routing

---

# 13. Database Strategy

Each service owns its own database.
No shared schemas.

Migrations run at startup in development only.
Production uses migration pipelines.

---

# 14. Security Considerations

Never trust client payment status
Only webhooks finalize payment
Validate webhook signatures
Store minimal card data (tokenized only)
Use HTTPS everywhere
Secrets loaded via environment variables

---

# 15. Running the System

Start distributed environment:

```
dotnet run --project src/AppHost
```

This launches:
- All services
- RabbitMQ
- Databases
- Observability dashboard

---

# 16. Testing Flow

1. Create order via client
2. Authorization event sent
3. Payment authorized
4. Capture triggered
5. Accounting ledger updated

Check logs to trace correlation id across services.

---

# 17. Production Readiness Checklist

- Idempotency protection
- Exactly-once messaging
- Financial reconciliation
- Audit trail
- Provider failover ready
- Observability included
- Domain isolation
- Eventual consistency
- Saga orchestration with compensation and timeouts

---

# 18. Future Enhancements

Multi‑provider smart routing
Fraud scoring integration
Refund workflows
Dispute management
Settlement batching
Currency conversion ledger

---

# Final Notes

This project models financial correctness rather than request/response CRUD.

If implemented correctly, payments can never be lost, duplicated or silently corrupted — only compensated through explicit accounting events.

Operational Note: 
In production, ensure all services are monitored and have alerting on critical failures, especially around payment processing and reconciliation.
