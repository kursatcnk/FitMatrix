# Build context is the repository root (see docker-compose.yml). Only the backend folder is copied, so the
# developer NuGet.Config at the root (which points at a local D: package cache) never reaches the image.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/FitMatrix.Api.csproj backend/
RUN dotnet restore backend/FitMatrix.Api.csproj
COPY backend/ backend/
RUN dotnet publish backend/FitMatrix.Api.csproj -c Release -o /app --no-restore -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "FitMatrix.Api.dll"]
