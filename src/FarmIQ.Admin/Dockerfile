FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

COPY FarmIQ.sln ./
COPY src/FarmIQ.Shared/FarmIQ.Shared.csproj src/FarmIQ.Shared/
COPY src/FarmIQ.Admin/FarmIQ.Admin.csproj src/FarmIQ.Admin/
RUN dotnet restore src/FarmIQ.Admin/FarmIQ.Admin.csproj

COPY . .
RUN dotnet publish src/FarmIQ.Admin/FarmIQ.Admin.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
RUN apk add --no-cache wget
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "FarmIQ.Admin.dll"]
