#!/usr/bin/env bash
# Publishes the agent and installs it on the Pi with its systemd units.
set -euo pipefail

host="${MOVIESTART_PI:-sheva-server}"
root="$(cd "$(dirname "$0")/.." && pwd)"
out="$root/MovieStart.Agent/bin/publish-linux-arm64"

dotnet publish "$root/MovieStart.Agent" -c Release -r linux-arm64 --self-contained -o "$out"

ssh "$host" 'sudo install -d -o "$USER" -g "$USER" /opt/moviestart ~/.config/systemd/user'
rsync -a --delete --exclude appsettings.Local.json "$out/" "$host:/opt/moviestart/"
scp -q "$root/deploy/moviestart-agent.service" "$host:/tmp/"
scp -q "$root/deploy/mpv.service" "$host:.config/systemd/user/"

ssh "$host" '
  sudo install -m 644 /tmp/moviestart-agent.service /etc/systemd/system/
  sudo systemctl daemon-reload
  sudo systemctl enable moviestart-agent
  sudo systemctl restart moviestart-agent
  systemctl --user daemon-reload
  systemctl --user enable --now mpv
'
echo "Deployed. Health: curl http://$host.local:5080/api/health"
