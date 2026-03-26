# =========================
# BUILD STAGE
# =========================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files
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

# Create non-root user for security
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
USER appuser

# Copy published files
COPY --from=build /app/publish .

# Expose port
EXPOSE 80

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD curl -f http://localhost:80/health/live || exit 1

# Run app
ENTRYPOINT ["dotnet", "AppHub.WebApi.dll"]
