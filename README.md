# MovieStart

A Mac desktop app that finds movies, downloads them to a Raspberry Pi 5, and plays them on a TV connected to the Pi over HDMI.

> Status: early stage — design. No code yet.

## How it works

```
Mac (MovieStart.Desktop)                Raspberry Pi 5 (sheva-server.local)
┌──────────────────────┐  HTTP +        ┌──────────────────────────────────────┐
│ search, library,     │  SignalR       │ MovieStart.Agent (ASP.NET Core)      │
│ remote control       │ ─────────────▶ │  ├─ Prowlarr      — release search   │
└──────────┬───────────┘                │  ├─ qBittorrent   — downloads        │
           │                            │  ├─ mpv (DRM/KMS) — playback         │──HDMI──▶ TV
           ▼                            │  └─ SQLite        — library          │
       TMDB API                         └──────────────────────────────────────┘
```

1. Search for a movie on the Mac — posters and details come from TMDB.
2. Pick a 1080p release with the audio language and voice-over you want.
3. The movie downloads to the Pi; progress shows in the library.
4. Once it's ready, start it on the TV and control playback from the Mac: pause, seek, volume, audio tracks, subtitles.
5. After watching, delete the movie to free up space on the Pi.

## Features

- 1080p only; CAMRip/TS releases are filtered out.
- Release ranking: voice-over profile → source (BDRip > WEB-DL > WEBRip) → MKV → x265 10-bit → seeders.
- Filters by audio language (UKR / RUS / ENG) and voice-over type (dub, multi-voice, single-voice author, original).
- Preferred audio track is selected automatically on playback.
- Pi disk usage, movie deletion, free-space check before a download starts.

## Structure

```
MovieStart.slnx
├─ src/MovieStart.Desktop   — Avalonia UI, macOS
├─ src/MovieStart.Agent     — ASP.NET Core minimal API, linux-arm64
└─ src/MovieStart.Shared    — DTOs and API contracts
```

## Requirements

- **Mac:** .NET 10 SDK.
- **Raspberry Pi 5:** Raspberry Pi OS Lite 64-bit, `mpv`, `qbittorrent-nox`, `ffmpeg`, `avahi-daemon`, Prowlarr; external USB drive for movies; active cooling.

## Development

The agent also runs on a Mac (mpv and qBittorrent via Homebrew), so the Pi is only needed to verify TV output and to deploy.

```bash
# deploy the agent to the Pi
dotnet publish src/MovieStart.Agent -c Release -r linux-arm64 --self-contained -o out
rsync -a out/ <user>@sheva-server.local:/opt/moviestart/
ssh <user>@sheva-server.local sudo systemctl restart moviestart-agent
```

## Design

Screen mockups: [MovieStart — design](https://claude.ai/artifact/HvoKQhLp5Rc7ihU1KTyNZH).
