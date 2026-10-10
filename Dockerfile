FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json ./
COPY src/SmartCore.Identity.Api/SmartCore.Identity.Api.csproj src/SmartCore.Identity.Api/packages.lock.json src/SmartCore.Identity.Api/
RUN dotnet restore src/SmartCore.Identity.Api --locked-mode
COPY src/ src/
COPY database/ database/
RUN dotnet publish src/SmartCore.Identity.Api -c Release --no-restore -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet","SmartCore.Identity.Api.dll"]
