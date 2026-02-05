using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using DFrame;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CashlessLoadTest.Worker.Scenarios.SignupActors;

namespace CashlessLoadTest.Worker;

// ============================================================================
// Main Program - DFrame Worker Entry Point
// ============================================================================
public class Program
{
    public static async Task Main(string[] args)
    {
        // Show usage if --help
        if (args.Length > 0 && (args[0] == "--help" || args[0] == "-h"))
        {
            ShowUsage();
            return;
        }

        // Parse command-line arguments
        string? controllerAddress = null;
        string? baseUrlOverride = null;
        string? runIdOverride = null;
        int? nodeIdOverride = null;
        int virtualProcess = 1;

#if DEBUG
        virtualProcess = 5;
#endif

        // Parse from environment variables first (fallback)
        var envVp = Environment.GetEnvironmentVariable("VIRTUAL_PROCESS");
        if (!string.IsNullOrEmpty(envVp) && int.TryParse(envVp, out int envVpValue) && envVpValue > 0)
        {
            virtualProcess = envVpValue;
        }

        // Parse command-line arguments (higher priority)
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--url", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                baseUrlOverride = args[i + 1]?.Trim();
                i++;
                continue;
            }

            if (args[i].Equals("--vp", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out int vp) && vp > 0)
                {
                    virtualProcess = vp;
                    i++;
                    continue;
                }
            }

            if (args[i].Equals("--run-id", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                runIdOverride = args[i + 1]?.Trim();
                i++;
                continue;
            }

            if ((args[i].Equals("--node-id", StringComparison.OrdinalIgnoreCase) || 
                 args[i].Equals("--prefix", StringComparison.OrdinalIgnoreCase) || 
                 args[i].Equals("-p", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out int nodeId))
                {
                    nodeIdOverride = nodeId;
                    i++;
                    continue;
                }
            }

            // Controller address is the first non-flag argument
            if (!args[i].StartsWith("-") && string.IsNullOrEmpty(controllerAddress))
            {
                controllerAddress = args[i];
            }
        }

        controllerAddress ??= Environment.GetEnvironmentVariable("CONTROLLER_ADDRESS")?.Trim()
            ?? "http://localhost:7313";

        // Load settings from Postman files (auto-discovery)
        var settings = SignupActorsSettings.Load(baseUrlOverride, runIdOverride, nodeIdOverride);

        // Create HttpClient with optimized settings
        var httpClientHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
            MaxConnectionsPerServer = 100,
            UseCookies = false
        };

        var httpTimeoutSeconds = int.Parse(Environment.GetEnvironmentVariable("HTTP_TIMEOUT_SECONDS") ?? "60");
        var httpClient = new HttpClient(httpClientHandler)
        {
            Timeout = TimeSpan.FromSeconds(httpTimeoutSeconds)
        };

        // Set default headers
        httpClient.DefaultRequestHeaders.Add("User-Agent", "DFrame-CashlessLoadTest/2.0");
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║           CashlessLoadTest Worker v2.0                       ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Controller: {controllerAddress,-46} ║");
        Console.WriteLine($"║  Base URL:   {settings.BaseUrl,-46} ║");
        Console.WriteLine($"║  Run ID:     {settings.RunId,-46} ║");
        Console.WriteLine($"║  NodeId:     {settings.WorkerNodeId:D2,-44} ║");
        Console.WriteLine($"║  Telco:      [{string.Join(",", settings.TelcoDigits)}]{new string(' ', 37)} ║");
        Console.WriteLine($"║  VPs:        {virtualProcess,-46} ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        // Configure and run DFrame Worker
        await Host.CreateDefaultBuilder(args)
            .ConfigureServices(services =>
            {
                // Register all dependencies for SignupActors
                services.AddSingleton(httpClient);
                services.AddSingleton(settings);
                services.AddSingleton(new SignupActorsDataGenerator(settings));
                services.AddSingleton(sp => new SignupActorsTokenProvider(
                    sp.GetRequiredService<HttpClient>(),
                    sp.GetRequiredService<SignupActorsSettings>()
                ));
            })
            .RunDFrameWorkerAsync((ctx, options) =>
            {
                options.ControllerAddress = controllerAddress;
                options.VirtualProcess = virtualProcess;

                options.Metadata = new Dictionary<string, string>
                {
                    { "MachineName", Environment.MachineName },
                    { "BaseUrl", settings.BaseUrl },
                    { "RunId", settings.RunId },
                    { "WorkerVersion", "2.0.0" },
                    { "OS", Environment.OSVersion.ToString() },
                    { "VirtualProcess", virtualProcess.ToString() }
                };
            });
    }

    private static void ShowUsage()
    {
        Console.WriteLine(@"
CashlessLoadTest Worker - DFrame Load Testing Worker

USAGE:
    CashlessLoadTest.Worker.exe <controller_address> [options]

ARGUMENTS:
    <controller_address>    gRPC address of the DFrame Controller
                           Example: http://192.168.10.14:7313

OPTIONS:
    --vp <int>             Number of Virtual Processes (VUs) per worker
                           Default: 1 (5 in DEBUG mode)

    --url <string>         Override Base URL from Postman environment
                           Example: https://mada21.com:2401

    --run-id <string>      Unique Run ID for data uniqueness
                           Default: auto-generated from UTC timestamp

    --node-id, -p <int>    Worker Node ID (0-99) for mobile uniqueness
                           Each worker process MUST use a unique node ID
                           to prevent duplicate mobile numbers (16+ workers)
                           Default: auto-derived from ProcessId+MachineName+RunId

    --help, -h             Show this help message

AUTOMATIC CONFIGURATION:
    The worker automatically reads settings from Postman files:
    - SignupActors/*.postman_environment.json (credentials, base URL)
    - SignupActors/*.postman_collection.json (request templates)

    No manual ENV setup required if Postman files are present!

ENV OVERRIDES (optional):
    BASE_URL, VIRTUAL_PROCESS, RUN_ID, WORKER_NODE_ID
    AUTH_CLIENT_ID, AUTH_USERNAME, AUTH_PASSWORD
    HMAC_APP_ID, HMAC_APP_SECRET, PROFILE_ID, OTP_CODE
    TELCO_DIGITS=0,1,3,7,8 (Yemen prefixes: 70,71,73,77,78)

MOBILE FORMAT:
    7{TelcoDigit}{NodeId:00}{WorkloadDigit}{Seq:0000} = 9 digits
    Example: 780305001 = 7 + 8(telco) + 03(nodeId) + 0(workload) + 5001(seq)

EXAMPLE:
    .\CashlessLoadTest.Worker.exe http://192.168.10.14:7313 --vp 10 --run-id TEST -p 03
");
    }
}
