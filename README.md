# MovieStart

A Mac desktop app that finds movies, downloads them to a Raspberry Pi 5, and plays them on a TV connected to the Pi over HDMI.

> Status: search, downloads, library and player control are in place; not yet deployed to the Pi.

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

- Search in English, Ukrainian or Russian: TMDB for titles, Prowlarr for releases (rutracker, kinozal, rutor, Toloka).
- 1080p only; CAMRip/TS releases are filtered out.
- Release ranking: voice-over profile → source (BDRip > WEB-DL > WEBRip) → MKV → x265 10-bit → seeders.
- Filters by audio language (UKR / RUS / ENG) and voice-over type (dub, multi-voice, single-voice author, original).
- Voice-over profile (e.g. Ukrainian dub → Russian dub → English original): ranks releases and picks the audio track on playback; subtitles follow (forced ones with a dub, full ones with the original). Tracks are read with ffprobe.
- Series: download a whole series, a season or single episodes; they are merged into one episode list. While a season downloads, untick episodes to skip them; a watched episode can be deleted on its own and is not downloaded again.
- Samples, trailers and extras inside torrents are skipped automatically.
- Continue watching: the position of every file is remembered; "Continue" resumes the last episode or moves to the next one.
- Pi disk usage, deletion of whole items, single downloads or single episodes, free-space check before a download starts.
- No database: each item keeps its metadata in `item.json` next to its files on the movies disk.

## Structure

```
MovieStart.slnx
├─ MovieStart.Agent         — ASP.NET Core minimal API, linux-arm64
├─ MovieStart.Desktop       — Avalonia UI, macOS
├─ MovieStart.Shared        — DTOs and API contracts
└─ tests/                   — one xUnit project per project
```

## Requirements

- **Mac:** .NET 10 SDK.
- **Raspberry Pi 5:** Raspberry Pi OS 64-bit with the desktop (mpv draws on the TV through the labwc session), `mpv`, `qbittorrent-nox`, `ffmpeg`, `avahi-daemon`, Docker for Prowlarr; movies on a separate drive (`/mnt/media/movies`).

## Development

The agent also runs on a Mac (mpv and qBittorrent via Homebrew), so the Pi is only needed to verify TV output and to deploy.

```bash
cp MovieStart.Desktop/appsettings.Local.example.json MovieStart.Desktop/appsettings.Local.json  # point the app at localhost
dotnet run --project MovieStart.Agent     # http://localhost:5080
dotnet run --project MovieStart.Desktop
dotnet test --solution MovieStart.slnx
```

### Demo mode

Try the whole app on a Mac without the Pi, Prowlarr, qBittorrent or mpv:

```bash
./scripts/demo.sh
```

Search uses the real TMDB (put `Tmdb:ReadAccessToken` into `MovieStart.Agent/appsettings.Local.json`); releases, downloads and the TV player are simulated. A finished download is a short real video (made by ffmpeg) with several audio and subtitle tracks, so track selection and the remote can be tested. The app shows a **Demo** menu to finish downloads instantly or reset the demo library. Data lives in `/tmp/moviestart-demo`.

Local overrides go into `appsettings.Local.json` (gitignored). Port 5000 is avoided because macOS AirPlay Receiver uses it.

## Deploy to the Pi

One-time setup on the Pi:

- qBittorrent: `sudo systemctl enable --now qbittorrent-nox@sheva-server`, WebUI on port 8090 (8080 is taken on this Pi), default save path `/mnt/media/movies`.
- Prowlarr: copy `deploy/prowlarr/compose.yaml` to `/opt/prowlarr/` and run `docker compose up -d`; add indexers at `http://sheva-server.local:9696`.
- Secrets in `/etc/moviestart/agent.env` (owner `root:sheva-server`, mode 640):

```ini
QBittorrent__BaseUrl=http://localhost:8090
QBittorrent__Password=...
Prowlarr__ApiKey=...
Tmdb__ReadAccessToken=...
```

Then, from the Mac:

```bash
./scripts/deploy.sh   # publishes linux-arm64, installs /opt/moviestart, moviestart-agent.service and the mpv user unit
```

## Design

Screen mockups: [MovieStart — design](https://claude.ai/artifact/HvoKQhLp5Rc7ihU1KTyNZH).
