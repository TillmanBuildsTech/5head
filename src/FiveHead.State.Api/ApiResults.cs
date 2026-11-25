namespace FiveHead.State.Api;

public static class ApiResults
{
    public static IResult From(MutationResult result) => result.Kind switch
    {
        MutationKind.Ok => Results.Ok(result.Matter!.ToResponse()),
        MutationKind.NotFound => Results.NotFound(new { error = "not_found", message = result.Message }),
        MutationKind.Conflict => Results.Json(
            new
            {
                error = string.IsNullOrEmpty(result.Code) ? "conflict" : result.Code,
                message = result.Message,
                matter = result.Matter!.ToResponse()
            },
            statusCode: StatusCodes.Status409Conflict),
        MutationKind.Forbidden => Results.Json(
            new { error = "forbidden", message = result.Message, matter = result.Matter!.ToResponse() },
            statusCode: StatusCodes.Status403Forbidden),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
    };

    public static IResult BadJson() =>
        Results.BadRequest(new { error = "bad_request", message = "Body must be a JSON object." });

    public static IResult Unauthorized() =>
        Results.Json(
            new { error = "unauthorized", message = "Bearer token was missing or rejected." },
            statusCode: StatusCodes.Status401Unauthorized);

    public static IResult NotFoundBody() =>
        Results.Json(
            new { error = "not_found", message = "Not found." },
            statusCode: StatusCodes.Status404NotFound);
}
