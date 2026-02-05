using System.Security.Cryptography;
using System.Text;
using CashlessLoadTest.Worker.Common;

namespace CashlessLoadTest.Worker.Scenarios.SignupActors;

/// <summary>
/// Auth response from token endpoint.
/// </summary>
public class AuthResponse
{
    public string access_token { get; set; } = "";
    public string token_type { get; set; } = "";
    public int expires_in { get; set; }
    public string? error { get; set; }
    public string? error_description { get; set; }
}

/// <summary>
/// Token provider with HMAC authentication matching Postman pre-request script.
/// Uses constructor injection for settings.
/// </summary>
public class SignupActorsTokenProvider
{
    private readonly HttpClient _httpClient;
    private readonly SignupActorsSettings _settings;
    private string? _cachedToken;
    private DateTime _tokenExpiresAt;
    private readonly object _tokenLock = new();

    public int CacheHits { get; private set; }
    public int CacheMisses { get; private set; }

    public SignupActorsTokenProvider(HttpClient httpClient, SignupActorsSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
    }

    /// <summary>
    /// Gets a valid token, using cache if available.
    /// </summary>
    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        // Check in-memory cache
        lock (_tokenLock)
        {
            if (!string.IsNullOrEmpty(_cachedToken) && DateTime.UtcNow < _tokenExpiresAt)
            {
                CacheHits++;
                return _cachedToken;
            }
        }

        CacheMisses++;

        // Need to login
        var token = await LoginAsync(cancellationToken);
        if (!string.IsNullOrEmpty(token))
        {
            lock (_tokenLock)
            {
                _cachedToken = token;
            }
        }

        return token;
    }

    private async Task<string?> LoginAsync(CancellationToken cancellationToken)
    {
        // Validate required settings
        if (string.IsNullOrEmpty(_settings.ClientId))
        {
            Console.WriteLine("[TokenProvider] ERROR: ClientId not set");
            return null;
        }
        if (string.IsNullOrEmpty(_settings.Username))
        {
            Console.WriteLine("[TokenProvider] ERROR: Username not set");
            return null;
        }
        if (string.IsNullOrEmpty(_settings.Password))
        {
            Console.WriteLine("[TokenProvider] ERROR: Password not set");
            return null;
        }
        if (string.IsNullOrEmpty(_settings.AppId))
        {
            Console.WriteLine("[TokenProvider] ERROR: AppId not set");
            return null;
        }
        if (string.IsNullOrEmpty(_settings.AppSecret))
        {
            Console.WriteLine("[TokenProvider] ERROR: AppSecret not set");
            return null;
        }

        var formData = new Dictionary<string, string>
        {
            ["client_id"] = _settings.ClientId,
            ["client_secret"] = _settings.ClientSecret,
            ["grant_type"] = "password",
            ["username"] = _settings.Username,
            ["password"] = _settings.Password,
            ["scope"] = _settings.Scope
        };

        // Compute HMAC signature matching Postman script
        var hmacHeader = ComputeHmacHeader();

        var headers = new Dictionary<string, string>
        {
            ["X-HMAC"] = hmacHeader,
            ["X-Requested-With"] = "XMLHttpRequest",
            ["Accept-Language"] = "ar,en"
        };

        var url = $"{_settings.BaseUrl}/auth/connect/token";

        // Add jitter to prevent login storm
        await Task.Delay(Random.Shared.Next(10, 200), cancellationToken);

        var result = await HttpHelper.SendFormUrlEncodedAsync<AuthResponse>(
            _httpClient,
            url,
            formData,
            cancellationToken,
            maxRetries: 3,
            retryDelayMs: 1000,
            customHeaders: headers
        );

        if (result.IsSuccess && result.Data != null && !string.IsNullOrEmpty(result.Data.access_token))
        {
            _tokenExpiresAt = DateTime.UtcNow.AddSeconds(result.Data.expires_in - 30);
            Console.WriteLine($"[TokenProvider] Login successful, expires at {_tokenExpiresAt:HH:mm:ss}");
            return result.Data.access_token;
        }

        Console.WriteLine($"[TokenProvider] Login failed: {result.ErrorMessage}");
        return null;
    }

    /// <summary>
    /// Computes HMAC header matching Postman pre-request script:
    /// bodyRaw = ClientId + username + password (client_secret disabled)
    /// data = timestamp\nbodyRaw\nmethod\npath\nquery
    /// signature = HMAC-SHA256(data.toLowerCase(), appSecret)
    /// header = appId:signature:timestamp:nonce
    /// </summary>
    private string ComputeHmacHeader()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var nonce = Guid.NewGuid().ToString();

        // bodyRaw = ClientId + ClientSecret + username + password (client_secret IS enabled)
        var bodyRaw = $"{_settings.ClientId}{_settings.ClientSecret}{_settings.Username}{_settings.Password}";

        // Build data string
        var method = "POST";
        var path = "/auth/connect/token";
        var query = "";

        var dataToSign = $"{timestamp}\n{bodyRaw}\n{method}\n{path}\n{query}".ToLowerInvariant();

        // DEBUG: Log what we're signing
        Console.WriteLine($"[HMAC DEBUG] ClientId: {_settings.ClientId}");
        Console.WriteLine($"[HMAC DEBUG] Username: {_settings.Username}");
        Console.WriteLine($"[HMAC DEBUG] AppId: {_settings.AppId}");
        Console.WriteLine($"[HMAC DEBUG] AppSecret length: {_settings.AppSecret?.Length ?? 0}");
        Console.WriteLine($"[HMAC DEBUG] bodyRaw: {bodyRaw}");
        Console.WriteLine($"[HMAC DEBUG] dataToSign: {dataToSign.Replace("\n", "\\n")}");

        // HMAC-SHA256 - use AppSecret as UTF8 string (matching CryptoJS behavior)
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_settings.AppSecret));
        var signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(dataToSign));
        var signature = Convert.ToBase64String(signatureBytes);

        var result = $"{_settings.AppId}:{signature}:{timestamp}:{nonce}";
        Console.WriteLine($"[HMAC DEBUG] X-HMAC: {result}");

        return result;
    }
}
