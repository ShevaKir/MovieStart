using MovieStart.Agent.Player;

namespace MovieStart.Agent.Tests;

public sealed class FakeAudioOutput : IAudioOutput
{
    /// <summary>What the next <see cref="RestoreAsync"/> reports.</summary>
    public bool Restarts { get; set; }

    public Task<bool> RestoreAsync(CancellationToken cancellationToken) => Task.FromResult(Restarts);
}
