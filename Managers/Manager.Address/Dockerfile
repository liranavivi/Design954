# Use the official .NET 9.0 runtime as the base image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 5120

# Use the official .NET 9.0 SDK for building
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution file
COPY FlowOrchestrator.sln .

# Copy project files for dependency resolution
COPY Managers/Manager.Address/Manager.Address.csproj Managers/Manager.Address/
COPY Shared/Shared.Configuration/Shared.Configuration.csproj Shared/Shared.Configuration/
COPY Shared/Shared.Correlation/Shared.Correlation.csproj Shared/Shared.Correlation/
COPY Shared/Shared.Entities/Shared.Entities.csproj Shared/Shared.Entities/
COPY Shared/Shared.HealthChecks/Shared.HealthChecks.csproj Shared/Shared.HealthChecks/
COPY Shared/Shared.Models/Shared.Models.csproj Shared/Shared.Models/
COPY Shared/Shared.MongoDB/Shared.MongoDB.csproj Shared/Shared.MongoDB/
COPY Shared/Shared.Repositories/Shared.Repositories.csproj Shared/Shared.Repositories/
COPY Shared/Shared.Services/Shared.Services.csproj Shared/Shared.Services/

# Restore dependencies
RUN dotnet restore Managers/Manager.Address/Manager.Address.csproj

# Copy all source code
COPY . .

# Build the application
WORKDIR /src/Managers/Manager.Address
RUN dotnet build Manager.Address.csproj -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish Manager.Address.csproj -c Release -o /app/publish /p:UseAppHost=false

# Final stage
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Create a non-root user
RUN groupadd -r appuser && useradd -r -g appuser appuser
RUN chown -R appuser:appuser /app
USER appuser

ENTRYPOINT ["dotnet", "Manager.Address.dll"]
