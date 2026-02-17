# Implementation Plan — Enterprise Distributed Payments Platform

## Phase 1: Project Scaffolding & Infrastructure

### 1.1 Repository Setup
- [x] Initialize git repository
- [x] Create .gitignore for .NET, Rider, VS, macOS, node_modules
- [x] Create directory structure matching README §4
- [x] Create .NET solution file
- [x] Create Directory.Build.props with shared properties (.NET 10)
- [x] Create Directory.Packages.props for central package management

### 1.2 BuildingBlocks — Contracts
- [x] Define integration events: PaymentAuthorized, PaymentCaptured, PaymentFailed, PaymentSettled, LedgerEntryCreated
- [x] Define integration commands: AuthorizePayment, CapturePayment, CancelPayment
- [x] Define shared value objects: Money, Currency, IdempotencyKey, CorrelationContext

### 1.3 BuildingBlocks — Exceptions
- [x] DomainException (business rule violation)
- [x] IntegrationException (provider/network failure)
- [x] TransientException (retryable)

### 1.4 BuildingBlocks — Persistence
- [x] OutboxMessage entity and OutboxDbContext
- [x] ProcessedCommand entity (idempotency)
- [x] IUnitOfWork abstraction
- [x] Base Entity / AggregateRoot with domain event collection

### 1.5 BuildingBlocks — Messaging
- [x] IEventBus abstraction
- [x] RabbitMQ implementation with MassTransit
- [x] Outbox dispatcher background service
- [x] Message envelope with CorrelationId / CausationId / TraceId

### 1.6 BuildingBlocks — Observability
- [x] Serilog configuration builder
- [x] OpenTelemetry tracing setup
- [x] Correlation ID middleware
- [x] Metrics registration helpers

---

## Phase 2: Orders Service

### 2.1 Domain Layer
- [x] Order aggregate root (Id, CustomerId, Amount, Currency, Status, IdempotencyKey)
- [x] OrderStatus value object (Created → PaymentAuthorizing → Authorized → Capturing → Captured → Failed → Cancelled)
- [x] Domain events: OrderCreated, OrderPaymentAuthorized, OrderCaptured, OrderFailed

### 2.2 Application Layer
- [x] CreateOrderCommand / Handler
- [x] ConfirmOrderCommand / Handler (triggers capture)
- [x] CancelOrderCommand / Handler
- [x] Event handlers: PaymentAuthorizedHandler, PaymentCapturedHandler, PaymentFailedHandler

### 2.3 Infrastructure Layer
- [x] OrdersDbContext with EF Core
- [x] Order repository
- [x] Outbox integration
- [x] Database migrations

### 2.4 API Layer
- [x] POST /api/orders
- [x] POST /api/orders/{id}/confirm
- [x] POST /api/orders/{id}/cancel
- [x] GET  /api/orders/{id}
- [x] Global exception middleware → ProblemDetails
- [x] Idempotency middleware

---

## Phase 3: Payments Service

### 3.1 Domain Layer
- [x] Payment aggregate root (Id, OrderId, Amount, Currency, Status, ProviderTransactionId)
- [x] PaymentStatus value object (Pending → Authorized → Captured → Failed → Cancelled → Settled)
- [x] Domain events: PaymentAuthorized, PaymentCaptured, PaymentFailed

### 3.2 Application Layer
- [x] AuthorizePaymentCommandHandler
- [x] CapturePaymentCommandHandler
- [x] CancelPaymentCommandHandler
- [x] Idempotency guard

### 3.3 Payment Provider Abstraction
- [x] IPaymentGateway interface (Authorize, Capture, Refund, HandleWebhook)
- [x] Simulated payment provider adapter
- [x] Provider response mapping to domain

### 3.4 Infrastructure Layer
- [x] PaymentsDbContext
- [x] Payment repository
- [x] Outbox integration
- [x] Resilience policies (Polly): retry, circuit breaker, timeout

### 3.5 API Layer
- [x] Webhook endpoint for provider callbacks
- [x] GET /api/payments/{id}
- [x] Global exception middleware

---

## Phase 4: Accounting Service

### 4.1 Domain Layer
- [x] LedgerEntry entity (Id, TransactionId, AccountName, DebitAmount, CreditAmount, Timestamp)
- [x] Account value object
- [x] Double-entry validation (debits = credits per transaction)

### 4.2 Application Layer
- [x] PaymentCapturedHandler → create ledger entries (Debit: CustomerReceivable, Credit: Revenue)
- [x] ReconciliationService (compares settlements vs captures)

### 4.3 Infrastructure Layer
- [x] AccountingDbContext
- [x] Ledger repository
- [x] Outbox integration

### 4.4 API Layer
- [x] GET /api/ledger/{transactionId}
- [x] GET /api/ledger/balance/{account}
- [x] POST /api/reconciliation/run (trigger nightly reconciliation manually)

---

## Phase 5: AppHost (Aspire Orchestration)

- [x] Aspire AppHost project
- [x] Wire PostgreSQL containers per service
- [x] Wire RabbitMQ container
- [x] Wire all services with service discovery
- [x] Configure environment variables and connection strings
- [x] Dashboard configuration

---

## Phase 6: Test Client

- [x] Console/HTTP client that exercises full lifecycle
- [x] Create order → Authorize → Capture → Verify ledger
- [x] Correlation ID propagation demo

---

## Phase 7: Documentation

- [x] Generate architecture PDF (kept OUT of git repo)
- [x] Generate API reference PDF (kept OUT of git repo)
- [x] Add .gitignore rule for /docs-generated/ folder

---

## Commit Strategy

Every phase/sub-phase gets its own commit with detailed messages explaining:
- What was added
- Why (design rationale)
- How it connects to the payment lifecycle
- Error handling approach chosen

---

## Key Design Decisions Log

| Decision | Rationale |
|----------|-----------|
| MassTransit over raw RabbitMQ | Provides outbox, retry, saga, consumer pipeline out of the box |
| EF Core Outbox | Atomic state + event in single transaction |
| Central Package Management | Consistent dependency versions across all projects |
| Simulated Provider | Allows full lifecycle testing without real payment gateway |
| Aggregate Root pattern | Enforces invariants at domain boundary |
| Double-entry ledger | Financial correctness — every debit has matching credit |
| Idempotency via ProcessedCommands | Prevents duplicate payment processing |
| ProblemDetails for errors | RFC 7807 standard error responses |
