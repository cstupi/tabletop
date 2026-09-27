using Tabletop.Protocol;

namespace Tabletop.Management.Http;

public enum ClientVersionVerdict { Accept, UpgradeRequired, Malformed }

public static class ClientVersion
{
    public static ClientVersionVerdict Decide(string? header, int minimum) => header switch
    {
        null => ClientVersionVerdict.Accept,
        _ when int.TryParse(header, out var v) => v >= minimum ? ClientVersionVerdict.Accept : ClientVersionVerdict.UpgradeRequired,
        _ => ClientVersionVerdict.Malformed,
    };
}

/// <summary>Reads Tabletop-Client-Version on every request; a client below the minimum is told to update and nothing else runs.</summary>
public sealed class ClientVersionMiddleware(RequestDelegate next, Configuration.ManagementOptions options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var verdict = ClientVersion.Decide(context.Request.Headers[ProtocolVersion.Header].FirstOrDefault(), options.MinimumClientVersion);
        if (verdict == ClientVersionVerdict.Accept) { await next(context); return; }
        var status = verdict == ClientVersionVerdict.UpgradeRequired ? ProtocolVersion.UpgradeRequiredStatusCode : StatusCodes.Status400BadRequest;
        await Results.Problem(
            title: verdict == ClientVersionVerdict.UpgradeRequired ? "Update required" : "Malformed client version",
            detail: $"This server accepts {ProtocolVersion.Header} >= {options.MinimumClientVersion}.",
            statusCode: status).ExecuteAsync(context);
    }
}
