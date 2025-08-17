using MyCabs.Api.Common;

namespace MyCabs.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IWebHostEnvironment _env;
    public ExceptionHandlingMiddleware(RequestDelegate next, IWebHostEnvironment env)
    { _next = next; _env = env; }

    public async Task Invoke(HttpContext context)
    {
        try { await _next(context); }
        catch (Exception ex)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            var details = _env.IsDevelopment() ? new { ex.Message, ex.StackTrace } : null;
            var payload = ApiEnvelope.Fail(context, "INTERNAL_SERVER_ERROR", "Something went wrong", 500, null, details);
            await context.Response.WriteAsJsonAsync(payload);
        }
    }
}