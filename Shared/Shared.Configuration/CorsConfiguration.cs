using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Shared.Configuration;

/// <summary>
/// Extension methods for configuring CORS in manager applications
/// </summary>
public static class CorsConfiguration
{
    /// <summary>
    /// Adds CORS services with development-friendly policy for Swagger UI
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddManagerCors(this IServiceCollection services)
    {
        services.AddCors(options =>
        {
            options.AddPolicy("AllowSwagger", policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });

        return services;
    }

    /// <summary>
    /// Configures CORS middleware in the application pipeline
    /// </summary>
    /// <param name="app">The application builder</param>
    /// <returns>The application builder for chaining</returns>
    public static IApplicationBuilder UseManagerCors(this IApplicationBuilder app)
    {
        app.UseCors("AllowSwagger");
        return app;
    }
}
