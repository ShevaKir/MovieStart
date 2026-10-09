using MovieStart.Shared;
using MovieStart.Shared.Profile;

namespace MovieStart.Agent.Profile;

public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiRoutes.VoiceProfile, (IVoiceProfileStore profiles) => profiles.Get());

        app.MapPut(ApiRoutes.VoiceProfile, async (VoiceProfile profile, IVoiceProfileStore profiles, CancellationToken cancellationToken) =>
        {
            try
            {
                await profiles.SaveAsync(profile, cancellationToken);
                return Results.NoContent();
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        return app;
    }
}
