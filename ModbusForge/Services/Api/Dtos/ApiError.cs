namespace ModbusForge.Services.Api.Dtos;

/// <summary>
/// RFC-7807-style problem details returned on error responses.
/// Raw exception messages are never exposed; only the <see cref="Title"/> field is returned to callers.
/// </summary>
public sealed record ApiError(string Title, string? Detail = null, int? Status = null)
{
    public static ApiError BadRequest(string title) => new(title, null, 400);
    public static ApiError NotFound(string title) => new(title, null, 404);
    public static ApiError Conflict(string title) => new(title, null, 409);
    public static ApiError Unauthorized() => new("Unauthorized – valid API key required.", null, 401);
    public static ApiError TooManyRequests() => new("Too many requests. Please slow down.", null, 429);
    public static ApiError Timeout() => new("The operation timed out. Please retry.", null, 504);
    public static ApiError InternalError() => new("An unexpected error occurred. Details have been logged.", null, 500);
}
