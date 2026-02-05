using DFrame;
using DFrame.Controller;
using Microsoft.AspNetCore.Builder;

namespace CashlessLoadTest.Controller;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Configure execution result history provider
        // FlatFileLogExecutionResultHistoryProvider saves execution results to JSON files
        builder.Services.AddSingleton<IExecutionResultHistoryProvider>(sp =>
        {
            // Get the log directory from environment or use default
            var logDirectory = Environment.GetEnvironmentVariable("DFRAME_LOG_DIRECTORY") 
                ?? Path.Combine(Directory.GetCurrentDirectory(), "logs");
            
            return new FlatFileLogExecutionResultHistoryProvider(logDirectory);
        });

        // Run DFrame Controller with configuration
        await builder.RunDFrameControllerAsync((ctx, options) =>
        {
            options.Title = "Cashless Load Test Controller";
        });
    }
}
