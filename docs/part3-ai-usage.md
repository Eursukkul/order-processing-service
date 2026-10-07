# Part 3 — AI-Assisted Development

> **AI tool used: Claude Code (Anthropic) — GitHub Copilot was not used.** Part 3 asks for
> "an AI coding assistant such as GitHub Copilot"; the workflow below (constrained prompts,
> line-by-line review, committed rules) is tool-agnostic and applies the same way to Copilot.

## 3.1 Implementation

`POST /api/orders` lives in `src/OrderService.Api/Features/Orders/`:

| File | Role |
|---|---|
| `OrdersEndpoints.cs` | Route, typed results, outcome → HTTP status mapping |
| `CreateOrderContracts.cs` | Request/response records |
| `CreateOrderRequestValidator.cs` | Shape validation (no DB access) |
| `CreateOrderHandler.cs` | Business flow: customer → products → reserve → persist |
| `GetOrderHandler.cs` | `GET /api/orders/{id}`: status tracking, no-tracking projection |
| `CreateOrderResult.cs` | Outcome as a value, not an exception |
| `CreateOrderLog.cs` | Source-generated structured logging |
| `Infrastructure/OrderDbContext.cs` | Explicit mapping onto the Part 2 schema |

### Behaviour

| Condition | Response |
|---|---|
| Valid, stock available | `201 Created` + `{ orderId, orderStatus, totalAmount }`, `Location: /api/orders/{id}` |
| Malformed body, qty ≤ 0, duplicate ProductId | `400` `ValidationProblemDetails` |
| Customer or product does not exist | `404` ProblemDetails |
| Product inactive | `409` ProblemDetails |
| Insufficient stock (checked, or lost the race) | `409` ProblemDetails |
| Anything unexpected | `500` ProblemDetails with a traceId, details only in the log |

### Points worth defending in the interview
- **Prices come from the database.** The request carries `productId` and `quantity` only, so a
  tampered client cannot set its own price. The total is derived server-side.
- **Two-stage availability check, on purpose.** The first read exists to produce a precise error
  ("requested 6, available 5") and the prices. It is explicitly *not* the guarantee — the
  guarantee is the conditional `ExecuteUpdateAsync`, which is the only thing that holds under
  concurrency. Without the second stage this code oversells; without the first it returns vague errors.
- **Lock ordering by ascending `ProductId`** so two concurrent multi-item orders cannot deadlock.
- **Execution strategy around the transaction.** `EnableRetryOnFailure` makes EF Core reject a
  user-initiated transaction unless it runs inside `CreateExecutionStrategy()`. The change tracker
  is cleared at the top of the retried delegate so a retry does not double-add the order.
- **Failures are values.** `CreateOrderResult` makes every rejection visible to the compiler and
  keeps the endpoint's status-code mapping exhaustive.
- **Out of scope by instruction:** real payment, real shipping, RabbitMQ infrastructure, Redis.
  The one place they would attach is marked with a comment: the outbox insert inside the transaction.

## 3.2 Unit Tests

`dotnet test` → **16 passing**.

| Test | Scenario |
|---|---|
| `…CreatesPendingOrderPricedFromTheCatalogue` | Happy path: order + items persisted, status `Pending`, total 5990.00 from catalogue prices, stock decremented by exactly the ordered quantity |
| `…WhenRequestedQuantityExceedsStock_RejectsAndLeavesStockUntouched` | Product unavailable |
| `…WhenProductIsInactive_RejectsTheOrder` | `IsActive = false` |
| `…WhenProductDoesNotExist_RejectsTheOrder` | Unknown product |
| `…WhenCustomerDoesNotExist_RejectsBeforeTouchingInventory` | Unknown customer, no stock side effect |
| `…WhenOneItemOfManyIsUnavailable_RollsBackTheWholeOrder` | Atomicity: the first item's reservation is undone |
| `…WhenTwoOrdersRaceForTheLastUnit_OnlyOneSucceeds` | Concurrency guard: second order rejected, stock lands on 0, never negative |
| `GetOrderHandlerTests` (2) | Order read back with status + items; unknown id → null (404) |
| `CreateOrderRequestValidatorTests` (7) | Empty basket, qty 0/-1, null item, duplicate ProductId, bad CustomerId, valid request |

**Why SQLite in-memory and not the EF in-memory provider.** The in-memory provider is not
relational: it ignores transactions and does not translate `ExecuteUpdate`. Those are the two
mechanisms this handler's correctness rests on, so testing against it would make the most
important tests meaningless. SQLite runs real SQL, so a green test means the SQL is valid.

**Honest limitation of the concurrency test.** SQLite serialises writers, so the test proves the
*guard* (the loser's conditional `UPDATE` matches zero rows → rejected, stock never negative), not
true parallel contention. Proving the latter needs an integration test against real SQL Server —
`Testcontainers.MsSql` with N parallel callers — which is the next test I would add.

---

## What I asked the AI assistant to generate

Prompts were narrow and carried the constraint, not just the goal. Roughly in order:

1. *"Scaffold an EF Core `DbContext` mapping these four existing SQL Server tables. Explicit
   `OnModelCreating`, no migrations, no conventions — the schema already exists and must not change."*
2. *"Write a minimal API `POST /api/orders` handler. Reserve stock with a single conditional
   `ExecuteUpdateAsync` whose `Where` contains `StockQuantity >= quantity`. Do not read stock then
   update it."* — the constraint was in the prompt because the first, unconstrained attempt
   generated exactly the read-then-write race.
3. *"Convert these interpolated `logger.LogInformation` calls to source-generated `[LoggerMessage]`."*
4. *"Map this outcome enum to typed results with ProblemDetails. 404 for not-found, 409 for state
   conflict, 400 for validation."*
5. *"Write xUnit tests for successful order creation and for product-unavailable. Use SQLite
   in-memory, assert persisted state through a second `DbContext`."*
6. *"This T-SQL reserves stock with a set-based `UPDATE … OUTPUT`. Review it for correctness under
   `READ COMMITTED` with two concurrent buyers of the last unit."* — used as a reviewer, not an author.

Where I deliberately did **not** use generation: the Part 1 design decisions (sync/async split,
outbox vs. direct publish, idempotency ledger). Those are judgement calls that have to be mine to
defend, and an assistant will happily produce a plausible-sounding architecture I cannot justify.

## How I reviewed the generated code

- **Read every diff line by line** against the ten rules in `.github/copilot-instructions.md`.
  Accepting a suggestion is a code review, not a keystroke.
- **Checked the generated SQL, not just the C#.** Turned on
  `Microsoft.EntityFrameworkCore.Database.Command` logging and confirmed the reservation really
  emits one `UPDATE … WHERE StockQuantity >= @qty`, not a `SELECT` followed by an `UPDATE`.
- **Wrote the failing test first for anything about concurrency.** The race test and the partial-
  rollback test were written before accepting the handler, so "it compiles" was never the bar.
- **Caught three things the assistant got wrong**, which are the most useful things to be able to
  describe in the interview:
  1. *Read-then-write stock check.* The first suggestion loaded the `Product` entity, compared in
     C#, then saved — the textbook oversell bug. Rejected and re-prompted with the constraint.
  2. *Transaction + retry incompatibility.* The generated code combined
     `EnableRetryOnFailure` with a bare `BeginTransactionAsync`, which throws at runtime on SQL
     Server. Fixed by wrapping the block in `CreateExecutionStrategy()` — and the generated code
     then needed `ChangeTracker.Clear()` added so a retry would not double-add the order, which
     the assistant did not volunteer.
  3. *EF in-memory provider for the tests.* Suggested by default, and it would have made the
     transaction and `ExecuteUpdate` assertions vacuous. Swapped for SQLite.
- **Treated confident explanations as unverified.** Every claim about locking behaviour was checked
  against the EF Core / SQL Server documentation rather than accepted because it read well.

## How the rules were established and maintained

- `.github/copilot-instructions.md` is committed, so the rules travel with the repository and are
  reviewable in a PR like any code. The file uses Copilot's conventional path so a teammate on
  Copilot picks the same rules up automatically; Claude Code was given them as context.
- Each rule exists because of a concrete failure, not as style preference — rule 2 exists because
  of the oversell suggestion, rule 7 because of the retry crash, rule 8 because of the in-memory
  provider. The file grows by one line each time an assistant gets something wrong.
- The rules are enforced mechanically where possible, not by goodwill: zero-warning build,
  `dotnet test` green, and a test for every rule that is testable. A rule that only lives in a
  markdown file decays; a rule with a test behind it does not.
- Scope discipline: anything the assistant suggests that needs a new NuGet package stops and gets
  justified. `FluentAssertions` was removed for exactly this reason — version 8 requires a paid
  licence for commercial use, so the tests use plain xUnit assertions instead.
