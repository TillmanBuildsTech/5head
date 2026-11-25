FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY FiveHead.State.Api.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
HEALTHCHECK --interval=10s --timeout=3s --retries=10 CMD curl -fsS http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "FiveHead.State.Api.dll"]
