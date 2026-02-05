namespace CashlessLoadTest.Worker.Common;

// ============================================================================
// Configuration - HTTP and legacy transfer settings only
// SignupActors uses SignupActorsSettings instead
// ============================================================================
public static class Config
{
    // ==========================================================================
    // HTTP Configuration (used by HttpHelper)
    // ==========================================================================
    public static int HttpTimeoutSeconds { get; } = int.Parse(Environment.GetEnvironmentVariable("HTTP_TIMEOUT_SECONDS") ?? "60");
    public static int MaxRetries { get; } = int.Parse(Environment.GetEnvironmentVariable("MAX_RETRIES") ?? "3");
    public static int RetryDelayMs { get; } = int.Parse(Environment.GetEnvironmentVariable("RETRY_DELAY_MS") ?? "1000");

    // ==========================================================================
    // Token Cache Configuration
    // ==========================================================================
    public static string TokenCacheDirectory { get; } =
        Environment.GetEnvironmentVariable("TOKEN_CACHE_DIRECTORY")?.Trim()
        ?? Path.Combine(Directory.GetCurrentDirectory(), "token-cache");

    public static int TokenRefreshBufferMs { get; } = 30 * 1000;

    // ==========================================================================
    // Legacy Transfer Configuration (for existing transfer workloads)
    // ==========================================================================
    private static string _baseUrl = Environment.GetEnvironmentVariable("BASE_URL")?.Trim() ?? "https://mada.com:2401";
    public static string BaseUrl
    {
        get => _baseUrl;
        set => _baseUrl = value;
    }

    public static string CommonPin { get; } = Environment.GetEnvironmentVariable("COMMON_PIN")?.Trim() ?? "654321";
    public static int LoginMaxRetries { get; } = int.Parse(Environment.GetEnvironmentVariable("LOGIN_MAX_RETRIES") ?? "3");
    public static int LoginRetryDelayMs { get; } = int.Parse(Environment.GetEnvironmentVariable("LOGIN_RETRY_DELAY_MS") ?? "100");

    public static string TransferStoreDirectory { get; } =
        Environment.GetEnvironmentVariable("TRANSFER_STORE_DIRECTORY")?.Trim()
        ?? Path.Combine(Directory.GetCurrentDirectory(), "transfer-store");

}
