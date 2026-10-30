FROM node:24-bookworm-slim AS web
WORKDIR /src/Server
RUN npm install --global pnpm@10.32.1
COPY Server/package.json Server/pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile --ignore-scripts
COPY Server/wwwroot/src ./wwwroot/src
COPY Server/scripts ./scripts
RUN pnpm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
COPY --from=web /src/Server/wwwroot ./Server/wwwroot
RUN dotnet restore Server/Server.csproj --locked-mode
RUN dotnet publish Server/Server.csproj --configuration Release --no-restore --output /publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
RUN apt-get update && apt-get install --yes --no-install-recommends curl libfontconfig1 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build --chown=app:app /publish ./
RUN mkdir -p /app/AppData && chown app:app /app/AppData
ENV ASPNETCORE_HTTP_PORTS=8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV Remotely_ApplicationOptions__DbProvider=SQLite
ENV Remotely_ConnectionStrings__SQLite="Data Source=/app/AppData/Remotely.db"
USER app
EXPOSE 8080
VOLUME ["/app/AppData"]
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s CMD curl --fail http://localhost:8080/api/healthcheck || exit 1
ENTRYPOINT ["dotnet", "Remotely_Server.dll"]
