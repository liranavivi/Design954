# Use the official .NET SDK image for building
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

# Set the working directory
WORKDIR /src

# Copy solution file
COPY FlowOrchestrator.sln .

# Copy all project files for dependency resolution
COPY Processors/Processor.PluginLoader/Processor.PluginLoader.csproj Processors/Processor.PluginLoader/
COPY Processors/Processor.Base/Processor.Base.csproj Processors/Processor.Base/
COPY Plugins/Plugin.Shared/Plugin.Shared.csproj Plugins/Plugin.Shared/
COPY Shared/Shared.Configuration/Shared.Configuration.csproj Shared/Shared.Configuration/
COPY Shared/Shared.Correlation/Shared.Correlation.csproj Shared/Shared.Correlation/
COPY Shared/Shared.Entities/Shared.Entities.csproj Shared/Shared.Entities/
COPY Shared/Shared.Exceptions/Shared.Exceptions.csproj Shared/Shared.Exceptions/
COPY Shared/Shared.Extensions/Shared.Extensions.csproj Shared/Shared.Extensions/
COPY Shared/Shared.HealthChecks/Shared.HealthChecks.csproj Shared/Shared.HealthChecks/
COPY Shared/Shared.MassTransit/Shared.MassTransit.csproj Shared/Shared.MassTransit/
COPY Shared/Shared.Models/Shared.Models.csproj Shared/Shared.Models/
COPY Shared/Shared.MongoDB/Shared.MongoDB.csproj Shared/Shared.MongoDB/
COPY Shared/Shared.Repositories/Shared.Repositories.csproj Shared/Shared.Repositories/
COPY Shared/Shared.Services/Shared.Services.csproj Shared/Shared.Services/

# Restore dependencies
RUN dotnet restore Processors/Processor.PluginLoader/Processor.PluginLoader.csproj

# Copy the entire source code
COPY . .

# Build the application with real hash generation
WORKDIR /src/Processors/Processor.PluginLoader
RUN dotnet build Processor.PluginLoader.csproj -c Release -o /app/build

# Publish the application with real hash generation
FROM build AS publish
RUN dotnet publish Processor.PluginLoader.csproj -c Release -o /app/publish

# Use the official .NET runtime image for the final stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base

# Install FFmpeg for audio processing capabilities
RUN apt-get update && \
    apt-get install -y ffmpeg && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Final stage
FROM base AS final
WORKDIR /app

# Copy the published application
COPY --from=publish /app/publish .

# Create a non-root user for security
RUN groupadd -r appuser && useradd -r -g appuser appuser

# Change ownership of the app directory to the appuser
RUN chown -R appuser:appuser /app

# Switch to the non-root user
USER appuser

# Set the entry point
ENTRYPOINT ["dotnet", "Processor.PluginLoader.dll"]
