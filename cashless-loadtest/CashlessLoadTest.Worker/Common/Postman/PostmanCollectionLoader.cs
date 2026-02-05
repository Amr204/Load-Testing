using System.Text.Json;
using System.Text.Json.Nodes;

namespace CashlessLoadTest.Worker.Common.Postman;

/// <summary>
/// Represents a Postman request extracted from collection.
/// </summary>
public class PostmanRequest
{
    public string Name { get; set; } = "";
    public string Method { get; set; } = "GET";
    public string UrlPath { get; set; } = "";
    public string RawBody { get; set; } = "";
    public string BodyMode { get; set; } = "raw"; // raw, urlencoded
    public Dictionary<string, string> Headers { get; } = new();
    public Dictionary<string, string> FormData { get; } = new(); // for urlencoded
    public bool RequiresAuth { get; set; } = false;
}

/// <summary>
/// Loads and parses Postman collection JSON files.
/// </summary>
public class PostmanCollectionLoader
{
    public PostmanRequest? TokenRequest { get; private set; }
    public PostmanRequest? RegisterRequest { get; private set; }
    public PostmanRequest? VerifyRequest { get; private set; }

    public static PostmanCollectionLoader Load(string filePath)
    {
        var loader = new PostmanCollectionLoader();

        if (!File.Exists(filePath))
        {
            Console.WriteLine($"[PostmanCollection] File not found: {filePath}");
            return loader;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);
            
            // Recursively search for requests
            if (doc.RootElement.TryGetProperty("item", out var items))
            {
                loader.ParseItems(items);
            }

            Console.WriteLine($"[PostmanCollection] Loaded requests: Token={loader.TokenRequest != null}, Register={loader.RegisterRequest != null}, Verify={loader.VerifyRequest != null}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PostmanCollection] Error loading {filePath}: {ex.Message}");
        }

        return loader;
    }

    private void ParseItems(JsonElement items)
    {
        if (items.ValueKind != JsonValueKind.Array) return;

        foreach (var item in items.EnumerateArray())
        {
            // Check if this is a folder (has nested items)
            if (item.TryGetProperty("item", out var nestedItems))
            {
                ParseItems(nestedItems);
                continue;
            }

            // It's a request
            if (item.TryGetProperty("name", out var nameProp) && item.TryGetProperty("request", out var requestProp))
            {
                var name = nameProp.GetString() ?? "";
                var request = ParseRequest(name, requestProp);

                // Match by name
                if (name.Equals("token", StringComparison.OrdinalIgnoreCase))
                {
                    TokenRequest = request;
                }
                else if (name.Equals("register", StringComparison.OrdinalIgnoreCase))
                {
                    RegisterRequest = request;
                }
                else if (name.Equals("verify", StringComparison.OrdinalIgnoreCase))
                {
                    VerifyRequest = request;
                }
            }
        }
    }

    private PostmanRequest ParseRequest(string name, JsonElement requestElement)
    {
        var request = new PostmanRequest { Name = name };

        // Method
        if (requestElement.TryGetProperty("method", out var methodProp))
        {
            request.Method = methodProp.GetString() ?? "GET";
        }

        // URL
        if (requestElement.TryGetProperty("url", out var urlProp))
        {
            if (urlProp.ValueKind == JsonValueKind.String)
            {
                request.UrlPath = urlProp.GetString() ?? "";
            }
            else if (urlProp.TryGetProperty("raw", out var rawUrlProp))
            {
                request.UrlPath = rawUrlProp.GetString() ?? "";
            }
        }

        // Headers
        if (requestElement.TryGetProperty("header", out var headersProp) && headersProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var header in headersProp.EnumerateArray())
            {
                if (header.TryGetProperty("key", out var keyProp) && header.TryGetProperty("value", out var valueProp))
                {
                    var key = keyProp.GetString() ?? "";
                    var value = valueProp.GetString() ?? "";
                    if (!string.IsNullOrEmpty(key))
                    {
                        request.Headers[key] = value;
                    }
                }
            }
        }

        // Auth
        if (requestElement.TryGetProperty("auth", out var authProp))
        {
            request.RequiresAuth = true;
        }

        // Body
        if (requestElement.TryGetProperty("body", out var bodyProp))
        {
            if (bodyProp.TryGetProperty("mode", out var modeProp))
            {
                request.BodyMode = modeProp.GetString() ?? "raw";
            }

            if (request.BodyMode == "raw" && bodyProp.TryGetProperty("raw", out var rawProp))
            {
                request.RawBody = rawProp.GetString() ?? "";
            }
            else if (request.BodyMode == "urlencoded" && bodyProp.TryGetProperty("urlencoded", out var urlencodedProp))
            {
                if (urlencodedProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var field in urlencodedProp.EnumerateArray())
                    {
                        var disabled = false;
                        if (field.TryGetProperty("disabled", out var disabledProp))
                        {
                            disabled = disabledProp.GetBoolean();
                        }
                        if (disabled) continue;

                        if (field.TryGetProperty("key", out var keyProp) && field.TryGetProperty("value", out var valueProp))
                        {
                            var key = keyProp.GetString() ?? "";
                            var value = valueProp.GetString() ?? "";
                            if (!string.IsNullOrEmpty(key))
                            {
                                request.FormData[key] = value;
                            }
                        }
                    }
                }
            }
        }

        return request;
    }
}
