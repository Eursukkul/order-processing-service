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
builder.Services.AddScoped<GetOrderHandler>();
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

// Exposes the entry point to WebApplicationFactory in the integration tests.
public partial class Program;

internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        int status;
        string title;

        // Missing or malformed body: the framework raises BadHttpRequestException carrying
        // the right status (400). That is the client's fault, not a server fault.
        if (exception is BadHttpRequestException badRequest)
        {
            logger.LogWarning("Bad request on {Method} {Path}: {Reason}",
                httpContext.Request.Method, httpContext.Request.Path, badRequest.Message);

            status = badRequest.StatusCode;
            title = "The request body is missing or malformed.";
        }
        else
        {
            logger.LogError(exception, "Unhandled exception on {Method} {Path}.",
                httpContext.Request.Method, httpContext.Request.Path);

            status = StatusCodes.Status500InternalServerError;
            title = "An unexpected error occurred.";
        }

        // Written through the same ProblemDetails service as every other error response, so
        // it gets application/problem+json, the RFC type link, and a traceId. The exception
        // is deliberately not attached: its details belong in the log, not the payload.
        httpContext.Response.StatusCode = status;
        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = { Status = status, Title = title }
        });

        return true;
    }
}
