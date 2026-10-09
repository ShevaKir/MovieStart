using System.Net;
using System.Net.Http.Json;
using MovieStart.Shared;
using MovieStart.Shared.Profile;
using MovieStart.Shared.Search;

namespace MovieStart.Agent.Tests;

public sealed class ProfileEndpointsTests : IDisposable
{
    private readonly AgentFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StartsWithTheDefaultProfile()
    {
        using var client = _factory.CreateClient();

        var profile = await client.GetFromJsonAsync<VoiceProfile>(ApiRoutes.VoiceProfile, Token);

        Assert.Equal(VoiceProfile.Default.Audio, profile!.Audio);
    }

    [Fact]
    public async Task SavedProfileIsKeptOnDisk()
    {
        using var client = _factory.CreateClient();
        var profile = new VoiceProfile([new VoicePreference("rus", VoiceType.Mvo, "HDRezka")], ["eng"]);

        var response = await client.PutAsJsonAsync(ApiRoutes.VoiceProfile, profile, Token);
        var saved = await client.GetFromJsonAsync<VoiceProfile>(ApiRoutes.VoiceProfile, Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(profile.Audio, saved!.Audio);
        Assert.Equal(["eng"], saved.SubtitleLanguages);
        Assert.True(File.Exists(Path.Combine(_factory.MediaRoot, "profile.json")));
    }

    [Fact]
    public async Task RejectsPreferenceWithoutLanguage()
    {
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(ApiRoutes.VoiceProfile, new VoiceProfile([new VoicePreference("")], []), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
