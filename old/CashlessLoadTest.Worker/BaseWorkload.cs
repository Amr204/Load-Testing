using DFrame;

namespace CashlessLoadTest.Worker;

// ============================================================================
// Base Workload Helper (shared token management)
// ============================================================================
public abstract class BaseWorkload : Workload
{
    protected readonly HttpClient _httpClient;
    protected readonly string _baseUrl;
    protected string? _token;
    protected string? _senderPhone;
    protected DateTime _tokenExpiresAt;
    protected bool _loginLogged;
    protected static readonly Random _random = new();

    // Metrics tracking
    protected int _tokenCacheHits = 0;
    protected int _tokenCacheMisses = 0;
    protected int _successfulRequests = 0;
    protected int _failedRequests = 0;

    protected BaseWorkload(HttpClient httpClient, string baseUrl = "")
    {
        _httpClient = httpClient;
        _baseUrl = string.IsNullOrEmpty(baseUrl) ? Config.BaseUrl : baseUrl;
    }

    protected async Task EnsureValidTokenAsync(CancellationToken cancellationToken)
    {
        // Check if token is expired or missing
        if (_token == null || DateTime.UtcNow >= _tokenExpiresAt)
        {
            _loginLogged = false;

            // Try to load from cache first
            var cachedToken = TokenCache.LoadToken(_senderPhone!);

            if (cachedToken != null && !string.IsNullOrEmpty(cachedToken.Token))
            {
                // Use cached token
                _token = cachedToken.Token;
                _tokenExpiresAt = cachedToken.ExpiresAt;
                _tokenCacheHits++;

                if (!_loginLogged)
                {
                    _loginLogged = true;
                    var timeUntilExpiry = (_tokenExpiresAt - DateTime.UtcNow).TotalSeconds;
                    Console.WriteLine($"[VU {_senderPhone}] Token loaded from cache | expires in {timeUntilExpiry:F0}s | cacheHits={_tokenCacheHits}");
                }
            }
            else
            {
                // Cache miss or expired - perform login
                _tokenCacheMisses++;
                Console.WriteLine($"[VU {_senderPhone}] Token cache miss | performing login | cacheMisses={_tokenCacheMisses}");

                _token = await LoginAsync(_senderPhone!, cancellationToken);

                // Save to cache if login successful
                if (_token != null && _tokenExpiresAt > DateTime.UtcNow)
                {
                    TokenCache.SaveToken(_senderPhone!, _token, _tokenExpiresAt);
                    Console.WriteLine($"[VU {_senderPhone}] Token saved to cache");
                }
            }
        }
    }

    protected async Task<string?> LoginAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        // Jitter to prevent login storm
        await Task.Delay(_random.Next(0, 500), cancellationToken);

        var deviceId = "urqzUEurRV4iX/ChtExJQcrbf1lJ/Ot+nq0eOFPUoXxNGOzNuQvbEAcTWUxSKFx2";

        var formData = new[]
        {
            new KeyValuePair<string, string>("client_id", "MobileApp"),
            new KeyValuePair<string, string>("client_secret", "1q2w3e*"),
            new KeyValuePair<string, string>("grant_type", "pin_code_credentials"),
            new KeyValuePair<string, string>("phone_number", phoneNumber),
            new KeyValuePair<string, string>("pin_code", Config.CommonPin),
            new KeyValuePair<string, string>("scope", "profile roles email phone MobileAppServices"),
            new KeyValuePair<string, string>("device.id", deviceId),
            new KeyValuePair<string, string>("device.name", "emu64x"),
            new KeyValuePair<string, string>("device.os", "android 1"),
            new KeyValuePair<string, string>("device.model", "sdk_gphone64_x86_64"),
            new KeyValuePair<string, string>("device.type", "android"),
        };

        var loginHeaders = new Dictionary<string, string>
        {
            { "x-vcp-loc", "-34.397,150.644" },
            { "X-Requested-With", "XMLHttpRequest" },
            { "Accept-Language", "en,ar" },
            { "x-device-id", deviceId }
        };

        // Retry login specifically for HTTP 409 (Conflict) errors
        HttpResponseResult<LoginResponse>? lastResult = null;

        for (int attempt = 0; attempt <= Config.LoginMaxRetries; attempt++)
        {
            if (attempt > 0)
            {
                // Exponential backoff for retries: 2s, 4s, 8s
                var delayMs = Config.LoginRetryDelayMs;// * (int)Math.Pow(2, attempt - 1);
                Console.WriteLine($"[VU {_senderPhone}] Retrying login (attempt {attempt + 1}/{Config.LoginMaxRetries + 1}) after {delayMs}ms delay");
                await Task.Delay(delayMs, cancellationToken);
            }

            var result = await HttpHelper.SendFormUrlEncodedAsync<LoginResponse>(
                _httpClient,
                $"{_baseUrl}/auth/connect/token",
                formData,
                cancellationToken,
                0, // No retries at HttpHelper level - we handle retries here
                Config.RetryDelayMs,
                loginHeaders
            );

            lastResult = result;

            // Check for HTTP 409 (Conflict) - retry this specific error
            if (!result.IsSuccess && result.StatusCode == 409)
            {
                if (attempt < Config.LoginMaxRetries)
                {
                    Console.WriteLine($"[VU {_senderPhone}] Login returned 409 Conflict, will retry...");
                    continue; // Retry the login
                }
                else
                {
                    // Max retries reached for 409
                    var errorMsg = $"Login failed after {Config.LoginMaxRetries + 1} attempts: HTTP 409 Conflict";
                    if (!string.IsNullOrEmpty(result.ResponseBody))
                        errorMsg += $"\nResponse: {result.ResponseBody}";
                    Console.WriteLine($"[VU {_senderPhone}] {errorMsg}");
                    return null;
                }
            }

            // For other errors, don't retry (return immediately)
            if (!result.IsSuccess || result.Data == null)
            {
                var errorMsg = $"Login failed: {result.ErrorMessage}";
                if (!string.IsNullOrEmpty(result.ResponseBody))
                    errorMsg += $"\nResponse: {result.ResponseBody}";
                Console.WriteLine($"[VU {_senderPhone}] {errorMsg}");
                return null;
            }

            // Check for OAuth errors in response body
            if (!string.IsNullOrEmpty(result.Data.error))
            {
                // Don't retry OAuth errors (invalid credentials, etc.)
                var errorMsg = $"Login returned OAuth error: {result.Data.error} - {result.Data.error_description ?? "No description"}";
                Console.WriteLine($"[VU {_senderPhone}] {errorMsg}");
                return null;
            }

            // Success - validate access token
            if (string.IsNullOrEmpty(result.Data.access_token))
            {
                Console.WriteLine($"[VU {_senderPhone}] Login response missing access_token");
                return null;
            }

            // Login successful
            var expiresInMs = result.Data.expires_in * 1000;
            _tokenExpiresAt = DateTime.UtcNow.AddMilliseconds(expiresInMs - Config.TokenRefreshBufferMs);

            if (!_loginLogged)
            {
                _loginLogged = true;
                if (attempt > 0)
                {
                    Console.WriteLine($"[VU {_senderPhone}] Login OK after {attempt + 1} attempts | user={phoneNumber} | expires_in={result.Data.expires_in}s");
                }
                else
                {
                    Console.WriteLine($"[VU {_senderPhone}] Login OK | user={phoneNumber} | expires_in={result.Data.expires_in}s");
                }
            }

            return result.Data.access_token;
        }

        // Should not reach here, but handle it anyway
        if (lastResult != null)
        {
            var errorMsg = $"Login failed after {Config.LoginMaxRetries + 1} attempts: {lastResult.ErrorMessage}";
            if (!string.IsNullOrEmpty(lastResult.ResponseBody))
                errorMsg += $"\nResponse: {lastResult.ResponseBody}";
            Console.WriteLine($"[VU {_senderPhone}] {errorMsg}");
        }

        return null;
    }

    protected string PickReceiverDifferentFrom(string senderPhone)
    {
        var availableReceivers = new List<string>(Config.Users);
        availableReceivers.Remove(senderPhone);

        if (availableReceivers.Count == 0)
        {
            throw new InvalidOperationException($"No available receivers (all users are the same as sender: {senderPhone})");
        }

        return availableReceivers[_random.Next(availableReceivers.Count)];
    }
}
