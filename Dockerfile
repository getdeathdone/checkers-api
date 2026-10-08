# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy csproj files and restore
COPY src/CheckersApi.Core/CheckersApi.Core.csproj src/CheckersApi.Core/
COPY src/CheckersApi.Web/CheckersApi.Web.csproj src/CheckersApi.Web/
RUN dotnet restore src/CheckersApi.Web/CheckersApi.Web.csproj

# Copy all source files and publish
COPY src/CheckersApi.Core/ src/CheckersApi.Core/
COPY src/CheckersApi.Web/ src/CheckersApi.Web/
RUN dotnet publish src/CheckersApi.Web/CheckersApi.Web.csproj -c Release -o /out

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /out ./

EXPOSE 5050
ENV ASPNETCORE_URLS="http://+:5050"
ENV ASPNETCORE_ENVIRONMENT="Production"

ENTRYPOINT ["dotnet", "CheckersApi.Web.dll"]
