#!/usr/bin/env bash
# Runs the .NET SDK in a container so the host needs no SDK installed.
#
# The repository ROOT is mounted (not just backend/) because the merge tests
# read ../shared-fixtures, which is shared with the TypeScript suite.
#
# --user maps container writes to the host user so build output is not
# root-owned. That leaves HOME as "/", which the SDK cannot write to, hence
# the explicit DOTNET_CLI_HOME and XDG_DATA_HOME on a tmpfs-ish path.
set -euo pipefail
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exec docker run --rm \
  -v "$REPO_ROOT:/repo" \
  -v f1dash-nuget:/nuget \
  -v f1dash-dotnet-home:/dotnet-home \
  -e NUGET_PACKAGES=/nuget \
  -e DOTNET_CLI_HOME=/dotnet-home \
  -e HOME=/dotnet-home \
  -e XDG_DATA_HOME=/dotnet-home \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  -e DOTNET_NOLOGO=1 \
  -e MSBUILDTERMINALLOGGER=off \
  -e DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
  -e "ARCHIVE_PATH=${ARCHIVE_PATH:-/repo/data/archive}" \
  -e "F1_HTTP_PROXY=${F1_HTTP_PROXY:-}" \
  -e "F1_LIVE_TEST=${F1_LIVE_TEST:-}" \
  --user "$(id -u):$(id -g)" \
  -w /repo/backend \
  mcr.microsoft.com/dotnet/sdk:9.0 \
  dotnet "$@"
