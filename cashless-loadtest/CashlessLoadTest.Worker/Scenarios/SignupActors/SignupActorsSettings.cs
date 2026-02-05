using System.Text.Json.Nodes;
using CashlessLoadTest.Worker.Common.Postman;

namespace CashlessLoadTest.Worker.Scenarios.SignupActors;

/// <summary>
/// Settings for SignupActors scenario - loaded from Postman files with optional ENV overrides.
/// </summary>
public class SignupActorsSettings
{
    // Base paths (relative to exe directory)
    public string PostmanEnvPath { get; set; } = "";
    public string PostmanCollectionPath { get; set; } = "";

    // Resolved values from Postman
    public string BaseUrl { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string AppId { get; set; } = "";
    public string AppSecret { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string Scope { get; set; } = "profile roles email phone AgentAppServices";
    public string OtpCode { get; set; } = "004121";

    // Run ID for uniqueness
    public string RunId { get; set; } = "";

    // Postman request templates
    public PostmanRequest? TokenRequest { get; set; }
    public PostmanRequest? RegisterRequest { get; set; }
    public PostmanRequest? VerifyRequest { get; set; }

    // Raw body template as JsonNode for cloning
    public JsonNode? RegisterBodyTemplate { get; set; }

    /// <summary>
    /// Loads settings from Postman files with CLI/ENV overrides.
    /// </summary>
    public static SignupActorsSettings Load(string? baseUrlOverride = null, string? runIdOverride = null)
    {
        var settings = new SignupActorsSettings();

        // Find Postman files relative to exe directory
        var exeDir = AppContext.BaseDirectory;
        var signupActorsDir = Path.Combine(exeDir, "SignupActors");

        // Also try relative to working directory (for development)
        if (!Directory.Exists(signupActorsDir))
        {
            signupActorsDir = Path.Combine(Directory.GetCurrentDirectory(), "SignupActors");
        }
        if (!Directory.Exists(signupActorsDir))
        {
            // Try parent directories
            var parent = Directory.GetParent(Directory.GetCurrentDirectory())?.FullName;
            if (parent != null)
            {
                signupActorsDir = Path.Combine(parent, "SignupActors");
            }
        }

        Console.WriteLine($"[Settings] Looking for Postman files in: {signupActorsDir}");

        // Find environment file
        var envFiles = Directory.Exists(signupActorsDir) 
            ? Directory.GetFiles(signupActorsDir, "*.postman_environment.json")
            : Array.Empty<string>();
        
        if (envFiles.Length > 0)
        {
            settings.PostmanEnvPath = envFiles[0];
        }

        // Find collection file
        var collectionFiles = Directory.Exists(signupActorsDir)
            ? Directory.GetFiles(signupActorsDir, "*.postman_collection.json")
            : Array.Empty<string>();

        if (collectionFiles.Length > 0)
        {
            settings.PostmanCollectionPath = collectionFiles[0];
        }

        // Load environment
        PostmanEnvironmentLoader? envLoader = null;
        if (!string.IsNullOrEmpty(settings.PostmanEnvPath))
        {
            envLoader = PostmanEnvironmentLoader.Load(settings.PostmanEnvPath);
            
            // Extract values
            settings.BaseUrl = envLoader.GetValue("BaseApi", "");
            settings.ClientId = envLoader.GetValue("ClientId", "");
            settings.ClientSecret = envLoader.GetValue("ClientSecret", "");
            settings.Username = envLoader.GetValue("username", "");
            settings.Password = envLoader.GetValue("password", "");
            settings.AppId = envLoader.GetValue("AppId", "");
            settings.AppSecret = envLoader.GetValue("AppSecret", "");
            settings.ProfileId = envLoader.GetValue("profileId", "");
        }

        // Load collection
        if (!string.IsNullOrEmpty(settings.PostmanCollectionPath))
        {
            var collectionLoader = PostmanCollectionLoader.Load(settings.PostmanCollectionPath);
            settings.TokenRequest = collectionLoader.TokenRequest;
            settings.RegisterRequest = collectionLoader.RegisterRequest;
            settings.VerifyRequest = collectionLoader.VerifyRequest;

            // Parse register body template as JsonNode
            if (settings.RegisterRequest != null && !string.IsNullOrEmpty(settings.RegisterRequest.RawBody))
            {
                try
                {
                    settings.RegisterBodyTemplate = JsonNode.Parse(settings.RegisterRequest.RawBody);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Settings] Failed to parse register body template: {ex.Message}");
                }
            }
        }

        // Apply ENV overrides (optional - only if set)
        var envBaseUrl = Environment.GetEnvironmentVariable("BASE_URL")?.Trim();
        if (!string.IsNullOrEmpty(envBaseUrl))
        {
            settings.BaseUrl = envBaseUrl;
        }

        var envClientId = Environment.GetEnvironmentVariable("AUTH_CLIENT_ID")?.Trim();
        if (!string.IsNullOrEmpty(envClientId))
        {
            settings.ClientId = envClientId;
        }

        var envUsername = Environment.GetEnvironmentVariable("AUTH_USERNAME")?.Trim();
        if (!string.IsNullOrEmpty(envUsername))
        {
            settings.Username = envUsername;
        }

        var envPassword = Environment.GetEnvironmentVariable("AUTH_PASSWORD")?.Trim();
        if (!string.IsNullOrEmpty(envPassword))
        {
            settings.Password = envPassword;
        }

        var envAppId = Environment.GetEnvironmentVariable("HMAC_APP_ID")?.Trim();
        if (!string.IsNullOrEmpty(envAppId))
        {
            settings.AppId = envAppId;
        }

        var envAppSecret = Environment.GetEnvironmentVariable("HMAC_APP_SECRET")?.Trim();
        if (!string.IsNullOrEmpty(envAppSecret))
        {
            settings.AppSecret = envAppSecret;
        }

        var envProfileId = Environment.GetEnvironmentVariable("PROFILE_ID")?.Trim();
        if (!string.IsNullOrEmpty(envProfileId))
        {
            settings.ProfileId = envProfileId;
        }

        var envOtpCode = Environment.GetEnvironmentVariable("OTP_CODE")?.Trim();
        if (!string.IsNullOrEmpty(envOtpCode))
        {
            settings.OtpCode = envOtpCode;
        }

        // CLI overrides (highest priority)
        if (!string.IsNullOrEmpty(baseUrlOverride))
        {
            settings.BaseUrl = baseUrlOverride;
        }

        // Run ID: CLI > ENV > auto-generate
        if (!string.IsNullOrEmpty(runIdOverride))
        {
            settings.RunId = runIdOverride;
        }
        else
        {
            var envRunId = Environment.GetEnvironmentVariable("RUN_ID")?.Trim();
            if (!string.IsNullOrEmpty(envRunId))
            {
                settings.RunId = envRunId;
            }
            else
            {
                // Auto-generate from UTC timestamp
                settings.RunId = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            }
        }

        // Log summary
        Console.WriteLine($"[Settings] BaseUrl: {settings.BaseUrl}");
        Console.WriteLine($"[Settings] ClientId: {settings.ClientId}");
        Console.WriteLine($"[Settings] Username: {settings.Username}");
        Console.WriteLine($"[Settings] AppId: {(string.IsNullOrEmpty(settings.AppId) ? "(not set)" : settings.AppId[..Math.Min(8, settings.AppId.Length)] + "...")}");
        Console.WriteLine($"[Settings] ProfileId: {(string.IsNullOrEmpty(settings.ProfileId) ? "(not set - will fail)" : settings.ProfileId)}");
        Console.WriteLine($"[Settings] RunId: {settings.RunId}");

        return settings;
    }
}
