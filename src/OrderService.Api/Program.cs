using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using OrderService.Api.Features.Orders;
using OrderService.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("OrderDb"),
        sql =>
        {
            // Transient SQL faults (failover, throttling) are retried by the provider.
            // CreateOrderHandler therefore runs its explicit transaction inside
            // db.Database.CreateExecutionStrategy(), which is required once retries
            // are enabled - otherwise EF throws on a user-initiated transaction.
            sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), null);
            sql.CommandTimeout(30);
        }));

builder.Services.AddScoped<ICreateOrderHandler, CreateOrderHandler>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// Any unhandled exception becomes a ProblemDetails response; the details stay in the
// log, not in the payload, so internal errors are never leaked to the caller.
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapOrdersEndpoints();

app.Run();

internal sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception on {Method} {Path}.",
            httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        await httpContext.Response.WriteAsJsonAsync(new
        {
            type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            title = "An unexpected error occurred.",
            status = StatusCodes.Status500InternalServerError,
            traceId = httpContext.TraceIdentifier
        }, cancellationToken);

        return true;
    }
}
