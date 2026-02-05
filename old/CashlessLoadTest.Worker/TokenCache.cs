using System.Text.Json;

namespace CashlessLoadTest.Worker;

// ============================================================================
// Token Cache (file-based token storage)
// ============================================================================
public class TokenCacheEntry
{
    public string Token { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime SavedAt { get; set; }
}

public static class TokenCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private static string GetCacheFilePath(string phoneNumber)
    {
        // Ensure cache directory exists
        Directory.CreateDirectory(Config.TokenCacheDirectory);

        // Sanitize phone number for filename
        var safePhone = phoneNumber.Replace(" ", "_").Replace("/", "_");
        return Path.Combine(Config.TokenCacheDirectory, $"token_{safePhone}.json");
    }

    public static TokenCacheEntry? LoadToken(string phoneNumber)
    {
        try
        {
            var filePath = GetCacheFilePath(phoneNumber);

            if (!File.Exists(filePath))
            {
                return null;
            }

            var json = File.ReadAllText(filePath);
            var entry = JsonSerializer.Deserialize<TokenCacheEntry>(json, JsonOptions);

            if (entry == null)
            {
                return null;
            }

            // Check if token is expired
            if (DateTime.UtcNow >= entry.ExpiresAt)
            {
                // Token expired, delete cache file
                try
                {
                    File.Delete(filePath);
                }
                catch
                {
                    // Ignore delete errors
                }
                return null;
            }

            return entry;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TokenCache] Error loading token for {phoneNumber}: {ex.Message}");
            return null;
        }
    }

    public static void SaveToken(string phoneNumber, string token, DateTime expiresAt)
    {
        try
        {
            var entry = new TokenCacheEntry
            {
                Token = token,
                PhoneNumber = phoneNumber,
                ExpiresAt = expiresAt,
                SavedAt = DateTime.UtcNow
            };

            var filePath = GetCacheFilePath(phoneNumber);
            var json = JsonSerializer.Serialize(entry, JsonOptions);

            // Write atomically using temp file
            var tempPath = filePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TokenCache] Error saving token for {phoneNumber}: {ex.Message}");
            // Don't throw - token caching is optional
        }
    }

    public static void ClearToken(string phoneNumber)
    {
        try
        {
            var filePath = GetCacheFilePath(phoneNumber);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // Ignore errors
        }
    }

    public static void ClearExpiredTokens()
    {
        try
        {
            if (!Directory.Exists(Config.TokenCacheDirectory))
            {
                return;
            }

            var cacheFiles = Directory.GetFiles(Config.TokenCacheDirectory, "token_*.json");
            var clearedCount = 0;

            foreach (var filePath in cacheFiles)
            {
                try
                {
                    var json = File.ReadAllText(filePath);
                    var entry = JsonSerializer.Deserialize<TokenCacheEntry>(json, JsonOptions);

                    if (entry == null || DateTime.UtcNow >= entry.ExpiresAt)
                    {
                        File.Delete(filePath);
                        clearedCount++;
                    }
                }
                catch
                {
                    // If file is corrupted or unreadable, delete it
                    try
                    {
                        File.Delete(filePath);
                        clearedCount++;
                    }
                    catch
                    {
                        // Ignore delete errors
                    }
                }
            }

            if (clearedCount > 0)
            {
                Console.WriteLine($"[TokenCache] Cleared {clearedCount} expired token(s)");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TokenCache] Error clearing expired tokens: {ex.Message}");
        }
    }
}
