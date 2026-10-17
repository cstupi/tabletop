namespace Tabletop.Management.Http;

/// <summary>Every request body is JSON (asset bytes go straight to the blob store), so none needs more than MaxBytes. A declared
/// Content-Length above it is refused before reading; a body without one is read up to MaxBytes + 1 into memory and refused when
/// it reaches past it. The refusal is 413.</summary>
public sealed class BodySizeLimit(RequestDelegate next)
{
    public const long MaxBytes = 1024 * 1024;

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } f) f.MaxRequestBodySize = MaxBytes;
        if (ctx.Request.ContentLength is { } declared)
        {
            if (declared > MaxBytes) { await TooLarge(ctx); return; }
        }
        else if (HasBody(ctx.Request))
        {
            var buffered = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int n;
            while ((n = await ctx.Request.Body.ReadAsync(chunk, ctx.RequestAborted)) > 0)
            {
                if (buffered.Length + n > MaxBytes) { await TooLarge(ctx); return; }
                buffered.Write(chunk, 0, n);
            }
            buffered.Position = 0;
            ctx.Request.Body = buffered;
            ctx.Request.ContentLength = buffered.Length;
        }
        await next(ctx);
    }

    private static bool HasBody(HttpRequest r) => r.Headers.TransferEncoding.Count > 0 || !(HttpMethods.IsGet(r.Method) || HttpMethods.IsHead(r.Method) || HttpMethods.IsDelete(r.Method) || HttpMethods.IsOptions(r.Method));

    private static Task TooLarge(HttpContext ctx) =>
        Results.Problem(title: "Request too large", detail: $"A request body is at most {MaxBytes:N0} bytes.", statusCode: StatusCodes.Status413PayloadTooLarge).ExecuteAsync(ctx);
}
