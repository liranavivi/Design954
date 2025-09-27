using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Shared.Configuration;

/// <summary>
/// Configuration extension methods for MassTransit setup with RabbitMQ and Kafka.
/// </summary>
public static class MassTransitConfiguration
{
    public static IServiceCollection AddMassTransitBusProvider(
        this IServiceCollection services,
        IConfiguration configuration,
        string compositeKey = "", params Type[] consumerTypes)
    {
        var provider = configuration["MassTransit:Provider"] ?? "RabbitMQ";

        return provider.ToUpperInvariant() switch
        {
            "KAFKA" => AddMassTransitWithKafka(services, configuration, compositeKey, consumerTypes),
            "RABBITMQ" or _ => AddMassTransitWithRabbitMq(services, configuration, compositeKey, consumerTypes)
        };
    }

    public static IServiceCollection AddMassTransitBusProvider(
        this IServiceCollection services,
        IConfiguration configuration,
        params Type[] consumerTypes)
    {
        var provider = configuration["MassTransit:Provider"] ?? "RabbitMQ";

        return provider.ToUpperInvariant() switch
        {
            "KAFKA" => AddMassTransitWithKafka(services, configuration, "", consumerTypes),
            "RABBITMQ" or _ => AddMassTransitWithRabbitMq(services, configuration, "", consumerTypes)
        };
    }


    private static IServiceCollection AddMassTransitWithRabbitMq(
        this IServiceCollection services,
        IConfiguration configuration,
        string compositeKey = "", params Type[] consumerTypes)
    {
        services.AddMassTransit(x =>
        {
            // Add consumers dynamically
            foreach (var consumerType in consumerTypes)
            {
                x.AddConsumer(consumerType);
            }

            // Note: Correlation ID filters will be configured per-bus basis
            // as generic filter registration is not available in this MassTransit version

            x.UsingRabbitMq((context, cfg) =>
            {
                var rabbitMqSettings = configuration.GetSection("RabbitMQ");

                cfg.Host(rabbitMqSettings["Host"] ?? "localhost", rabbitMqSettings["VirtualHost"] ?? "/", h =>
                {
                    h.Username(rabbitMqSettings["Username"] ?? "guest");
                    h.Password(rabbitMqSettings["Password"] ?? "guest");
                });

                // Configure retry policy
                cfg.UseMessageRetry(r => r.Intervals(
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(15),
                    TimeSpan.FromSeconds(30)
                ));

                // Each instance gets unique temporary queues
                var instanceId = $"{compositeKey}";

                foreach (var consumerType in consumerTypes)
                {
                    cfg.ReceiveEndpoint($"{consumerType.Name.ToLower()}-{instanceId}", e =>
                    {
                        e.AutoDelete = true;
                        e.ConfigureConsumer(context, consumerType);
                    });
                }


                // Configure endpoints to use message type routing
                //cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }

    private static IServiceCollection AddMassTransitWithKafka(
        this IServiceCollection services,
        IConfiguration configuration,
        string compositeKey = "", params Type[] consumerTypes)
    {
        services.AddMassTransit(x =>
        {
            // Add consumers dynamically
            foreach (var consumerType in consumerTypes)
            {
                x.AddConsumer(consumerType);
            }

            // Note: Correlation ID filters will be configured per-bus basis
            // as generic filter registration is not available in this MassTransit version

            x.UsingInMemory((context, cfg) =>
            {
                var kafkaSettings = configuration.GetSection("Kafka");

                // Configure retry policy
                cfg.UseMessageRetry(r => r.Intervals(
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(15),
                    TimeSpan.FromSeconds(30)
                ));

                // Each instance gets unique temporary queues
                var instanceId = $"{compositeKey}";

                foreach (var consumerType in consumerTypes)
                {
                    cfg.ReceiveEndpoint($"{consumerType.Name.ToLower()}-{instanceId}", e =>
                    {
                        e.ConfigureConsumer(context, consumerType);
                    });
                }

                // Configure endpoints to use message type routing
                //cfg.ConfigureEndpoints(context);
            });

            // Add Kafka rider for event streaming
            x.AddRider(rider =>
            {
                var kafkaSettings = configuration.GetSection("Kafka");

                foreach (var consumerType in consumerTypes)
                {
                    rider.AddConsumer(consumerType);
                }

                rider.UsingKafka((context, k) =>
                {
                    k.Host(kafkaSettings["BootstrapServers"] ?? "localhost:9092");

                    // Configure topics for each consumer
                    foreach (var consumerType in consumerTypes)
                    {
                        var topicName = $"{consumerType.Name.ToLower().Replace("consumer", "")}-{compositeKey}";
                        k.TopicEndpoint<string, object>(topicName, consumerType.Name.ToLower(), e =>
                        {
                            e.ConfigureConsumer(context, consumerType);
                        });
                    }
                });
            });
        });

        return services;
    }

}
