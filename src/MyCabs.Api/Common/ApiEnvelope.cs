namespace MyCabs.Api.Common;

public record ApiError(string Code, string Message, IDictionary<string, string[]>? Fields = null, object? Details = null);

public record ApiEnvelope(
    bool Success,
    object? Data,
    ApiError? Error,
    string TraceId,
    object? Meta = null
)
{
    public static ApiEnvelope Ok(HttpContext ctx, object? data = null, object? meta = null)
        => new(true, data, null, ctx.TraceIdentifier, meta);

    public static ApiEnvelope Fail(
        HttpContext ctx,
        string code,
        string message,
        int statusCode,
        IDictionary<string, string[]>? fields = null,
        object? details = null
    )
        => new(false, null, new ApiError(code, message, fields, details), ctx.TraceIdentifier);
}