# =========================
# BUILD STAGE
# =========================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy all project files
COPY ["AppHub.WebApi/AppHub.WebApi.csproj", "AppHub.WebApi/"]
COPY ["AppHub.Core/AppHub.Core.csproj", "AppHub.Core/"]
COPY ["AppHub.Infrastructure/AppHub.Infrastructure.csproj", "AppHub.Infrastructure/"]
COPY ["AppHub.SharedKernel/AppHub.SharedKernel.csproj", "AppHub.SharedKernel/"]

# Restore dependencies
RUN dotnet restore "AppHub.WebApi/AppHub.WebApi.csproj"

# Copy everything
COPY . .

# Build project
WORKDIR "/src/AppHub.WebApi"
RUN dotnet build "AppHub.WebApi.csproj" -c Release -o /app/build

# Publish project
RUN dotnet publish "AppHub.WebApi.csproj" -c Release -o /app/publish

# =========================
# RUNTIME STAGE
# =========================
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# Copy published files
COPY --from=build /app/publish .

# Expose port
EXPOSE 80

# Run app
ENTRYPOINT ["dotnet", "AppHub.WebApi.dll"]