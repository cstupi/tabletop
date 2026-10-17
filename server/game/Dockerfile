# The production image. Build from the repo root: docker build -f server/game/Dockerfile -t tabletop-game .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY libs/ libs/
COPY server/game/ server/game/
RUN dotnet publish server/game/Tabletop.GameServer.csproj -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS= \
    Kestrel__Endpoints__Http__Url=http://0.0.0.0:5200 \
    DOTNET_EnableDiagnostics=0
USER app
EXPOSE 5200
HEALTHCHECK --interval=10s --timeout=3s --start-period=20s --retries=3 \
    CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/5200 && printf 'GET /healthz HTTP/1.0\\r\\nHost: localhost\\r\\n\\r\\n' >&3 && head -n1 <&3 | grep -q ' 200 '"]
ENTRYPOINT ["dotnet", "Tabletop.GameServer.dll"]
