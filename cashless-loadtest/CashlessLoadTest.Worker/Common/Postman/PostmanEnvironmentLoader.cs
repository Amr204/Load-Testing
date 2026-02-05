using System.Text.Json;
using System.Text.Json.Nodes;

namespace CashlessLoadTest.Worker.Common.Postman;

/// <summary>
/// Loads and parses Postman environment JSON files.
/// </summary>
public class PostmanEnvironmentLoader
{
    public Dictionary<string, string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static PostmanEnvironmentLoader Load(string filePath)
    {
        var loader = new PostmanEnvironmentLoader();
        
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"[PostmanEnv] File not found: {filePath}");
            return loader;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var doc = JsonDocument.Parse(json);
            
            if (doc.RootElement.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in values.EnumerateArray())
                {
                    var enabled = true;
                    if (item.TryGetProperty("enabled", out var enabledProp))
                    {
                        enabled = enabledProp.GetBoolean();
                    }

                    if (!enabled) continue;

                    if (item.TryGetProperty("key", out var key) && item.TryGetProperty("value", out var value))
                    {
                        var keyStr = key.GetString() ?? "";
                        var valueStr = value.GetString() ?? "";
                        if (!string.IsNullOrEmpty(keyStr))
                        {
                            loader.Variables[keyStr] = valueStr;
                        }
                    }
                }
            }

            Console.WriteLine($"[PostmanEnv] Loaded {loader.Variables.Count} variables from {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PostmanEnv] Error loading {filePath}: {ex.Message}");
        }

        return loader;
    }

    public string GetValue(string key, string defaultValue = "")
    {
        return Variables.TryGetValue(key, out var value) ? value : defaultValue;
    }
}
