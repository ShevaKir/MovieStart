using Microsoft.Extensions.Options;
using MovieStart.Agent.Media;
using MovieStart.Shared;
using MovieStart.Shared.Player;

namespace MovieStart.Agent.Player;

public static class PlayerEndpoints
{
    public static IEndpointRouteBuilder MapPlayerEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiRoutes.PlayerState, (IPlayer player) => player.State);
        app.MapPost(ApiRoutes.PlayerPlay, PlayAsync);
        app.MapPost(ApiRoutes.PlayerCommand, SendCommandAsync);
        return app;
    }

    private static async Task<IResult> PlayAsync(
        PlayRequest request, IPlayer player, IOptions<MediaOptions> media, CancellationToken cancellationToken)
    {
        if (!MediaPath.TryResolve(media.Value.Root, request.Path, out var fullPath))
            return Results.Problem("The file must be inside the media root.", statusCode: StatusCodes.Status400BadRequest);

        if (!File.Exists(fullPath))
            return Results.Problem("File not found.", statusCode: StatusCodes.Status404NotFound);

        return await RunAsync(() => player.PlayAsync(fullPath, new PlaybackOptions(), cancellationToken));
    }

    private static Task<IResult> SendCommandAsync(PlayerCommand command, IPlayer player, CancellationToken cancellationToken) =>
        RunAsync(() => player.SendAsync(command, cancellationToken));

    private static async Task<IResult> RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            return Results.NoContent();
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
