var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Same shape as https://jsonplaceholder.typicode.com/posts — the worker expects this schema.
var orderData = new[]
{
    new { userId = 1, id = 1,  title = "Order 1", body = "First order" },
    new { userId = 1, id = 2,  title = "Order 2", body = "Second order" },
    new { userId = 2, id = 3,  title = "Order 3", body = "Third order" },
};

// 1. Success — jsonplaceholder substitute (offline)
app.MapGet("/posts", () => Results.Json(orderData));

// 2. Always fail — exercises retry-then-fail path (AC-05)
app.MapGet("/fail/posts", () => Results.StatusCode(500));

// 3. Flaky — fails first 3 attempts, succeeds on 4th (AC-04)
int flakyCounter = 0;
app.MapGet("/flaky/posts", () =>
{
    var n = Interlocked.Increment(ref flakyCounter);
    if (n <= 3) return Results.StatusCode(500);
    return Results.Json(orderData);
});

// Optional: reset the flaky counter without restarting the process
app.MapPost("/flaky/reset", () =>
{
    Interlocked.Exchange(ref flakyCounter, 0);
    return Results.Ok(new { reset = true });
});

app.Run("http://localhost:5001");