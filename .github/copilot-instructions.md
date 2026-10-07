# Copilot / AI assistant instructions

These rules are committed to the repository so every AI suggestion is generated against
the same constraints, and so a reviewer can see what the assistant was told. They are the
"development rules governing AI generation" referred to in Part 3.2.

## Stack
- .NET 10, C# 14, nullable reference types on, implicit usings on.
- ASP.NET Core minimal APIs with typed results (`Results<T1, T2, …>`), no MVC controllers.
- EF Core 10 with the SQL Server provider. SQLite in-memory for tests.
- xUnit. No mocking framework: fakes and a real relational test database instead.

## Architecture
- Vertical slices under `Features/<Area>/`. A feature owns its request, response, validator,
  handler, and endpoint mapping. Do not add Repository/UnitOfWork layers on top of `DbContext`
  — `DbContext` already is both.
- `Domain/` holds entities only: no EF attributes, no service dependencies.
- `Infrastructure/` holds the `DbContext` and its explicit `OnModelCreating` mapping. The
  database schema is the contract; do not let conventions silently change it.
- No MediatR, no AutoMapper, no FluentValidation. One endpoint does not justify a pipeline.

## Rules the assistant must not break
1. **Never trust client-supplied money.** Prices and totals are always read from `Products`.
2. **Inventory is reserved with a conditional atomic UPDATE** — the `StockQuantity >= quantity`
   guard lives inside the `UPDATE`/`ExecuteUpdateAsync`, never as a separate read-then-write.
3. **No external I/O inside a database transaction.** No HTTP, no broker publish, no email.
4. **All database access is async** with a `CancellationToken` threaded through.
5. **Expected business failures are return values, not exceptions.** Exceptions are for faults.
6. **Logging uses source-generated `[LoggerMessage]`**, never string interpolation. Never log
   PII (customer name, email) or payment data.
7. **A user-initiated transaction must be wrapped in `CreateExecutionStrategy()`** because
   `EnableRetryOnFailure` is on.
8. **Tests assert persisted state through a fresh `DbContext`**, not the change tracker.
9. Deterministic time via `TimeProvider`; never `DateTime.UtcNow` in business code.
10. If a suggestion needs a new NuGet package, stop and justify it before accepting.

## Review gate for generated code
Nothing is committed until: it builds with zero warnings, `dotnet test` is green, and the
diff has been read line by line against the ten rules above.
