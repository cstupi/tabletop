using System.Security.Claims;
using System.Text.Json;
using Tabletop.Content;
using Tabletop.Management.Content.Assets;
using Tabletop.Management.Http;

namespace Tabletop.Management.Content.Armies;

public static class ArmyEndpoints
{
    // Fixed problem details: what Army Forge or the network said goes to the log, never to the caller.
    public const string NoSuchList = "Army Forge did not return that list.";
    public const string Unreachable = "Army Forge could not be reached.";
    public const string MalformedExport = "Army Forge's export of that list could not be read.";

    public static void MapArmies(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/armies").RequireAuthorization();

        g.MapPost("/import", async (ClaimsPrincipal u, ImportRequest r, ArmyForgeClient forge, ICatalogKinds kinds, IArmyRepository armies, ILogger<ArmyForgeClient> log) =>
        {
            if (Limits.Text(r.Source, "source", Limits.Source) is { } badSource) return Problems.Unprocessable(badSource);
            var listId = ArmyForgeClient.ListId(r.Source);
            if (listId is null) return Problems.Unprocessable("source must be an Army Forge share link or list id");
            return await ImportAsync(CurrentAccount.Id(u), r.Source, listId, () => forge.Fetch(listId), kinds, armies, log);
        });

        g.MapGet("", async (ClaimsPrincipal u, IArmyRepository armies, int? limit, Guid? after) =>
        {
            var page = Keyset.Parse(limit, after);
            return Results.Ok(Page<ArmySummary>.Of(await armies.List(CurrentAccount.Id(u), page), page.Limit, a => a.Id));
        });
        g.MapGet("/{id:guid}", async (ClaimsPrincipal u, Guid id, IArmyRepository armies) => await armies.Get(CurrentAccount.Id(u), id) is { } a ? Results.Ok(a) : Problems.NotFound());
        g.MapPatch("/{id:guid}", async (ClaimsPrincipal u, Guid id, PatchArmyRequest p, IArmyRepository armies, IAssetRepository assets) =>
        {
            if (ArmyLimits.Patch(p) is { } bad) return Problems.Unprocessable(bad);
            var owner = CurrentAccount.Id(u);
            var refs = (p.Units ?? []).Where(x => x.AssetId is not null).Select(x => (x.AssetId!.Value, AssetReferences.Miniature))
                .Concat((p.Slots ?? []).Where(x => !x.ClearAsset && x.AssetId is not null).Select(x => (x.AssetId!.Value, AssetReferences.Miniature)))
                .ToList();
            if (await AssetReferences.Check(assets, owner, refs) is { } badAsset) return Problems.Unprocessable(badAsset);
            return await armies.Patch(owner, id, p) is { } a ? Results.Ok(a) : Problems.NotFound();
        });
        g.MapDelete("/{id:guid}", async (ClaimsPrincipal u, Guid id, IArmyRepository armies) => await armies.Delete(CurrentAccount.Id(u), id) ? Results.NoContent() : Problems.NotFound());
    }

    /// <summary>
    /// An import once the source is known good: the account's cap, the list (Army Forge's fetch, or the dev route's body), its game
    /// system, the mapping, the insert. Every problem is the fixed text above or the mapping's own.
    /// </summary>
    internal static async Task<IResult> ImportAsync(Guid owner, string source, string listId, Func<Task<AfList>> read,
        ICatalogKinds kinds, IArmyRepository armies, ILogger log)
    {
        if (await armies.Count(owner) >= Limits.PerAccount) return Problems.Unprocessable($"An account keeps at most {Limits.PerAccount} armies.");
        Army army;
        try
        {
            var list = await read();
            if (!ArmyForgeImport.GameRules.TryGetValue(list.GameSystem ?? "", out var gameRules)) return Problems.Unprocessable($"Only Grimdark Future and Age of Fantasy lists import; this list is '{Printable(list.GameSystem)}'.");
            army = ArmyForgeImport.ToArmy(list, source, await kinds.For(gameRules));
        }
        catch (ArmyForgeException e)
        {
            log.LogWarning(e, "Army Forge fetch of list {ListId} failed", listId);
            return Problems.BadGateway(e.Status is null ? Unreachable : NoSuchList);
        }
        catch (InvalidArmyException e) { return Problems.Unprocessable(e.Message); }
        catch (JsonException e)
        {
            log.LogWarning(e, "Army Forge export of list {ListId} is malformed", listId);
            return Problems.Unprocessable(MalformedExport);
        }
        await armies.Insert(owner, army);
        return Results.Created($"/v1/armies/{army.Id}", army);
    }

    /// <summary>Army Forge's game-system code, quoted back only when it would pass as a name.</summary>
    private static string Printable(string? s) => Limits.Text(s, "game system", 16) is null ? s! : "(unprintable)";
}
