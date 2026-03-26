# =========================
# BUILD STAGE
# =========================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["AppHub.Auth.Service/AppHub.Auth.Service.csproj", "AppHub.Auth.Service/"]
COPY ["AppHub.WebApi/AppHub.Core/AppHub.Core.csproj", "AppHub.WebApi/AppHub.Core/"]
COPY ["AppHub.WebApi/AppHub.Infrastructure/AppHub.Infrastructure.csproj", "AppHub.WebApi/AppHub.Infrastructure/"]
COPY ["AppHub.WebApi/AppHub.SharedKernel/AppHub.SharedKernel.csproj", "AppHub.WebApi/AppHub.SharedKernel/"]

RUN dotnet restore "AppHub.Auth.Service/AppHub.Auth.Service.csproj"

COPY . .

WORKDIR "/src/AppHub.Auth.Service"
RUN dotnet build "AppHub.Auth.Service.csproj" -c Release -o /app/build
RUN dotnet publish "AppHub.Auth.Service.csproj" -c Release -o /app/publish

# =========================
# RUNTIME STAGE
# =========================
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 80

ENTRYPOINT ["dotnet", "AppHub.Auth.Service.dll"]
