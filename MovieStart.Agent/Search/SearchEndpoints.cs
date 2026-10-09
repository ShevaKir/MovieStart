using MovieStart.Shared;
using MovieStart.Shared.Library;

namespace MovieStart.Agent.Search;

public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiRoutes.SearchTitles, (string? query, SearchService search, CancellationToken cancellationToken) =>
            RunAsync(async () => Results.Ok(await search.SearchTitlesAsync(query ?? string.Empty, cancellationToken))));

        app.MapGet(ApiRoutes.SearchReleases, (
                MediaKind kind, string? title, string? originalTitle, int? year, string? query,
                SearchService search, CancellationToken cancellationToken) =>
            RunAsync(async () => Results.Ok(await search.SearchReleasesAsync(
                new ReleaseSearch(kind, title ?? string.Empty, originalTitle, year, query), cancellationToken))));

        return app;
    }

    private static async Task<IResult> RunAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (SearchUnavailableException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
