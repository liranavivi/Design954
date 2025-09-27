using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Shared.Correlation;

/// <summary>
/// Simple test class to verify the optimized template caching implementation
/// </summary>
public static class TemplatePerformanceTest
{
    /// <summary>
    /// Tests the template caching performance and correctness
    /// </summary>
    public static void RunTest(ILogger logger)
    {
        Console.WriteLine("=== Template Caching Performance Test ===");
        
        // Create test context
        var context = new HierarchicalLoggingContext
        {
            OrchestratedFlowId = Guid.NewGuid(),
            WorkflowId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            StepId = Guid.NewGuid(),
            ProcessorId = Guid.NewGuid(),
            PublishId = Guid.NewGuid(),
            ExecutionId = Guid.NewGuid()
        };

        // Test template patterns
        var templates = new[]
        {
            "Processing entity {EntityId} in step {StepId}",
            "Saved data to cache. MapName: {MapName}, Key: {Key}, DataLength: {DataLength}",
            "Activity completed. Duration: {Duration}ms, Status: {Status}",
            "User {UserId} performed action {Action} at {Timestamp}"
        };

        var testData = new object[][]
        {
            new object[] { "entity_123", "step_456" },
            new object[] { "UserDataMap", "user_12345", 1024 },
            new object[] { 250.5, "Success" },
            new object[] { "user_789", "Login", DateTime.UtcNow }
        };

        // Warm up - first calls will parse and cache templates
        Console.WriteLine("\n--- Warm-up Phase (Template Parsing & Caching) ---");
        var warmupStopwatch = Stopwatch.StartNew();
        
        for (int i = 0; i < templates.Length; i++)
        {
            logger.LogInformationWithHierarchy(context, templates[i], testData[i]);
        }
        
        warmupStopwatch.Stop();
        Console.WriteLine($"Warm-up completed in: {warmupStopwatch.ElapsedMilliseconds}ms");

        // Performance test - subsequent calls should use cached templates
        Console.WriteLine("\n--- Performance Test (Cached Template Usage) ---");
        const int iterations = 1000;
        
        var performanceStopwatch = Stopwatch.StartNew();
        
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            for (int i = 0; i < templates.Length; i++)
            {
                // Vary the data to simulate real usage
                var dynamicData = GenerateDynamicTestData(i, iteration);
                logger.LogInformationWithHierarchy(context, templates[i], dynamicData);
            }
        }
        
        performanceStopwatch.Stop();
        
        var totalLogs = iterations * templates.Length;
        var avgTimePerLog = (double)performanceStopwatch.ElapsedMilliseconds / totalLogs;
        
        Console.WriteLine($"Performance test completed:");
        Console.WriteLine($"  Total logs: {totalLogs:N0}");
        Console.WriteLine($"  Total time: {performanceStopwatch.ElapsedMilliseconds:N0}ms");
        Console.WriteLine($"  Average time per log: {avgTimePerLog:F3}ms");
        Console.WriteLine($"  Logs per second: {totalLogs / performanceStopwatch.Elapsed.TotalSeconds:F0}");

        // Test template correctness
        Console.WriteLine("\n--- Template Correctness Test ---");
        TestTemplateCorrectness(logger, context);
        
        Console.WriteLine("\n=== Test Completed ===");
    }

    private static object[] GenerateDynamicTestData(int templateIndex, int iteration)
    {
        return templateIndex switch
        {
            0 => new object[] { $"entity_{iteration}", $"step_{iteration % 10}" },
            1 => new object[] { $"Map_{iteration % 5}", $"key_{iteration}", iteration * 10 },
            2 => new object[] { iteration * 0.5 + 100, iteration % 2 == 0 ? "Success" : "Failed" },
            3 => new object[] { $"user_{iteration % 100}", "Action", DateTime.UtcNow.AddSeconds(-iteration) },
            _ => new object[] { iteration }
        };
    }

    private static void TestTemplateCorrectness(ILogger logger, HierarchicalLoggingContext context)
    {
        // Test that template parameters are properly substituted
        Console.WriteLine("Testing template parameter substitution...");
        
        // This should result in properly formatted message, not literal {MapName}
        logger.LogInformationWithHierarchy(context,
            "Cache operation: MapName={MapName}, Key={Key}, Size={Size}",
            "TestMap", "test_key_123", 2048);
            
        Console.WriteLine("Template correctness test completed - check logs for proper substitution");
    }
}
