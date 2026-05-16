# Multi-stage container build for the API service.
# This was kept from the original template because containerized deployment still fits the
# new architecture, even though the server-rendered MVC frontend was removed.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["SakugaVault/SakugaVault.csproj", "SakugaVault/"]
RUN dotnet restore "SakugaVault/SakugaVault.csproj"
COPY . .
WORKDIR "/src/SakugaVault"
RUN dotnet build "./SakugaVault.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./SakugaVault.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SakugaVault.dll"]
