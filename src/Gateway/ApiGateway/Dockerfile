# syntax=docker/dockerfile:1
#
# Build context = repo kökü (docker-compose.yml içinde `context: .`)

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# 1) Kökteki ortak build dosyaları: TargetFramework ve paket sürümleri burada.
COPY Directory.Build.props Directory.Packages.props ./

# 2) Sadece .csproj -> restore katmanı kod değiştikçe tekrar çalışmaz.
COPY src/Gateway/ApiGateway/ApiGateway.csproj src/Gateway/ApiGateway/
RUN dotnet restore src/Gateway/ApiGateway/ApiGateway.csproj

# 3) Kaynak kod.
COPY src/Gateway/ApiGateway/ src/Gateway/ApiGateway/

RUN dotnet publish src/Gateway/ApiGateway/ApiGateway.csproj \
    -c "$BUILD_CONFIGURATION" \
    -o /app/publish \
    --no-restore \
    -p:UseAppHost=false

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .

USER $APP_UID

ENTRYPOINT ["dotnet", "ApiGateway.dll"]
