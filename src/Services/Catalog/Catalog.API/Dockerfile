# syntax=docker/dockerfile:1
#
# Build context = repo kökü (docker-compose.yml içinde `context: .`)
# Sebep: Catalog.API kardeş projelere (Core/Entity/Repository/Business) ProjectReference
# verdiği için build sırasında onların da context içinde görünmesi gerekiyor.

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# 1) Önce kökteki ortak build dosyaları: TargetFramework ve paket sürümleri burada.
#    Bunlar olmadan restore paket sürümlerini çözemez.
COPY Directory.Build.props Directory.Packages.props ./

# 2) Sadece .csproj'ları kopyala -> restore katmanı kod değiştikçe tekrar çalışmaz.
COPY src/Services/Catalog/Catalog.API/Catalog.API.csproj               src/Services/Catalog/Catalog.API/
COPY src/Services/Catalog/Catalog.Business/Catalog.Business.csproj     src/Services/Catalog/Catalog.Business/
COPY src/Services/Catalog/Catalog.Core/Catalog.Core.csproj             src/Services/Catalog/Catalog.Core/
COPY src/Services/Catalog/Catalog.Entity/Catalog.Entity.csproj         src/Services/Catalog/Catalog.Entity/
COPY src/Services/Catalog/Catalog.Repository/Catalog.Repository.csproj src/Services/Catalog/Catalog.Repository/
# Paylaşılan mesaj sözleşmeleri + MassTransit kurulumu
COPY src/BuildingBlocks/BuildingBlocks.Contracts/BuildingBlocks.Contracts.csproj src/BuildingBlocks/BuildingBlocks.Contracts/
COPY src/BuildingBlocks/BuildingBlocks.Messaging/BuildingBlocks.Messaging.csproj src/BuildingBlocks/BuildingBlocks.Messaging/

RUN dotnet restore src/Services/Catalog/Catalog.API/Catalog.API.csproj

# 3) Kaynak kodu kopyala ve publish et.
COPY src/Services/Catalog/ src/Services/Catalog/
COPY src/BuildingBlocks/ src/BuildingBlocks/

RUN dotnet publish src/Services/Catalog/Catalog.API/Catalog.API.csproj \
    -c "$BUILD_CONFIGURATION" \
    -o /app/publish \
    --no-restore \
    -p:UseAppHost=false

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Container içinde sadece HTTP dinliyoruz; TLS'i ileride gateway/ingress üstlenir.
# (ASPNETCORE_URLS yerine HTTP_PORTS: imajın kendi ayarını ezip uyarı üretmiyor)
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .

# .NET imajlarının hazır non-root kullanıcısı (APP_UID=1654)
USER $APP_UID

ENTRYPOINT ["dotnet", "Catalog.API.dll"]
