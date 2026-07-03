# syntax=docker/dockerfile:1.7
# Multi-stage: Debian bookworm-slim (ICU içerir → SqlClient çalışır). Runtime patch'e pinli (8.0.11).
# PaymentService/AnalyticsService Dockerfile pattern'iyle birebir. COPY listesi InventoryService'in GERÇEK
# transitive kapanışına göre (Faz 3/4 stale-Dockerfile dersi): Api → Infrastructure → {Application, Domain,
# Outbox, Inbox, EventBus, Contracts} → Common. 5c mesajlaşma (saga command consumer'ları + outbox writer)
# Application'ı ve tüm building block'ları (Outbox/Inbox/EventBus/Contracts) ref eder → hepsi COPY listesinde.

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build
WORKDIR /src

# Restore katmanı: yalnızca csproj + lock + ortak props kopyalanır → kaynak değişince restore cache'i bozulmaz.
COPY ["Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["src/BuildingBlocks/OrderHub.Common/OrderHub.Common.csproj", "src/BuildingBlocks/OrderHub.Common/"]
COPY ["src/BuildingBlocks/OrderHub.Common/packages.lock.json", "src/BuildingBlocks/OrderHub.Common/"]
# Faz 5c building blocks: Infrastructure bunlara referans verir (EventBus transport, Contracts sözleşmeleri,
# Outbox writer, Inbox dedup) → locked-mode restore için şart. EventBus yaprak; Outbox→Common+EventBus, Inbox/Contracts→EventBus.
COPY ["src/BuildingBlocks/OrderHub.EventBus/OrderHub.EventBus.csproj", "src/BuildingBlocks/OrderHub.EventBus/"]
COPY ["src/BuildingBlocks/OrderHub.EventBus/packages.lock.json", "src/BuildingBlocks/OrderHub.EventBus/"]
COPY ["src/BuildingBlocks/OrderHub.Contracts/OrderHub.Contracts.csproj", "src/BuildingBlocks/OrderHub.Contracts/"]
COPY ["src/BuildingBlocks/OrderHub.Contracts/packages.lock.json", "src/BuildingBlocks/OrderHub.Contracts/"]
COPY ["src/BuildingBlocks/OrderHub.Outbox/OrderHub.Outbox.csproj", "src/BuildingBlocks/OrderHub.Outbox/"]
COPY ["src/BuildingBlocks/OrderHub.Outbox/packages.lock.json", "src/BuildingBlocks/OrderHub.Outbox/"]
COPY ["src/BuildingBlocks/OrderHub.Inbox/OrderHub.Inbox.csproj", "src/BuildingBlocks/OrderHub.Inbox/"]
COPY ["src/BuildingBlocks/OrderHub.Inbox/packages.lock.json", "src/BuildingBlocks/OrderHub.Inbox/"]
COPY ["src/Services/InventoryService/InventoryService.Domain/OrderHub.InventoryService.Domain.csproj", "src/Services/InventoryService/InventoryService.Domain/"]
COPY ["src/Services/InventoryService/InventoryService.Domain/packages.lock.json", "src/Services/InventoryService/InventoryService.Domain/"]
# Application (5c'de eklendi: handler'lar + ExpireStockReservationsJob; Infrastructure ve Api ona ref verir).
COPY ["src/Services/InventoryService/InventoryService.Application/OrderHub.InventoryService.Application.csproj", "src/Services/InventoryService/InventoryService.Application/"]
COPY ["src/Services/InventoryService/InventoryService.Application/packages.lock.json", "src/Services/InventoryService/InventoryService.Application/"]
COPY ["src/Services/InventoryService/InventoryService.Infrastructure/OrderHub.InventoryService.Infrastructure.csproj", "src/Services/InventoryService/InventoryService.Infrastructure/"]
COPY ["src/Services/InventoryService/InventoryService.Infrastructure/packages.lock.json", "src/Services/InventoryService/InventoryService.Infrastructure/"]
COPY ["src/Services/InventoryService/InventoryService.Api/OrderHub.InventoryService.Api.csproj", "src/Services/InventoryService/InventoryService.Api/"]
COPY ["src/Services/InventoryService/InventoryService.Api/packages.lock.json", "src/Services/InventoryService/InventoryService.Api/"]
# Faz 6 6c-1: Api projesi OrderHub.Observability'ye ProjectReference verir (OTel tracing) → locked-mode restore için şart.
COPY ["src/BuildingBlocks/OrderHub.Observability/OrderHub.Observability.csproj", "src/BuildingBlocks/OrderHub.Observability/"]
COPY ["src/BuildingBlocks/OrderHub.Observability/packages.lock.json", "src/BuildingBlocks/OrderHub.Observability/"]

# --locked-mode: lock dosyasına birebir uyum (reproducible; sürpriz transitive sürüm yok).
RUN dotnet restore "src/Services/InventoryService/InventoryService.Api/OrderHub.InventoryService.Api.csproj" --locked-mode

# Kaynakları kopyala ve publish (restore tekrar etmesin → --no-restore).
COPY . .
RUN dotnet publish "src/Services/InventoryService/InventoryService.Api/OrderHub.InventoryService.Api.csproj" \
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

ENTRYPOINT ["dotnet", "OrderHub.InventoryService.Api.dll"]
