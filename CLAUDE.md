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

- Don't hardcode the agent address: default `http://sheva-server.local:5000`, configurable in settings.
- The player sits behind an `IPlayer` interface (mpv now, Kodi possible later).
- DTOs and API contracts live only in `MovieStart.Shared`.

## UI

- Light theme, green accent `#18794E`. Yellow is for warnings only.
- Mockups: https://claude.ai/artifact/HvoKQhLp5Rc7ihU1KTyNZH
