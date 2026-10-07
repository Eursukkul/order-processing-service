# Part 3 — AI-Assisted Development

> **AI tool used: Claude Code (Anthropic) — GitHub Copilot was not used.** Part 3 asks for
> "an AI coding assistant such as GitHub Copilot"; the review workflow below is tool-agnostic
> and applies the same way to Copilot.

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

## 3.2 Tests

### Unit tests — `tests/OrderService.Tests`, **16 passing**, SQLite in-memory

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

**Limitation of the SQLite concurrency test.** SQLite serialises writers, so that test proves the
*guard* (the loser's conditional `UPDATE` matches zero rows → rejected, stock never negative), not
true parallel contention. The integration tests below cover that.

### Integration tests — `tests/OrderService.IntegrationTests`, **13 passing**, real SQL Server

A SQL Server 2022 container (Testcontainers) with the committed seed script, and the API hosted
in-process (`WebApplicationFactory`). Needs Docker.

| Test | What it proves that unit tests cannot |
|---|---|
| `ParallelBuyersForTheLastUnit_ExactlyOneWins…` | 20 truly parallel requests for 1 unit → exactly one `201`, nineteen `409`, stock 0, one order — SQL Server row locking |
| `ParallelMultiItemOrdersInOppositeItemOrder…` | 40 parallel orders listing 1001/1002 in opposite orders → all `201`, stock exact, no deadlock — the `ProductId` lock ordering |
| `PostThenFollowLocation…` | Transaction runs under the retrying execution strategy; `Location` resolves; timestamps come back as UTC (`Z`) |
| `OneShortItem_RollsBackTheWholeOrder` | Whole-order rollback on SQL Server |
| `Rejections_…AsProblemDetails` (8) | `409`/`404`/`400` incl. null item, malformed JSON, no body — all `application/problem+json` with a `traceId` |
| `GetUnknownOrder_Returns404ProblemDetails` | `404` as ProblemDetails |

The ProblemDetails tests were checked against the previous exception handler and fail on it
(it returned `application/json`), so they guard that fix.

---

## What I asked the AI assistant to generate

The first version of the whole repository — design doc, SQL, API, tests, and the rules file — was
generated by Claude Code from the assessment brief, in a single commit (`6523482`). I did not keep a
record of the individual prompts from that stage, so I do not list them here rather than
reconstruct them from memory.

After that, the assistant was used under my direction for verification and fixes (commit
`4806daf`). What I asked for, in order:

1. *Run it and test it* — build, unit tests, then start the API against a real SQL Server in
   Docker and call every request in `OrderService.Api.http`.
2. *Check the repository against the assessment PDF* — item by item, and report anything
   incorrect or incomplete.
3. *Fix the issues found* — code bugs test-first, document inconsistencies directly.
4. *Add `GET /api/orders/{id}`* so the `201` `Location` header resolves, and state in the README
   that the `Idempotency-Key` header is designed but not implemented.
5. *Test every remaining case on localhost*, including 20 parallel buyers for the last unit, 40
   parallel opposite-order multi-item orders, and a forced `500` with SQL Server stopped.
6. *Turn those manual checks into integration tests* (Testcontainers + `WebApplicationFactory`) so
   they are repeatable, and fix what the localhost run found.

## How I used the assistant to implement

Everything after the first commit was implemented by the assistant under my direction. The split
of work was deliberate:

| The assistant | Me |
|---|---|
| Ran the build, tests, API, SQL scripts, and ad-hoc load scripts | Decided what had to be checked, and when a result was good enough |
| Proposed options with their cost (e.g. three ways to close the design/code gap) | Chose the option — `GET` endpoint yes, `Idempotency-Key` no |
| Wrote the tests and the production code | Set the scope of each change, and reviewed localhost myself before allowing a commit |
| Drafted the documentation | Required it to state only what is verifiable, including that the tool was Claude Code, not Copilot |

### The loop for a code change
Used for the validator, `GET`, and UTC fixes. The two exception-handler fixes broke step 1 — they
were fixed first, and only got tests later with the integration suite (see below).

1. **Failing test first.** The assistant writes the test and runs it red — e.g.
   `TryValidate_WithNullItem_FailsInsteadOfThrowing` failed with the `NullReferenceException` it
   was written to catch.
2. **Smallest fix** that turns it green, matching the rules in `.github/copilot-instructions.md`.
3. **Zero-warning build and the full suite**, not just the new test.
4. **Run it for real** — the API against SQL Server in Docker, every `.http` case, plus edge cases.
5. **I review and approve the commit.** The assistant does not commit on its own.

### Worked examples
- **`GET /api/orders/{id}`.** The assistant laid out three options with effort and risk; I chose
  the endpoint plus a README note over implementing idempotency. The tests were written first and
  did not compile (`GetOrderHandler` did not exist yet). Then the handler, and `CreatedAtRoute` so
  `Location` is tied to the real route. Verified by following `Location` from a real `POST`.
- **UTC timestamps.** A `DateTimeKind.Utc` assertion was added to the existing test first and
  failed. The fix is one value converter in `ConfigureConventions`, so it covers every `DateTime`
  rather than patching each property.
- **Integration tests.** I asked for them after a manual localhost run had already shown the race
  and deadlock behaviour, so the behaviour would stay proven. They needed two new packages
  (`Testcontainers.MsSql`, `Microsoft.AspNetCore.Mvc.Testing`), which rule 10 requires justifying.
  The fixture reuses the committed seed script instead of a second copy of the schema. Because
  these tests came *after* the content-type fix, the assistant ran them against the old exception
  handler to confirm they fail there (2 failures) — a test that cannot fail proves nothing.

### Where the assistant got it wrong
- Its first localhost deadlock script reported 5 failures. The API was fine: the script sent no
  requests at all (macOS `xargs -I` caps the command at 255 bytes). It was caught because stock
  was still exactly 1000 — a check on the database state, not only the status codes.
- It flagged the wrong `Content-Type` on `500` responses from reading the code, and that claim was
  kept marked as unverified until a `500` was forced by stopping SQL Server and the header was
  observed.
- The originally generated SQL comment justified `RAISERROR` over `THROW` with a reason that is not
  true (`THROW` accepts a variable). It read plausibly; it was only caught by checking it.

## How I reviewed the generated code

The 13 generated unit tests were green from the start. Review by **running the system end to end
against real infrastructure** — not by reading the code or trusting the tests — found the
following. None of these were caught by the unit tests.

| # | Found by | Problem | Fix |
|---|---|---|---|
| 1 | Following the README's setup commands | The seed script had no `CREATE DATABASE`/`USE`, so it created the tables in `master` while the API connected to `OrderDb` → every DB request returned `500` | Script creates and uses `OrderDb`; `2.1-create-order.sql` uses it too |
| 2 | Calling each `.http` request | The "insufficient stock" sample sent quantity 9999, which the validator rejects (`400`) before the stock check (`409`) | Sample uses 50 |
| 3 | Edge-case requests | A missing or malformed body returned `500`: the global exception handler mapped the framework's `BadHttpRequestException` to 500 | Handler uses the exception's own status (`400`) |
| 4 | Edge-case requests | `"items": [null]` threw a `NullReferenceException` in the validator → `500` | Validator rejects a null item (`400`) |
| 5 | Comparing design with code | The `201` `Location` header pointed at `GET /api/orders/{id}`, which did not exist | Endpoint added; `CreatedAtRoute` ties `Location` to the real route |
| 6 | Reading the response JSON | Timestamps had no `Z`: SQL Server returns `DATETIME2` as `DateTimeKind.Unspecified` | Value converter re-tags every `DateTime` as UTC on read |
| 7 | Comparing docs with each other | 1.5 declared `MessageId UNIQUEIDENTIFIER` but its example id is `order-90125-created`; the doc's PK was `MessageId` alone while the seed used `(MessageId, Consumer)`; the 1.3 example total did not match its items | Docs made consistent |
| 8 | Forcing a `500` by stopping SQL Server | Errors from the global exception handler were `application/json`, not `application/problem+json` like every other error | Handler writes through `IProblemDetailsService`; integration tests assert the media type |

How the code fixes were made: for code bugs 4, 5 and 6 a test was written first and seen to
fail before the fix was applied. For 8 the integration test came after the fix, so it was run
against the old handler to confirm it fails there. The full suite, a zero-warning build, and a fresh end-to-end run
against a newly created database were re-run after every change.

Scope decisions were mine: implementing `GET` (cheap, fixes a broken contract) but **not**
`Idempotency-Key`, which needs a new table and API contract beyond the brief — documented as a
known gap rather than added under time pressure.

**Lesson:** generated code that compiles and passes its own generated tests is not reviewed code.
The tests were written by the same assistant that wrote the code, against an in-memory database
the assistant chose, so they shared its blind spots. Running against the real database and the
real HTTP pipeline was what exposed them.

## How the rules were established and maintained

- `.github/copilot-instructions.md` holds the rules — stack, architecture, and ten rules the
  assistant must not break (prices from the DB only, atomic conditional reservation, no I/O inside
  a transaction, async + `CancellationToken`, …). They were written up front as constraints, at
  the same time as the first version of the code, not as lessons learned afterwards.
- The file is committed, so the rules travel with the repository and are reviewable in a PR like
  any code. It uses Copilot's conventional path so a teammate on Copilot loads the same rules
  automatically; for Claude Code the equivalent is `CLAUDE.md` / `AGENTS.md`.
- The rules are backed by checks rather than goodwill where possible: zero-warning build, green
  `dotnet test`, and tests that pin the testable rules (prices from the catalogue, stock never
  negative, whole-order rollback).
- Maintaining them: the issues above show what the rules did **not** cover — they constrain how
  code is written but say nothing about verifying it end to end. The rule that follows from it is
  *"a change is not done until it has been run against a real SQL Server, not only SQLite"* — now
  backed by the `Testcontainers.MsSql` integration tests. The remaining step is running them in CI.
