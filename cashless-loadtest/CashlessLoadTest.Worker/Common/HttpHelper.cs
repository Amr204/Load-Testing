using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CashlessLoadTest.Worker.Common;

// ============================================================================
// HTTP Response Wrapper
// ============================================================================
public class HttpResponseResult<T>
{
    public bool IsSuccess { get; set; }
    public T? Data { get; set; }
    public int StatusCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ResponseBody { get; set; }
    public Dictionary<string, IEnumerable<string>>? Headers { get; set; }

    public static HttpResponseResult<T> Success(T data, int statusCode, Dictionary<string, IEnumerable<string>>? headers = null)
    {
        return new HttpResponseResult<T>
        {
            IsSuccess = true,
            Data = data,
            StatusCode = statusCode,
            Headers = headers
        };
    }

    public static HttpResponseResult<T> Failure(int statusCode, string errorMessage, string? responseBody = null, Dictionary<string, IEnumerable<string>>? headers = null)
    {
        return new HttpResponseResult<T>
        {
            IsSuccess = false,
            StatusCode = statusCode,
            ErrorMessage = errorMessage,
            ResponseBody = responseBody,
            Headers = headers
        };
    }
}

// ============================================================================
// HTTP Helper - Common utilities for HTTP requests with retry logic
// ============================================================================
public static class HttpHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Sends a JSON request with optional bearer token and retry logic.
    /// </summary>
    public static async Task<HttpResponseResult<T>> SendRequestAsync<T>(
        HttpClient httpClient,
        HttpMethod method,
        string url,
        object? requestBody = null,
        string? bearerToken = null,
        CancellationToken cancellationToken = default,
        int maxRetries = 3,
        int retryDelayMs = 1000,
        Dictionary<string, string>? customHeaders = null)
    {
        Exception? lastException = null;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(method, url);

                // Add bearer token if provided
                if (!string.IsNullOrEmpty(bearerToken))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                }

                // Add custom headers if provided
                if (customHeaders != null)
                {
                    foreach (var header in customHeaders)
                    {
                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }

                // Add request body if provided
                if (requestBody != null)
                {
                    var json = JsonSerializer.Serialize(requestBody, JsonOptions);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                var response = await httpClient.SendAsync(request, cancellationToken);

                // Read response headers
                var headers = new Dictionary<string, IEnumerable<string>>();
                foreach (var header in response.Headers)
                {
                    headers[header.Key] = header.Value;
                }
                foreach (var header in response.Content.Headers)
                {
                    headers[header.Key] = header.Value;
                }

                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                // Handle success
                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        var data = JsonSerializer.Deserialize<T>(responseBody, JsonOptions);
                        return HttpResponseResult<T>.Success(data!, (int)response.StatusCode, headers);
                    }
                    catch (JsonException ex)
                    {
                        return HttpResponseResult<T>.Failure(
                            (int)response.StatusCode,
                            $"Failed to deserialize response: {ex.Message}",
                            responseBody,
                            headers
                        );
                    }
                }

                // Handle error response
                var errorMessage = $"HTTP {(int)response.StatusCode} {response.StatusCode}";

                // Try to extract error from response body
                try
                {
                    var errorObj = JsonSerializer.Deserialize<Dictionary<string, object>>(responseBody, JsonOptions);
                    if (errorObj != null && errorObj.TryGetValue("error", out var error))
                    {
                        errorMessage += $": {error}";
                    }
                    else if (errorObj != null && errorObj.TryGetValue("errorMessage", out var errorMsg))
                    {
                        errorMessage += $": {errorMsg}";
                    }
                }
                catch
                {
                    // Ignore JSON parsing errors for error response
                }

                // Retry on 5xx errors or 429 (Too Many Requests)
                if ((int)response.StatusCode >= 500 || response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    if (attempt < maxRetries)
                    {
                        var jitter = Random.Shared.Next(0, 100);
                        await Task.Delay(retryDelayMs * (attempt + 1) + jitter, cancellationToken);
                        continue;
                    }
                }

                return HttpResponseResult<T>.Failure((int)response.StatusCode, errorMessage, responseBody, headers);
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                if (attempt < maxRetries)
                {
                    var jitter = Random.Shared.Next(0, 100);
                    await Task.Delay(retryDelayMs * (attempt + 1) + jitter, cancellationToken);
                    continue;
                }
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                lastException = ex;
                if (attempt < maxRetries)
                {
                    var jitter = Random.Shared.Next(0, 100);
                    await Task.Delay(retryDelayMs * (attempt + 1) + jitter, cancellationToken);
                    continue;
                }
            }
            catch (Exception ex)
            {
                return HttpResponseResult<T>.Failure(0, $"Unexpected error: {ex.Message}", null);
            }
        }

        return HttpResponseResult<T>.Failure(
            0,
            $"Request failed after {maxRetries + 1} attempts. Last error: {lastException?.Message ?? "Unknown"}",
            null
        );
    }

    /// <summary>
    /// Sends a form-urlencoded request with retry logic.
    /// </summary>
    public static async Task<HttpResponseResult<T>> SendFormUrlEncodedAsync<T>(
        HttpClient httpClient,
        string url,
        IEnumerable<KeyValuePair<string, string>> formData,
        CancellationToken cancellationToken = default,
        int maxRetries = 3,
        int retryDelayMs = 1000,
        Dictionary<string, string>? customHeaders = null)
    {
        Exception? lastException = null;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new FormUrlEncodedContent(formData)
                };

                // Add custom headers if provided
                if (customHeaders != null)
                {
                    foreach (var header in customHeaders)
                    {
                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }

                var response = await httpClient.SendAsync(request, cancellationToken);

                var headers = new Dictionary<string, IEnumerable<string>>();
                foreach (var header in response.Headers)
                {
                    headers[header.Key] = header.Value;
                }
                foreach (var header in response.Content.Headers)
                {
                    headers[header.Key] = header.Value;
                }

                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        var data = JsonSerializer.Deserialize<T>(responseBody, JsonOptions);
                        return HttpResponseResult<T>.Success(data!, (int)response.StatusCode, headers);
                    }
                    catch (JsonException ex)
                    {
                        return HttpResponseResult<T>.Failure(
                            (int)response.StatusCode,
                            $"Failed to deserialize response: {ex.Message}",
                            responseBody,
                            headers
                        );
                    }
                }

                var errorMessage = $"HTTP {(int)response.StatusCode} {response.StatusCode}";

                try
                {
                    var errorObj = JsonSerializer.Deserialize<Dictionary<string, object>>(responseBody, JsonOptions);
                    if (errorObj != null && errorObj.TryGetValue("error", out var error))
                    {
                        errorMessage += $": {error}";
                    }
                    else if (errorObj != null && errorObj.TryGetValue("error_description", out var errorDesc))
                    {
                        errorMessage += $": {errorDesc}";
                    }
                }
                catch
                {
                    // Ignore JSON parsing errors
                }

                // Retry on 5xx or 429
                if ((int)response.StatusCode >= 500 || response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    if (attempt < maxRetries)
                    {
                        var jitter = Random.Shared.Next(0, 100);
                        await Task.Delay(retryDelayMs * (attempt + 1) + jitter, cancellationToken);
                        continue;
                    }
                }

                return HttpResponseResult<T>.Failure((int)response.StatusCode, errorMessage, responseBody, headers);
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                if (attempt < maxRetries)
                {
                    var jitter = Random.Shared.Next(0, 100);
                    await Task.Delay(retryDelayMs * (attempt + 1) + jitter, cancellationToken);
                    continue;
                }
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                lastException = ex;
                if (attempt < maxRetries)
                {
                    var jitter = Random.Shared.Next(0, 100);
                    await Task.Delay(retryDelayMs * (attempt + 1) + jitter, cancellationToken);
                    continue;
                }
            }
            catch (Exception ex)
            {
                return HttpResponseResult<T>.Failure(0, $"Unexpected error: {ex.Message}", null);
            }
        }

        return HttpResponseResult<T>.Failure(
            0,
            $"Request failed after {maxRetries + 1} attempts. Last error: {lastException?.Message ?? "Unknown"}",
            null
        );
    }
}
