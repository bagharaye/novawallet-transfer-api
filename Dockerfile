# Multi-stage build so the final image only carries the published app, not the
# full SDK/toolchain. Builds Part A only — the test project isn't needed at runtime.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/NovaWallet.Api/NovaWallet.Api.csproj src/NovaWallet.Api/
RUN dotnet restore src/NovaWallet.Api/NovaWallet.Api.csproj

COPY src/NovaWallet.Api/ src/NovaWallet.Api/
RUN dotnet publish src/NovaWallet.Api/NovaWallet.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Render (and most free PaaS hosts) assign the listen port via $PORT at runtime;
# ASPNETCORE_URLS is set from it in the CMD below rather than hardcoded here,
# since the platform-assigned port isn't known at build time.
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["/bin/sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080} dotnet NovaWallet.Api.dll"]
