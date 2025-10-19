
# Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ../../ .
RUN dotnet restore Imperium.MVP.sln
RUN dotnet publish src/Imperium.Api/Imperium.Api.csproj -c Release -o /app --no-restore

# Run
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
VOLUME ["/app/data"]
ENTRYPOINT ["dotnet", "Imperium.Api.dll"]
