namespace Tabletop.Management.Http;

/// <summary>RFC 7807 bodies. Another owner's row is a 404 like a missing one; existence is not disclosed.</summary>
public static class Problems
{
    public static IResult NotFound() => Results.Problem(title: "Not found", statusCode: StatusCodes.Status404NotFound);
    public static IResult Unauthorized() => Results.Problem(title: "Unauthorized", statusCode: StatusCodes.Status401Unauthorized);
    public static IResult Conflict(string detail) => Results.Problem(title: "Conflict", detail: detail, statusCode: StatusCodes.Status409Conflict);
    public static IResult Unprocessable(string detail) => Results.Problem(title: "Unprocessable", detail: detail, statusCode: StatusCodes.Status422UnprocessableEntity);
    public static IResult BadGateway(string detail) => Results.Problem(title: "Upstream failure", detail: detail, statusCode: StatusCodes.Status502BadGateway);
    /// <summary>Every 429 names when to retry, in seconds, as Retry-After.</summary>
    public static IResult TooManyRequests(string detail, int retryAfterSeconds = 60) => new RetryAfterResult(
        Results.Problem(title: "Too many requests", detail: detail, statusCode: StatusCodes.Status429TooManyRequests), retryAfterSeconds);

    private sealed class RetryAfterResult(IResult inner, int seconds) : IResult
    {
        public Task ExecuteAsync(HttpContext ctx)
        {
            ctx.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return inner.ExecuteAsync(ctx);
        }
    }
}
