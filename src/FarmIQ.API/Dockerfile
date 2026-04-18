FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

COPY FarmIQ.sln ./
COPY src/FarmIQ.Shared/FarmIQ.Shared.csproj src/FarmIQ.Shared/
COPY src/FarmIQ.Core/FarmIQ.Core.csproj src/FarmIQ.Core/
COPY src/FarmIQ.Application/FarmIQ.Application.csproj src/FarmIQ.Application/
COPY src/FarmIQ.Infrastructure/FarmIQ.Infrastructure.csproj src/FarmIQ.Infrastructure/
COPY src/FarmIQ.API/FarmIQ.API.csproj src/FarmIQ.API/
RUN dotnet restore src/FarmIQ.API/FarmIQ.API.csproj

COPY . .
RUN dotnet publish src/FarmIQ.API/FarmIQ.API.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
RUN apk add --no-cache wget
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "FarmIQ.API.dll"]
