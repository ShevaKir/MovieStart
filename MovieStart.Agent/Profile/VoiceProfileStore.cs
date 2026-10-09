using System.Text.Json;
using Microsoft.Extensions.Options;
using MovieStart.Agent.Media;
using MovieStart.Shared.Profile;

namespace MovieStart.Agent.Profile;

public interface IVoiceProfileStore
{
    VoiceProfile Get();

    Task SaveAsync(VoiceProfile profile, CancellationToken cancellationToken);
}

/// <summary>Keeps the profile in <c>{Media:Root}/profile.json</c>, next to the library.</summary>
public sealed class FileVoiceProfileStore(IOptions<MediaOptions> media, ILogger<FileVoiceProfileStore> logger) : IVoiceProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly Lock _lock = new();
    private VoiceProfile? _profile;

    private string FilePath => Path.Combine(media.Value.Root, "profile.json");

    public VoiceProfile Get()
    {
        lock (_lock)
            return _profile ??= Load();
    }

    public async Task SaveAsync(VoiceProfile profile, CancellationToken cancellationToken)
    {
        Validate(profile);
        Directory.CreateDirectory(media.Value.Root);
        var temp = FilePath + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, profile, JsonOptions, cancellationToken);
        File.Move(temp, FilePath, overwrite: true);

        lock (_lock)
            _profile = profile;
    }

    private VoiceProfile Load()
    {
        if (!File.Exists(FilePath))
            return VoiceProfile.Default;

        try
        {
            return JsonSerializer.Deserialize<VoiceProfile>(File.ReadAllText(FilePath), JsonOptions) ?? VoiceProfile.Default;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Unreadable {File}; using the default voice profile", FilePath);
            return VoiceProfile.Default;
        }
    }

    private static void Validate(VoiceProfile profile)
    {
        if (profile.Audio.Any(preference => string.IsNullOrWhiteSpace(preference.Language)))
            throw new ArgumentException("Every audio preference needs a language.");
    }
}
