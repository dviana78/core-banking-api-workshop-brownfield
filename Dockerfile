FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source
COPY src/CoreBanking.Api/CoreBanking.Api.csproj src/CoreBanking.Api/
RUN dotnet restore src/CoreBanking.Api/CoreBanking.Api.csproj
COPY src/CoreBanking.Api/ src/CoreBanking.Api/
RUN dotnet publish src/CoreBanking.Api/CoreBanking.Api.csproj -c Release -o /out --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_HTTP_PORTS=8080
ENV ASPNETCORE_ENVIRONMENT=Workshop
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "CoreBanking.Api.dll"]