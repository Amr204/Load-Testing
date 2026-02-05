using System.Text.Json.Serialization;

namespace CashlessLoadTest.Worker;

// ============================================================================
// Request/Response Models
// ============================================================================
public class LoginResponse
{
    public string access_token { get; set; } = string.Empty;
    public int expires_in { get; set; }
    public string? error { get; set; }
    public string? error_description { get; set; }
}

public class CreateTransferRequest
{
    [JsonPropertyName("publicIdentifier")]
    public string PublicIdentifier { get; set; } = string.Empty;

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "YER";

    [JsonPropertyName("amount")]
    public int Amount { get; set; } = 100;

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "DFrame stress test";
}

public class CreateTransferResponse
{
    public string id { get; set; } = string.Empty;
    public long? operationNumber { get; set; }
    public string? state { get; set; }
    public string? error { get; set; }
    public string? errorMessage { get; set; }
}

public class ConfirmTransferRequest
{
    [JsonPropertyName("publicIdentifier")]
    public string PublicIdentifier { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; set; } = "0";
}

public class ConfirmTransferResponse
{
    public string? id { get; set; }
    public string? state { get; set; }
    public string? error { get; set; }
    public string? errorMessage { get; set; }
}

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