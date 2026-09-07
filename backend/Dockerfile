# syntax=docker/dockerfile:1

# ---- build ----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

# Restore against the project files alone so a source-only change does not
# invalidate the package layer.
COPY Directory.Build.props ./
COPY src/F1Dash.Core/F1Dash.Core.csproj      src/F1Dash.Core/
COPY src/F1Dash.Server/F1Dash.Server.csproj  src/F1Dash.Server/
RUN dotnet restore src/F1Dash.Server/F1Dash.Server.csproj

COPY src/ src/
RUN dotnet publish src/F1Dash.Server/F1Dash.Server.csproj \
      -c Release -o /app --no-restore

# ---- runtime --------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS runtime
WORKDIR /app

# Non-root. The archive volume is chowned by compose.
RUN addgroup -S f1 && adduser -S -G f1 f1
COPY --from=build /app ./
RUN mkdir -p /data/archive && chown -R f1:f1 /data

USER f1
EXPOSE 4000

ENV ASPNETCORE_URLS=http://0.0.0.0:4000 \
    ARCHIVE_PATH=/data/archive \
    DOTNET_gcServer=1

# 127.0.0.1, not localhost: inside the container localhost resolves to ::1
# first, Kestrel binds IPv4 0.0.0.0, and the check fails with "connection
# refused" while the service is perfectly healthy from outside.
HEALTHCHECK --interval=10s --timeout=3s --start-period=15s --retries=5 \
  CMD wget -qO- http://127.0.0.1:4000/health || exit 1

ENTRYPOINT ["dotnet", "F1Dash.Server.dll"]
