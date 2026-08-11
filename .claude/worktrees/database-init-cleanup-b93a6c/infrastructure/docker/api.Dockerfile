# syntax=docker/dockerfile:1.7
# Build context is the REPO ROOT:
#   docker build -f infrastructure/docker/api.Dockerfile -t planvexa-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Root-level build configuration first so it is cached independently of source churn.
COPY ["global.json", "nuget.config", "Directory.Build.props", "Directory.Packages.props", "./"]

COPY . .

# ponytail: publish restores implicitly — a separate restore layer would need all
# ~20 referenced csproj files copied by hand and drifts every time a project is added.
# The NuGet cache mount keeps rebuilds fast instead.
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish "apps/api/Planvexa.Api/Planvexa.Api.csproj" \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# Npgsql probes for GSSAPI/Kerberos support at connection-negotiation time even for plain password
# auth; the aspnet base image doesn't include it, and its absence surfaces as background hosted
# services (which open their own connections outside the request pipeline) crashing with
# "libgssapi_krb5.so.2: cannot open shared object file" instead of a clear startup error.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
LABEL org.opencontainers.image.title="Planvexa API" \
      org.opencontainers.image.vendor="Planvexa contributors" \
      org.opencontainers.image.source="https://github.com/Anawaz/Planvexa" \
      org.opencontainers.image.licenses="AGPL-3.0-only"
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080
COPY LICENSE NOTICE ADDITIONAL_TERMS.md TRADEMARKS.md THIRD-PARTY-NOTICES.md /usr/share/planvexa/legal/
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Planvexa.Api.dll"]
