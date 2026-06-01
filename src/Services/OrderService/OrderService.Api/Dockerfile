# syntax=docker/dockerfile:1.7
# Multi-stage: Debian bookworm-slim (ICU içerir → SqlClient çalışır; alpine/chiseled invariant olurdu).
# Runtime patch'e pinli (8.0.11 = shipped artifact). SDK feature-band versiyonu kullanır (runtime patch
# tag'i yok); 8.0 minor pin yeterli — paket sürümleri zaten packages.lock.json + --locked-mode ile sabit.

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build
WORKDIR /src

# Restore katmanı: yalnızca csproj + lock + ortak props kopyalanır → kaynak değişince restore cache'i bozulmaz.
COPY ["Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["src/BuildingBlocks/OrderHub.Common/OrderHub.Common.csproj", "src/BuildingBlocks/OrderHub.Common/"]
COPY ["src/BuildingBlocks/OrderHub.Common/packages.lock.json", "src/BuildingBlocks/OrderHub.Common/"]
COPY ["src/Services/OrderService/OrderService.Domain/OrderHub.OrderService.Domain.csproj", "src/Services/OrderService/OrderService.Domain/"]
COPY ["src/Services/OrderService/OrderService.Domain/packages.lock.json", "src/Services/OrderService/OrderService.Domain/"]
COPY ["src/Services/OrderService/OrderService.Application/OrderHub.OrderService.Application.csproj", "src/Services/OrderService/OrderService.Application/"]
COPY ["src/Services/OrderService/OrderService.Application/packages.lock.json", "src/Services/OrderService/OrderService.Application/"]
COPY ["src/Services/OrderService/OrderService.Infrastructure/OrderHub.OrderService.Infrastructure.csproj", "src/Services/OrderService/OrderService.Infrastructure/"]
COPY ["src/Services/OrderService/OrderService.Infrastructure/packages.lock.json", "src/Services/OrderService/OrderService.Infrastructure/"]
COPY ["src/Services/OrderService/OrderService.Api/OrderHub.OrderService.Api.csproj", "src/Services/OrderService/OrderService.Api/"]
COPY ["src/Services/OrderService/OrderService.Api/packages.lock.json", "src/Services/OrderService/OrderService.Api/"]

# --locked-mode: lock dosyasına birebir uyum (reproducible; sürpriz transitive sürüm yok).
RUN dotnet restore "src/Services/OrderService/OrderService.Api/OrderHub.OrderService.Api.csproj" --locked-mode

# Kaynakları kopyala ve publish (restore tekrar etmesin → --no-restore).
COPY . .
RUN dotnet publish "src/Services/OrderService/OrderService.Api/OrderHub.OrderService.Api.csproj" \
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

ENTRYPOINT ["dotnet", "OrderHub.OrderService.Api.dll"]
