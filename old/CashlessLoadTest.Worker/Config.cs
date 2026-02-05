namespace CashlessLoadTest.Worker;

// ============================================================================
// Configuration
// ============================================================================
public static class Config
{
    private static string _baseUrl = Environment.GetEnvironmentVariable("BASE_URL")?.Trim() ?? "https://mada.com:2401";
    public static string BaseUrl {
        get => _baseUrl;
        set => _baseUrl = value;
    }
    public static string CommonPin { get; } = "654321";
    public static int TokenRefreshBufferMs { get; } = 30 * 1000; // Refresh 30 seconds before expiry

    // HTTP Configuration
    public static int HttpTimeoutSeconds { get; } = int.Parse(Environment.GetEnvironmentVariable("HTTP_TIMEOUT_SECONDS") ?? "60");
    public static int MaxRetries { get; } = int.Parse(Environment.GetEnvironmentVariable("MAX_RETRIES") ?? "1");
    public static int RetryDelayMs { get; } = int.Parse(Environment.GetEnvironmentVariable("RETRY_DELAY_MS") ?? "1000");


    // Login retry configuration (for 409 Conflict)
    public static int LoginMaxRetries { get; } = int.Parse(Environment.GetEnvironmentVariable("LOGIN_MAX_RETRIES") ?? "3");
    public static int LoginRetryDelayMs { get; } = int.Parse(Environment.GetEnvironmentVariable("LOGIN_RETRY_DELAY_MS") ?? "100");

    public static readonly string[] Users = new[]
    {
        "776134932", "777462906", "773627506", "777764010", "773909112", "777319144",
        "770014159", "735655831", "730082713", "771511630", "776789028", "773859992",
        "733271461", "775769507", "735117802", "771029378", "776809784", "775312672",
        "772544856", "778557570", "771752004", "778893356", "775121407", "771173241",
        "775869356", "739437668", "774660744", "770543166", "774627899", "738614496",
        "779990912", "778240616", "776462528", "774940094", "772947391", "770724497",
        "778591138", "782563336", "770498840", "773172008", "799123425", "776916340"
    };

    // Token cache configuration
    public static string TokenCacheDirectory { get; } =
        Environment.GetEnvironmentVariable("TOKEN_CACHE_DIRECTORY")?.Trim()
        ?? Path.Combine(Directory.GetCurrentDirectory(), "token-cache");

    // Transfer store configuration
    public static string TransferStoreDirectory { get; } =
        Environment.GetEnvironmentVariable("TRANSFER_STORE_DIRECTORY")?.Trim()
        ?? Path.Combine(Directory.GetCurrentDirectory(), "transfer-store");
}
