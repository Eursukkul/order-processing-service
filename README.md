# Senior .NET Developer — Technical Assessment

.NET 10 · EF Core 10 · SQL Server · xUnit

AI assistant: Claude Code (Anthropic), not GitHub Copilot — see [`docs/part3-ai-usage.md`](docs/part3-ai-usage.md).

## Answers

| Part | Where |
|---|---|
| 1.1 Order processing flow (diagram + steps) | [`docs/part1-system-design.md`](docs/part1-system-design.md) |
| 1.2 Sync vs async | same file |
| 1.3 RabbitMQ placement + example messages | same file |
| 1.4 Concurrency / overselling | same file |
| 1.5 Duplicate message processing / idempotency | same file |
| 2.1 Create-order transaction (T-SQL) | [`sql/2.1-create-order.sql`](sql/2.1-create-order.sql) |
| — schema + seed so 2.1 runs | [`sql/0-schema-and-seed.sql`](sql/0-schema-and-seed.sql) |
| 3.1 `POST /api/orders` (+ `GET /api/orders/{id}` for status tracking) | [`src/OrderService.Api/Features/Orders/`](src/OrderService.Api/Features/Orders) |
| 3.2 Unit tests + AI-assistance writeup | [`tests/OrderService.Tests/`](tests/OrderService.Tests), [`docs/part3-ai-usage.md`](docs/part3-ai-usage.md) |
| Rules governing AI generation | [`.github/copilot-instructions.md`](.github/copilot-instructions.md) |

## Run

```bash
dotnet build                                   # 0 warnings
dotnet test tests/OrderService.Tests           # 16 unit tests, no database (SQLite in-memory)
dotnet test tests/OrderService.IntegrationTests  # 13 integration tests, needs Docker
dotnet test                                    # both
```

The integration tests start a real SQL Server in Docker (Testcontainers), apply
`sql/0-schema-and-seed.sql`, and host the API in-process. They cover what SQLite cannot: truly
parallel buyers for the last unit, deadlock-free multi-item orders, the retrying execution
strategy, `DATETIME2` round-tripping, and the HTTP pipeline (status codes, ProblemDetails).

To run the API against SQL Server:

```bash
docker run -e 'ACCEPT_EULA=Y' -e 'MSSQL_SA_PASSWORD=Your_strong_Passw0rd' \
  -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest

sqlcmd -S localhost,1433 -U sa -P 'Your_strong_Passw0rd' -C -i sql/0-schema-and-seed.sql
dotnet run --project src/OrderService.Api
```

Then use [`src/OrderService.Api/OrderService.Api.http`](src/OrderService.Api/OrderService.Api.http)
for the happy path and each failure case. OpenAPI document at `/openapi/v1.json` in Development.

## Layout

```
docs/                              Part 1 design, Part 3 AI writeup
sql/                               Part 2 T-SQL (+ schema/seed)
src/OrderService.Api/
  Domain/                          Entities only
  Infrastructure/OrderDbContext    Explicit mapping onto the existing schema
  Features/Orders/                 Vertical slice for POST /api/orders
tests/OrderService.Tests/          xUnit, SQLite in-memory
tests/OrderService.IntegrationTests/  xUnit, Testcontainers SQL Server + WebApplicationFactory
.github/copilot-instructions.md    Committed rules the AI assistant generates against
```

## Scope

Implemented: validation, product existence/active/availability checks, atomic inventory
reservation, order creation in one transaction, structured logging, ProblemDetails error handling,
and `GET /api/orders/{id}` for status tracking (the `Location` of the `201`).

Designed but not implemented: the `Idempotency-Key` header on `POST /api/orders` (Part 1.1 step 2,
Part 1.5). It is not in the 3.1 requirements, and doing it properly needs a new
`IdempotencyKeys` table written in the same transaction as the order — a schema change beyond
the given tables. Until then a client retry after a timeout can create a second order.

Not implemented, per the brief: real payment integration, real shipping integration, RabbitMQ
infrastructure, Redis infrastructure. The single attachment point for the async flow — an outbox
row written inside the same transaction — is marked in `CreateOrderHandler`.
