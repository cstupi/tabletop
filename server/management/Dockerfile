# The production image. Build from the repo root: docker build -f server/management/Dockerfile -t tabletop-management .
# `--migrate` as the command applies the migrations over TABLETOP_DB_CONNECTION (the owner role) and exits.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY libs/ libs/
COPY server/management/ server/management/
COPY client/content/builtin/catalog.json client/content/builtin/catalog.json
RUN dotnet publish server/management/Tabletop.Management.csproj -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS= \
    TABLETOP_MANAGEMENT_URL=http://0.0.0.0:5100 \
    DOTNET_EnableDiagnostics=0
USER app
EXPOSE 5100
HEALTHCHECK --interval=10s --timeout=3s --start-period=20s --retries=3 \
    CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/5100 && printf 'GET /healthz HTTP/1.0\\r\\nHost: localhost\\r\\n\\r\\n' >&3 && head -n1 <&3 | grep -q ' 200 '"]
ENTRYPOINT ["dotnet", "Tabletop.Management.dll"]
