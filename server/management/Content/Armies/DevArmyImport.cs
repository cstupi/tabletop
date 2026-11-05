using System.Security.Claims;
using System.Text.Json;
using Tabletop.Content;
using Tabletop.Management.Http;

namespace Tabletop.Management.Content.Armies;

/// <summary>Source: what the army records as its source (the import stores the share link there). List: an Army Forge export body. Edition: one of ArmyEditions.All the army is stamped with; none is the served edition.</summary>
public sealed record DevArmyImportRequest(string Source, JsonElement List, string? Edition = null);

public static class DevArmyImportEndpoint
{
    /// <summary>
    /// Mapped only in dev mode (StartupGuard.DevMode), beside the session mint. `/v1/armies/import` without the fetch: the list comes in the
    /// body, so the simulator's lifecycle suite imports over HTTP without calling Army Forge. Everything after the fetch is the import's own
    /// (ArmyEndpoints.ImportAsync).
    /// </summary>
    public static void MapDevArmyImport(this IEndpointRouteBuilder app) =>
        app.MapPost("/v1/dev/armies/import", async (ClaimsPrincipal u, DevArmyImportRequest r, ICatalogKinds kinds, IArmyRepository armies, ILogger<ArmyForgeClient> log) =>
        {
            if (Limits.Text(r.Source, "source", Limits.Source) is { } badSource) return Problems.Unprocessable(badSource);
            if (r.List.ValueKind != JsonValueKind.Object) return Problems.Unprocessable(ArmyEndpoints.MalformedExport);
            if (r.Edition is not null && !ArmyEditions.All.Contains(r.Edition)) return Problems.Unprocessable($"Edition must be one of {string.Join(", ", ArmyEditions.All)}.");
            var body = r.List.GetRawText();
            return await ArmyEndpoints.ImportAsync(CurrentAccount.Id(u), r.Source, "(body)", () => Task.FromResult(ArmyForgeClient.Parse(body)), kinds, armies, log, r.Edition ?? ArmyEditions.Served);
        }).RequireAuthorization();
}
