# CLAUDE.md

## Project

MovieStart — a Mac app (Avalonia, .NET 10) plus an agent on a Raspberry Pi 5 (ASP.NET Core, linux-arm64) that drives Prowlarr, qBittorrent and mpv. Download first, then watch: no streaming while downloading. 1080p only. Overview in `README.md`; full spec in `docs/SPEC.md` (local only).

## Git

- Commits are authored only by the repository owner (current `git config user.name` / `user.email`).
- **No** `Co-Authored-By` lines and no Claude / AI attribution in commits or PRs.
- Everything committed is in **English**: commit messages, code comments, docs.
- Commit messages follow Conventional Commits: `feat: add mpv remote control`.
- Never commit `docs/SPEC.md`.

## Code

- Don't hardcode the agent address: default `http://sheva-server.local:5080`, configured via `appsettings.json`.
- The player sits behind an `IPlayer` interface (mpv now, Kodi possible later).
- DTOs and API contracts live only in `MovieStart.Shared`.
- Config: committed `appsettings.json`; personal local overrides in `appsettings.Local.json` (gitignored, template in `appsettings.Local.example.json`); env vars win on deploy (Desktop uses the `MOVIESTART_` prefix).
- Demo mode (`Demo:Enabled`, launch profile `demo`, `scripts/demo.sh`) swaps Prowlarr, qBittorrent and mpv for simulations in `MovieStart.Agent/Demo`; TMDB and ffprobe stay real. Keep it working when those interfaces change.
- Every project has its own test project under `tests/` (xUnit v3 on Microsoft.Testing.Platform). Run `dotnet test --solution MovieStart.slnx`.

## UI

- App UI language: English. Search must accept queries in Russian, Ukrainian and English.
- Light theme, green accent `#18794E`. Yellow is for warnings only.
- Mockups: https://claude.ai/artifact/HvoKQhLp5Rc7ihU1KTyNZH
