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

## Phase 8: Create a UI app to test it

### 8.1 Blazor Server UI Project
- [x] Create WebUI Blazor Server project (src/WebUI)
- [x] Configure Aspire service discovery for HTTP clients (orders-api, payments-api, accounting-api)
- [x] Create PaymentPlatformClient service (typed HTTP client for all backend APIs)
- [x] Create OrderTracker service (in-memory session state for created orders)

### 8.2 Dashboard Page
- [x] Stats grid: total orders, captured, authorized, failed, captured volume
- [x] Payment lifecycle flow visualization
- [x] Recent orders table with links

### 8.3 Order Management Pages
- [x] Orders list page with status badges, refresh, confirm/cancel actions
- [x] Create Order form with amount, currency, customer ID fields
- [x] Simulated failure tips (amounts ending in .99 declined, >10k timeout)
- [x] Order Details page with payment info, ledger entries, action buttons

### 8.4 Accounting & Ledger Page
- [x] Account balance cards (CustomerReceivable debits, Revenue credits)
- [x] Run Reconciliation button with balanced/imbalanced result display
- [x] Ledger entry lookup by Transaction/Payment ID

### 8.5 Full Lifecycle Demo Page
- [x] Automated 6-step flow: Create → Authorize → Confirm → Capture → Verify Ledger → Reconcile
- [x] Real-time step progress with active/completed/failed states
- [x] Configurable amount for testing happy path vs failure scenarios

### 8.6 AppHost Integration
- [x] Add WebUI project reference to AppHost
- [x] Wire service discovery (WithReference to all 3 services)
- [x] Add to solution file

---

## Phase 9: Saga Orchestration Pattern

### 9.1 Saga Contracts
- [x] Define ConfirmOrderRequested command (API → Saga trigger)
- [x] Define CancelOrderRequested command (API → Saga trigger)
- [x] Define OrderSagaStateChanged event (Saga → external observers)
- [x] Define AuthorizationTimeoutExpired event (Saga scheduled timeout)

### 9.2 Saga State Machine
- [x] OrderPaymentState entity (MassTransit SagaStateMachineInstance)
- [x] OrderPaymentStateMachine with full lifecycle:
  - Initially: OrderCreated → send AuthorizePaymentCommand → Authorizing
  - Authorizing: PaymentAuthorized → Authorized / PaymentFailed → Failed / Timeout → Failed
  - Authorized: ConfirmRequested → send CapturePaymentCommand → Capturing / CancelRequested → send CancelPaymentCommand → Cancelled
  - Capturing: PaymentCaptured → Captured / PaymentFailed → compensate (cancel auth) → Failed
  - Terminal states: Captured, Failed, Cancelled (ignore further messages)
- [x] Authorization timeout schedule (5-minute safety net)
- [x] Capture failure compensation (release authorized hold)

### 9.3 Orders Service Refactoring
- [x] Simplify CreateOrderCommandHandler (publishes domain event, saga sends authorize command)
- [x] Simplify ConfirmOrderCommandHandler (publishes ConfirmOrderRequested, saga sends capture command)
- [x] Simplify CancelOrderCommandHandler (publishes CancelOrderRequested, saga sends cancel command)
- [x] Remove old choreography-based event handlers (PaymentAuthorized/Captured/FailedEventHandler)
- [x] Add OrderSagaStateChangedHandler (syncs saga state back to Order aggregate)
- [x] Add saga state API endpoint (GET /api/orders/{id}/saga-state)

### 9.4 Infrastructure Updates
- [x] Add OrderPaymentSagaStates table to OrdersDbContext
- [x] Configure MassTransit EF Core saga persistence (pessimistic concurrency)
- [x] Wire delayed message scheduler for saga timeouts
- [x] Update Orders.Api, Orders.Application, Orders.Infrastructure csproj dependencies

### 9.5 UI & Client Updates
- [x] Add SagaStateDetail model to WebUI
- [x] Add GetSagaStateAsync to PaymentPlatformClient
- [x] Update Dashboard to show saga flow visualization
- [x] Add saga state panel to OrderDetails page
- [x] Update Lifecycle demo description
- [x] Update Console Client with saga state check step

### 9.6 Unit Tests
- [x] Create test project (Orders.Application.Tests)
- [x] Saga state machine tests (happy path, failure, compensation, timeout, cancellation)
- [x] Command handler tests (CreateOrder, ConfirmOrder, CancelOrder)
- [x] Order aggregate domain tests

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
| Saga over Choreography | Centralised orchestration for payment lifecycle provides visibility, compensation, timeouts, and debuggability |
