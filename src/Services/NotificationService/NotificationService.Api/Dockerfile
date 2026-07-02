# syntax=docker/dockerfile:1.7
# Multi-stage: Debian bookworm-slim (ICU içerir → SqlClient çalışır). Runtime patch'e pinli (8.0.11).
# AnalyticsService/InventoryService Dockerfile pattern'iyle birebir. COPY listesi NotificationService'in
# GERÇEK transitive kapanışına göre (Faz 3/4/5d-6 stale-Dockerfile dersi):
#   Api → {Application, Infrastructure}
#   Infrastructure → {Domain, Application, Contracts, EventBus, Inbox}
#   Contracts → EventBus; Inbox → EventBus; EventBus = yaprak.
#   Application → Domain; Domain = yaprak.
# OrderHub.Common: hiçbir NotificationService projesi veya Inbox/Contracts/EventBus Common'a REF VERMEZ → KOPYALANMAZ.

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build
WORKDIR /src

# Restore katmanı: yalnızca csproj + lock + ortak props kopyalanır → kaynak değişince restore cache'i bozulmaz.
COPY ["Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["src/BuildingBlocks/OrderHub.EventBus/OrderHub.EventBus.csproj", "src/BuildingBlocks/OrderHub.EventBus/"]
COPY ["src/BuildingBlocks/OrderHub.EventBus/packages.lock.json", "src/BuildingBlocks/OrderHub.EventBus/"]
COPY ["src/BuildingBlocks/OrderHub.Contracts/OrderHub.Contracts.csproj", "src/BuildingBlocks/OrderHub.Contracts/"]
COPY ["src/BuildingBlocks/OrderHub.Contracts/packages.lock.json", "src/BuildingBlocks/OrderHub.Contracts/"]
COPY ["src/BuildingBlocks/OrderHub.Inbox/OrderHub.Inbox.csproj", "src/BuildingBlocks/OrderHub.Inbox/"]
COPY ["src/BuildingBlocks/OrderHub.Inbox/packages.lock.json", "src/BuildingBlocks/OrderHub.Inbox/"]
COPY ["src/Services/NotificationService/NotificationService.Domain/OrderHub.NotificationService.Domain.csproj", "src/Services/NotificationService/NotificationService.Domain/"]
COPY ["src/Services/NotificationService/NotificationService.Domain/packages.lock.json", "src/Services/NotificationService/NotificationService.Domain/"]
COPY ["src/Services/NotificationService/NotificationService.Application/OrderHub.NotificationService.Application.csproj", "src/Services/NotificationService/NotificationService.Application/"]
COPY ["src/Services/NotificationService/NotificationService.Application/packages.lock.json", "src/Services/NotificationService/NotificationService.Application/"]
COPY ["src/Services/NotificationService/NotificationService.Infrastructure/OrderHub.NotificationService.Infrastructure.csproj", "src/Services/NotificationService/NotificationService.Infrastructure/"]
COPY ["src/Services/NotificationService/NotificationService.Infrastructure/packages.lock.json", "src/Services/NotificationService/NotificationService.Infrastructure/"]
COPY ["src/Services/NotificationService/NotificationService.Api/OrderHub.NotificationService.Api.csproj", "src/Services/NotificationService/NotificationService.Api/"]
COPY ["src/Services/NotificationService/NotificationService.Api/packages.lock.json", "src/Services/NotificationService/NotificationService.Api/"]

# --locked-mode: lock dosyasına birebir uyum (reproducible; sürpriz transitive sürüm yok).
RUN dotnet restore "src/Services/NotificationService/NotificationService.Api/OrderHub.NotificationService.Api.csproj" --locked-mode

# Kaynakları kopyala ve publish (restore tekrar etmesin → --no-restore).
COPY . .
RUN dotnet publish "src/Services/NotificationService/NotificationService.Api/OrderHub.NotificationService.Api.csproj" \
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

# Liveness: app ayakta mı (DB'ye bağlamaz → DB blip'inde container unhealthy olmasın).
HEALTHCHECK --interval=15s --timeout=3s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "OrderHub.NotificationService.Api.dll"]
