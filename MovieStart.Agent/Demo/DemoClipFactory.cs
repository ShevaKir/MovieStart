using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MovieStart.Shared.Search;

namespace MovieStart.Agent.Demo;

/// <summary>Track to put into a generated clip.</summary>
public sealed record DemoTrack(string Language, string Title);

/// <summary>
/// Makes small but real MKV files with several audio and subtitle tracks, so ffprobe, track selection
/// and deletion work on actual files. Each track layout is rendered once and then copied.
/// </summary>
public sealed class DemoClipFactory(IOptions<DemoOptions> options, ILogger<DemoClipFactory> logger)
{
    private readonly string _cache = Path.Combine(Path.GetTempPath(), "moviestart-demo-clips");
    private readonly SemaphoreSlim _render = new(1, 1);
    private bool _reportedMissing;

    /// <summary>Tracks a release would have, read from its title the same way the agent reads real releases.</summary>
    public static (IReadOnlyList<DemoTrack> Audio, IReadOnlyList<DemoTrack> Subtitles) TracksFor(ReleaseInfoLike release)
    {
        var audio = release.Audio.Select(track => new DemoTrack(track.Language, TrackTitle(track))).ToList();
        if (audio.Count == 0)
            audio.Add(new DemoTrack("eng", "Original"));

        var subtitles = release.Subtitles.Select(language => new DemoTrack(language, "Full")).ToList();

        // Dubbed releases usually carry forced subtitles for signs; they exercise the "forced only" rule.
        var dub = release.Audio.FirstOrDefault(track => track.Type == VoiceType.Dub);
        if (dub is not null)
            subtitles.Insert(0, new DemoTrack(dub.Language, "Forced"));

        return (audio, subtitles);
    }

    public async Task CreateAsync(string path, IReadOnlyList<DemoTrack> audio, IReadOnlyList<DemoTrack> subtitles, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var template = await RenderAsync(audio, subtitles, cancellationToken);
        if (template is null)
        {
            // Without ffmpeg the file is empty: the flow still works, ffprobe just finds no tracks.
            await File.WriteAllBytesAsync(path, [], cancellationToken);
            return;
        }

        File.Copy(template, path, overwrite: true);
    }

    private async Task<string?> RenderAsync(IReadOnlyList<DemoTrack> audio, IReadOnlyList<DemoTrack> subtitles, CancellationToken cancellationToken)
    {
        var seconds = options.Value.ClipSeconds;
        var key = string.Join("|", audio.Select(t => $"a:{t.Language}:{t.Title}").Concat(subtitles.Select(t => $"s:{t.Language}:{t.Title}"))) + $"|{seconds}";
        var file = Path.Combine(_cache, Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(key)))[..16] + ".mkv");

        await _render.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(file))
                return file;

            Directory.CreateDirectory(_cache);
            var srt = Path.Combine(_cache, "demo.srt");
            await File.WriteAllTextAsync(srt, "1\n00:00:01,000 --> 00:00:10,000\nMovieStart demo\n", cancellationToken);

            var arguments = new List<string> { "-y", "-v", "error", "-f", "lavfi", "-i", $"testsrc2=size=320x180:rate=5:duration={seconds}" };
            for (var i = 0; i < audio.Count; i++)
                arguments.AddRange(["-f", "lavfi", "-i", $"sine=frequency={330 + 110 * i}:sample_rate=8000:duration={seconds}"]);
            foreach (var _ in subtitles)
                arguments.AddRange(["-i", srt]);

            arguments.AddRange(["-map", "0:v"]);
            for (var i = 0; i < audio.Count; i++)
                arguments.AddRange(["-map", $"{i + 1}:a"]);
            for (var i = 0; i < subtitles.Count; i++)
                arguments.AddRange(["-map", $"{audio.Count + 1 + i}:s"]);

            arguments.AddRange(["-c:v", "libx264", "-preset", "ultrafast", "-crf", "40", "-c:a", "aac", "-b:a", "16k", "-c:s", "srt"]);
            for (var i = 0; i < audio.Count; i++)
                arguments.AddRange([$"-metadata:s:a:{i}", $"language={audio[i].Language}", $"-metadata:s:a:{i}", $"title={audio[i].Title}"]);
            for (var i = 0; i < subtitles.Count; i++)
                arguments.AddRange([$"-metadata:s:s:{i}", $"language={subtitles[i].Language}", $"-metadata:s:s:{i}", $"title={subtitles[i].Title}"]);
            arguments.Add(file);

            return await RunFfmpegAsync(arguments, cancellationToken) ? file : null;
        }
        finally
        {
            _render.Release();
        }
    }

    private async Task<bool> RunFfmpegAsync(List<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(options.Value.FfmpegPath) { RedirectStandardError = true };
        arguments.ForEach(start.ArgumentList.Add);
        try
        {
            using var process = Process.Start(start)!;
            var errors = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode == 0)
                return true;
            logger.LogWarning("ffmpeg failed to render a demo clip: {Errors}", await errors);
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            if (!_reportedMissing)
                logger.LogWarning("ffmpeg is not available; demo downloads will be empty files");
            _reportedMissing = true;
            return false;
        }
    }

    private static string TrackTitle(ReleaseAudio track)
    {
        var type = track.Type switch
        {
            VoiceType.Dub => "Дубляж",
            VoiceType.Mvo => "MVO",
            VoiceType.Dvo => "DVO",
            VoiceType.Avo => "AVO",
            VoiceType.Vo => "VO",
            _ => "Original",
        };
        return track.Studio is null ? type : $"{type}, {track.Studio}";
    }
}

/// <summary>The parts of a parsed release the clip factory needs.</summary>
public sealed record ReleaseInfoLike(IReadOnlyList<ReleaseAudio> Audio, IReadOnlyList<string> Subtitles);
