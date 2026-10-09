using MovieStart.Agent.Downloads;
using MovieStart.Agent.Player;
using MovieStart.Shared;
using MovieStart.Shared.Library;

namespace MovieStart.Agent.Library;

public static class LibraryEndpoints
{
    public static IEndpointRouteBuilder MapLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        const string item = ApiRoutes.Library + "/{id:guid}";
        const string download = item + "/downloads/{downloadId:guid}";

        app.MapGet(ApiRoutes.Library, (LibraryService library) => library.List());
        app.MapGet(ApiRoutes.Storage, (LibraryService library) => library.GetStorage());

        app.MapPost(ApiRoutes.Library, (AddToLibraryRequest request, LibraryService library, CancellationToken cancellationToken) =>
            RunAsync(async () =>
            {
                var added = await library.AddAsync(request, cancellationToken);
                return Results.Created(ApiRoutes.LibraryItem(added.Id), added);
            }));

        app.MapDelete(item, (Guid id, LibraryService library, CancellationToken cancellationToken) =>
            RunAsync(() => library.DeleteAsync(id, cancellationToken)));

        app.MapPost(item + "/play", (Guid id, PlayItemRequest? request, LibraryService library, CancellationToken cancellationToken) =>
            RunAsync(async () => Results.Ok(await library.PlayAsync(id, request ?? new PlayItemRequest(), cancellationToken))));

        app.MapDelete(download, (Guid id, Guid downloadId, LibraryService library, CancellationToken cancellationToken) =>
            RunAsync(() => library.DeleteDownloadAsync(id, downloadId, cancellationToken)));

        app.MapPost(download + "/pause", (Guid id, Guid downloadId, LibraryService library, CancellationToken cancellationToken) =>
            RunAsync(() => library.PauseDownloadAsync(id, downloadId, cancellationToken)));

        app.MapPost(download + "/resume", (Guid id, Guid downloadId, LibraryService library, CancellationToken cancellationToken) =>
            RunAsync(() => library.ResumeDownloadAsync(id, downloadId, cancellationToken)));

        return app;
    }

    private static Task<IResult> RunAsync(Func<Task> action) =>
        RunAsync(async () =>
        {
            await action();
            return Results.NoContent();
        });

    private static async Task<IResult> RunAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (LibraryNotFoundException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (LibraryStateException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (InsufficientStorageException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status507InsufficientStorage);
        }
        catch (QbitUnavailableException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (PlayerUnavailableException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (PlayerCommandException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
