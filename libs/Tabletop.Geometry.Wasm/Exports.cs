using System.Runtime.InteropServices.JavaScript;

namespace Tabletop.Geometry.Wasm;

/// <summary>One [JSExport] per <see cref="Facade"/> method, same name and signature.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("browser")]
public static partial class Exports
{
    [JSExport] public static string LandModels(string json) => Facade.LandModels(json);
    [JSExport] public static string LandPiece(string json) => Facade.LandPiece(json);
    [JSExport] public static string TurnPiece(string json) => Facade.TurnPiece(json);
    [JSExport] public static string ModelSurfaces(string json) => Facade.ModelSurfaces(json);
    [JSExport] public static string RestModel(string json) => Facade.RestModel(json);
    [JSExport] public static string PieceSurfaces(string json) => Facade.PieceSurfaces(json);
    [JSExport] public static string RestPiece(string json) => Facade.RestPiece(json);
    [JSExport] public static string Contacts(string json) => Facade.Contacts(json);
    [JSExport] public static string OverlapBases(string json) => Facade.OverlapBases(json);
    [JSExport] public static string EdgeToEdge(string json) => Facade.EdgeToEdge(json);
    [JSExport] public static string BodyToBody(string json) => Facade.BodyToBody(json);
    [JSExport] public static string EdgeToPoint(string json) => Facade.EdgeToPoint(json);
    [JSExport] public static string EdgeToMarker(string json) => Facade.EdgeToMarker(json);
    [JSExport] public static string NearestPair(string json) => Facade.NearestPair(json);
    [JSExport] public static string RangeOutline(string json) => Facade.RangeOutline(json);
    [JSExport] public static string ZonePolygons(string json) => Facade.ZonePolygons(json);
    [JSExport] public static string ZoneContains(string json) => Facade.ZoneContains(json);
    [JSExport] public static string ZoneHoldsBase(string json) => Facade.ZoneHoldsBase(json);
    [JSExport] public static string TableHoldsBase(string json) => Facade.TableHoldsBase(json);
}
