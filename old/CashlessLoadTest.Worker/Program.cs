using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DFrame;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CashlessLoadTest.Worker;

// ============================================================================
// Main Program
// ============================================================================
public class Program
{
    public static async Task Main(string[] args)
    {
        // Parse BASE_URL from args or environment variable
        // Priority: Command-line args > Environment variable > Default
        string baseUrl = Environment.GetEnvironmentVariable("BASE_URL")?.Trim() ?? "https://mada.com:2401";

        // Parse VirtualProcess from args or environment variable
        // Priority: Command-line args > Environment variable > Default
        int virtualProcess = 1; // Default

#if DEBUG
        virtualProcess = 5;
#endif

        // First, check environment variable (fallback)
        var envVp = Environment.GetEnvironmentVariable("VIRTUAL_PROCESS");
        if (!string.IsNullOrEmpty(envVp) && int.TryParse(envVp, out int envVpValue) && envVpValue > 0)
        {
            virtualProcess = envVpValue;
        }

        // Parse controller address, BASE_URL, and VirtualProcess from command-line arguments
        string? controllerAddress = null;

        for (int i = 0; i < args.Length; i++)
        {
            // Parse --base-url or --url argument
            if ((args[i].Equals("--base-url", StringComparison.OrdinalIgnoreCase) ||
                 args[i].Equals("--url", StringComparison.OrdinalIgnoreCase)) &&
                i + 1 < args.Length)
            {
                var urlValue = args[i + 1]?.Trim();
                if (!string.IsNullOrEmpty(urlValue))
                {
                    baseUrl = urlValue; // Override env var with command-line value
                    i++; // Skip the value
                    continue;
                }
            }

            // Parse --virtual-process or --vp argument
            if ((args[i].Equals("--virtual-process", StringComparison.OrdinalIgnoreCase) ||
                 args[i].Equals("--vp", StringComparison.OrdinalIgnoreCase)) &&
                i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out int vp) && vp > 0)
                {
                    virtualProcess = vp; // Override env var with command-line value
                    i++; // Skip the value
                    continue;
                }
            }

            // Controller address is the first non-flag argument
            if (!args[i].StartsWith("--") && string.IsNullOrEmpty(controllerAddress))
            {
                controllerAddress = args[i];
            }
        }

        controllerAddress ??= Environment.GetEnvironmentVariable("CONTROLLER_ADDRESS")?.Trim()
            ?? "http://localhost:7313";

        // Update Config.BaseUrl with parsed value
        Config.BaseUrl = baseUrl;

        // Create HttpClient with optimized settings
        var httpClientHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
            MaxConnectionsPerServer = 100, // Allow more concurrent connections
            UseCookies = false // Disable cookies for better performance
        };

        var httpClient = new HttpClient(httpClientHandler)
        {
            Timeout = TimeSpan.FromSeconds(Config.HttpTimeoutSeconds),
            BaseAddress = new Uri(baseUrl)
        };

        // Set default headers
        httpClient.DefaultRequestHeaders.Add("User-Agent", "DFrame-CashlessLoadTest/1.0");
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

        Console.WriteLine($"[DFrame Worker] Connecting to Controller: {controllerAddress}");
        Console.WriteLine($"[DFrame Worker] BaseUrl: {baseUrl}");
        Console.WriteLine($"[DFrame Worker] VirtualProcess: {virtualProcess}");

        // Configure and run DFrame Worker
        await Host.CreateDefaultBuilder(args)
            .ConfigureServices(services =>
            {
                // Register HttpClient as singleton for use in Workload
                services.AddSingleton(httpClient);
            })
            .RunDFrameWorkerAsync((ctx, options) =>
            {
                options.ControllerAddress = controllerAddress;
                options.VirtualProcess = virtualProcess;

                // Configure worker metadata
                options.Metadata = new Dictionary<string, string>
                {
                    { "MachineName", Environment.MachineName },
                    { "BaseUrl", baseUrl },
                    { "WorkerVersion", "1.0.0" },
                    { "OS", Environment.OSVersion.ToString() },
                    { "ProcessorCount", Environment.ProcessorCount.ToString() },
                    { "FrameworkVersion", Environment.Version.ToString() },
                    { "UserName", Environment.UserName },
                    { "UserDomainName", Environment.UserDomainName },
                    { "VirtualProcess", virtualProcess.ToString() }
                };
            });
    }
}
