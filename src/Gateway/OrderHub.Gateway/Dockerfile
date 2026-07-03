# syntax=docker/dockerfile:1.7
# Gateway = SAF EDGE (YARP reverse proxy). Stateless: DB/EF/migration YOK, servis/Contracts ProjectReference YOK.
# → restore-katmanı COPY listesi MİNİMAL: yalnız Gateway'in kendi csproj + lock (stale-Dockerfile riski yok, bağımsız).

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build
WORKDIR /src

COPY ["Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["src/Gateway/OrderHub.Gateway/OrderHub.Gateway.csproj", "src/Gateway/OrderHub.Gateway/"]
# ★ Gateway-scoped Directory.Packages.props (6a-2): NuGet EN YAKIN props'u kullanır → bu dosya olmadan container
#   root'un 40-pin setine düşer (Yarp 1.0.0 çöküşü / --locked-mode uyuşmazlığı). Lock ile birlikte kopyalanmalı.
COPY ["src/Gateway/OrderHub.Gateway/Directory.Packages.props", "src/Gateway/OrderHub.Gateway/"]
COPY ["src/Gateway/OrderHub.Gateway/packages.lock.json", "src/Gateway/OrderHub.Gateway/"]

# --locked-mode: lock dosyasına birebir uyum (reproducible; sürpriz transitive sürüm yok).
RUN dotnet restore "src/Gateway/OrderHub.Gateway/OrderHub.Gateway.csproj" --locked-mode

COPY . .
RUN dotnet publish "src/Gateway/OrderHub.Gateway/OrderHub.Gateway.csproj" \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---------- final ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0.11-bookworm-slim AS final
WORKDIR /app

# Healthcheck için curl (bookworm-slim'de yok); apt cache temizlenir.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Serilog File sink logs/'a yazar; non-root yazabilsin diye dizin oluşturulup app kullanıcısına devredilir.
RUN mkdir -p /app/logs && chown "$APP_UID" /app/logs

# .NET 8 base image'da tanımlı non-root kullanıcı (uid 1654). İlk container'dan itibaren non-root (K3).
USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Liveness: gateway ayakta mı (downstream'e bağlamaz → downstream blip'inde unhealthy olmasın).
HEALTHCHECK --interval=15s --timeout=3s --start-period=20s --retries=3 \
    CMD curl -f http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "OrderHub.Gateway.dll"]
