namespace Tabletop.Management.Http;

/// <summary>RFC 7807 bodies. Another owner's row is a 404 like a missing one; existence is not disclosed.</summary>
public static class Problems
{
    public static IResult NotFound() => Results.Problem(title: "Not found", statusCode: StatusCodes.Status404NotFound);
    public static IResult Conflict(string detail) => Results.Problem(title: "Conflict", detail: detail, statusCode: StatusCodes.Status409Conflict);
    public static IResult Unprocessable(string detail) => Results.Problem(title: "Unprocessable", detail: detail, statusCode: StatusCodes.Status422UnprocessableEntity);
    public static IResult BadGateway(string detail) => Results.Problem(title: "Upstream failure", detail: detail, statusCode: StatusCodes.Status502BadGateway);
}
