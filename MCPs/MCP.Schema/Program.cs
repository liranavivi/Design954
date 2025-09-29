using MCP.Schema.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.MCP.Configuration;
using Shared.MCP.Interfaces;

namespace MCP.Schema;

/// <summary>
/// Main program class for MCP.Schema server
/// </summary>
public partial class Program
{
    public static async Task Main(string[] args)
    {
        try
        {
            Console.WriteLine("🚀 Starting MCP.Schema Server...");

            var host = CreateHostBuilder(args).Build();
            
            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("MCP.Schema Server starting up");

            await host.RunAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Fatal error starting MCP.Schema Server: {ex.Message}");
            Console.WriteLine(ex.ToString());
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Creates and configures the host builder with all necessary services
    /// </summary>
    /// <param name="args">Command line arguments</param>
    /// <returns>Configured host builder</returns>
    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        // Find the project directory by looking for the .csproj file
        var currentDir = Directory.GetCurrentDirectory();
        var projectDir = FindProjectDirectory(currentDir);

        return Host.CreateDefaultBuilder(args)
            .UseContentRoot(projectDir)
            .UseEnvironment(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development")
            .ConfigureLogging(logging =>
            {
                // Clear default providers - OpenTelemetry will handle logging
                logging.ClearProviders();
            })
            .ConfigureAppConfiguration((context, config) =>
            {
                // Configure appsettings files with the same pattern as other projects
                config.SetBasePath(projectDir)
                      .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                      .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true)
                      .AddEnvironmentVariables()
                      .AddCommandLine(args);
            })
            .ConfigureServices((context, services) =>
            {
                var configuration = context.Configuration;
                
                // Configure MCP server options
                services.Configure<McpServerOptions>(configuration.GetSection("McpServer"));

                // Add MCP server with custom providers
                services.AddMcpServer(
                    configuration,
                    configuration["OpenTelemetry:ServiceName"] ?? "MCP.Schema",
                    configuration["OpenTelemetry:ServiceVersion"] ?? "1.0.0",
                    providers =>
                    {
                        // Add Schema-specific MCP providers
                        providers.AddSingleton<IMcpResourceProvider, SchemaResourceProvider>();
                        providers.AddSingleton<IMcpPromptProvider, SchemaPromptProvider>();
                        providers.AddSingleton<IMcpToolProvider, SchemaToolProvider>();
                    });

                // Add HTTP client for Schema Manager communication
                services.AddManagerHttpClient(configuration, "Schema");

                // Add Schema Manager HTTP client service
                services.AddScoped<ISchemaManagerClient, SchemaManagerClient>();

                // Add hosted service for MCP server
                services.AddHostedService<McpServerHostedService>();
            });
    }

    /// <summary>
    /// Finds the project directory by looking for the .csproj file
    /// </summary>
    /// <param name="startDirectory">Directory to start searching from</param>
    /// <returns>Project directory path</returns>
    private static string FindProjectDirectory(string startDirectory)
    {
        var currentDir = new DirectoryInfo(startDirectory);

        // Look for .csproj file in current directory and parent directories
        while (currentDir != null)
        {
            var csprojFiles = currentDir.GetFiles("*.csproj");
            if (csprojFiles.Length > 0)
            {
                return currentDir.FullName;
            }

            // Check if we're in the MCP.Schema directory specifically
            if (currentDir.Name == "MCP.Schema")
            {
                return currentDir.FullName;
            }

            currentDir = currentDir.Parent;
        }

        // Fallback: try to find the MCP.Schema directory
        var baseDir = startDirectory;
        var targetPath = Path.Combine(baseDir, "MCPs", "MCP.Schema");
        if (Directory.Exists(targetPath))
        {
            return targetPath;
        }

        // Final fallback: use current directory
        return startDirectory;
    }
}

/// <summary>
/// Make Program class accessible for testing
/// </summary>
public partial class Program { }
