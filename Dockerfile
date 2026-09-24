FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY JobOrchestrator.sln ./
COPY src/JobOrchestrator/JobOrchestrator.csproj src/JobOrchestrator/
COPY tests/JobOrchestrator.Tests/JobOrchestrator.Tests.csproj tests/JobOrchestrator.Tests/
RUN dotnet restore JobOrchestrator.sln

COPY . .
RUN dotnet publish src/JobOrchestrator/JobOrchestrator.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "JobOrchestrator.dll"]
