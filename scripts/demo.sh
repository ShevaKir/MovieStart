#!/usr/bin/env bash
# Runs the agent in demo mode and the desktop app against it.
# Search uses the real TMDB (set Tmdb:ReadAccessToken in MovieStart.Agent/appsettings.Local.json);
# trackers, downloads and the player are simulated. Closing the app stops the agent.
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet build MovieStart.slnx -nologo -v quiet

# Start the agent binary itself (not "dotnet run") so it can be stopped reliably on exit.
(
  cd MovieStart.Agent
  ASPNETCORE_ENVIRONMENT=Development \
  Demo__Enabled=true \
  Media__Root=/tmp/moviestart-demo \
  Player__SocketPath=/tmp/moviestart-demo.sock \
  Urls=http://localhost:5080 \
  exec ./bin/Debug/net10.0/MovieStart.Agent
) &
agent=$!
trap 'kill "$agent" 2>/dev/null || true' EXIT

until curl -sf http://localhost:5080/api/health >/dev/null; do
  kill -0 "$agent" 2>/dev/null || { echo "The demo agent did not start; see the output above." >&2; exit 1; }
  sleep 0.5
done

MOVIESTART_AgentUrl=http://localhost:5080 dotnet run --project MovieStart.Desktop --no-build
