# syntax=docker/dockerfile:1
#
# Build context = repo kökü (docker-compose.yml içinde `context: .`)
# Sebep: Auction.API kardeş projelere (Core/Entity/Repository/Business) ProjectReference
# verdiği için build sırasında onların da context içinde görünmesi gerekiyor.

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# 1) Sadece .csproj'ları kopyala -> restore katmanı kod değiştikçe tekrar çalışmaz.
COPY src/Services/Auction/Auction.API/Auction.API.csproj               src/Services/Auction/Auction.API/
COPY src/Services/Auction/Auction.Business/Auction.Business.csproj     src/Services/Auction/Auction.Business/
COPY src/Services/Auction/Auction.Core/Auction.Core.csproj             src/Services/Auction/Auction.Core/
COPY src/Services/Auction/Auction.Entity/Auction.Entity.csproj         src/Services/Auction/Auction.Entity/
COPY src/Services/Auction/Auction.Repository/Auction.Repository.csproj src/Services/Auction/Auction.Repository/
# İleride src/BuildingBlocks altında paylaşılan proje açarsan buraya ekle:
# COPY src/BuildingBlocks/BuildingBlocks.Shared/BuildingBlocks.Shared.csproj src/BuildingBlocks/BuildingBlocks.Shared/

RUN dotnet restore src/Services/Auction/Auction.API/Auction.API.csproj

# 2) Kaynak kodu kopyala ve publish et.
COPY src/Services/Auction/ src/Services/Auction/
# COPY src/BuildingBlocks/ src/BuildingBlocks/

RUN dotnet publish src/Services/Auction/Auction.API/Auction.API.csproj \
    -c "$BUILD_CONFIGURATION" \
    -o /app/publish \
    --no-restore \
    -p:UseAppHost=false

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# SignalR notu: WebSocket upgrade'i için container içinde düz HTTP dinlemek yeterli.
# TLS'i ileride gateway/ingress üstlenir (wss:// orada sonlanır).
# (ASPNETCORE_URLS yerine HTTP_PORTS: imajın kendi ayarını ezip uyarı üretmiyor)
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .

# .NET imajlarının hazır non-root kullanıcısı (APP_UID=1654)
USER $APP_UID

ENTRYPOINT ["dotnet", "Auction.API.dll"]
