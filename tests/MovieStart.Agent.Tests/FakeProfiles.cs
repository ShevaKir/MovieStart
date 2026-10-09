using MovieStart.Agent.Profile;
using MovieStart.Shared.Profile;

namespace MovieStart.Agent.Tests;

public sealed class FakeProfiles : IVoiceProfileStore
{
    public VoiceProfile Profile { get; set; } = VoiceProfile.Default;

    public VoiceProfile Get() => Profile;

    public Task SaveAsync(VoiceProfile profile, CancellationToken cancellationToken)
    {
        Profile = profile;
        return Task.CompletedTask;
    }
}
