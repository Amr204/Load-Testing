using System.Text.Json;

namespace CashlessLoadTest.Worker;

// ============================================================================
// Transfer Store (file-based transfer ID storage)
// ============================================================================
public class TransferStoreEntry
{
    public string TransferId { get; set; } = string.Empty;
    public string SenderPhone { get; set; } = string.Empty;
    public string ReceiverPhone { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool Confirmed { get; set; }
    public DateTime? ConfirmedAt { get; set; }

    // Claiming mechanism for distributed processing
    public bool Claimed { get; set; }
    public string? ClaimedByWorker { get; set; }
    public DateTime? ClaimedAt { get; set; }
}

public class TransferStoreStatistics
{
    public int TotalTransfers { get; set; }
    public int PendingTransfers { get; set; }
    public int ConfirmedTransfers { get; set; }
    public int ClaimedTransfers { get; set; }
    public int UnclaimedTransfers { get; set; }
    public int UniqueSenderReceiverPairs { get; set; }
    public DateTime LastCacheRefresh { get; set; }
}

public static class TransferStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };
    private static readonly object _lock = new object();
    
    // In-memory cache for faster lookups (keyed by transferId)
    private static readonly Dictionary<string, TransferStoreEntry> _cache = new();
    
    // Index by sender-receiver pair for fast grouping queries
    private static readonly Dictionary<string, List<string>> _senderReceiverIndex = new(); // Key: "sender:receiver", Value: List of transferIds
    
    // Index by sender for fast sender queries
    private static readonly Dictionary<string, List<string>> _senderIndex = new(); // Key: senderPhone, Value: List of transferIds
    
    // Track when cache was last refreshed
    private static DateTime _lastCacheRefresh = DateTime.MinValue;
    private static readonly TimeSpan CacheRefreshInterval = TimeSpan.FromSeconds(30);
    
    // Claim timeout - releases stale claims after this duration
    private static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(5);
    
    // Unique worker identifier (MachineName + ProcessId)
    public static string WorkerId { get; } = $"{Environment.MachineName}-{Environment.ProcessId}";

    private static string GetStoreFilePath(string transferId)
    {
        // Ensure store directory exists
        Directory.CreateDirectory(Config.TransferStoreDirectory);

        // Sanitize transfer ID for filename
        var safeTransferId = transferId.Replace(" ", "_").Replace("/", "_").Replace("\\", "_").Replace(":", "_");
        return Path.Combine(Config.TransferStoreDirectory, $"transfer_{safeTransferId}.json");
    }
    
    private static string GetGroupKey(string senderPhone, string receiverPhone)
    {
        return $"{senderPhone}:{receiverPhone}";
    }
    
    private static void RefreshCacheIfNeeded()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastCacheRefresh) < CacheRefreshInterval && _cache.Count > 0)
        {
            return; // Cache is still fresh
        }
        
        _cache.Clear();
        _senderReceiverIndex.Clear();
        _senderIndex.Clear();
        
        if (!Directory.Exists(Config.TransferStoreDirectory))
        {
            _lastCacheRefresh = now;
            return;
        }
        
        var transferFiles = Directory.GetFiles(Config.TransferStoreDirectory, "transfer_*.json");
        
        foreach (var filePath in transferFiles)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var entry = JsonSerializer.Deserialize<TransferStoreEntry>(json, JsonOptions);
                
                if (entry == null || string.IsNullOrEmpty(entry.TransferId))
                    continue;
                
                // Release stale claims (claimed for more than ClaimTimeout)
                if (entry.Claimed && entry.ClaimedAt.HasValue)
                {
                    var claimAge = now - entry.ClaimedAt.Value;
                    if (claimAge > ClaimTimeout)
                    {
                        entry.Claimed = false;
                        entry.ClaimedByWorker = null;
                        entry.ClaimedAt = null;
                        // Persist the release
                        try
                        {
                            var staleFilePath = GetStoreFilePath(entry.TransferId);
                            var staleJson = JsonSerializer.Serialize(entry, JsonOptions);
                            var staleTempPath = staleFilePath + ".tmp";
                            File.WriteAllText(staleTempPath, staleJson);
                            File.Move(staleTempPath, staleFilePath, overwrite: true);
                        }
                        catch
                        {
                            // Ignore errors during stale claim release
                        }
                    }
                }
                
                // Add to cache
                _cache[entry.TransferId] = entry;
                
                // Add to sender-receiver index
                var groupKey = GetGroupKey(entry.SenderPhone, entry.ReceiverPhone);
                if (!_senderReceiverIndex.ContainsKey(groupKey))
                {
                    _senderReceiverIndex[groupKey] = new List<string>();
                }
                _senderReceiverIndex[groupKey].Add(entry.TransferId);
                
                // Add to sender index
                if (!_senderIndex.ContainsKey(entry.SenderPhone))
                {
                    _senderIndex[entry.SenderPhone] = new List<string>();
                }
                _senderIndex[entry.SenderPhone].Add(entry.TransferId);
            }
            catch
            {
                // Skip corrupted files
                continue;
            }
        }
        
        _lastCacheRefresh = now;
    }
    
    private static void UpdateIndexes(TransferStoreEntry entry, bool isAdd)
    {
        var groupKey = GetGroupKey(entry.SenderPhone, entry.ReceiverPhone);
        
        if (isAdd)
        {
            // Add to sender-receiver index
            if (!_senderReceiverIndex.ContainsKey(groupKey))
            {
                _senderReceiverIndex[groupKey] = new List<string>();
            }
            if (!_senderReceiverIndex[groupKey].Contains(entry.TransferId))
            {
                _senderReceiverIndex[groupKey].Add(entry.TransferId);
            }
            
            // Add to sender index
            if (!_senderIndex.ContainsKey(entry.SenderPhone))
            {
                _senderIndex[entry.SenderPhone] = new List<string>();
            }
            if (!_senderIndex[entry.SenderPhone].Contains(entry.TransferId))
            {
                _senderIndex[entry.SenderPhone].Add(entry.TransferId);
            }
        }
        else
        {
            // Remove from indexes (when confirmed)
            if (_senderReceiverIndex.ContainsKey(groupKey))
            {
                _senderReceiverIndex[groupKey].Remove(entry.TransferId);
                if (_senderReceiverIndex[groupKey].Count == 0)
                {
                    _senderReceiverIndex.Remove(groupKey);
                }
            }
            
            if (_senderIndex.ContainsKey(entry.SenderPhone))
            {
                _senderIndex[entry.SenderPhone].Remove(entry.TransferId);
                if (_senderIndex[entry.SenderPhone].Count == 0)
                {
                    _senderIndex.Remove(entry.SenderPhone);
                }
            }
        }
    }

    public static void SaveTransfer(string transferId, string senderPhone, string receiverPhone)
    {
        if (string.IsNullOrEmpty(transferId) || string.IsNullOrEmpty(senderPhone) || string.IsNullOrEmpty(receiverPhone))
        {
            Console.WriteLine($"[TransferStore] Invalid parameters for SaveTransfer | transferId={transferId} | sender={senderPhone} | receiver={receiverPhone}");
            return;
        }
        
        lock (_lock)
        {
            try
            {
                var entry = new TransferStoreEntry
                {
                    TransferId = transferId,
                    SenderPhone = senderPhone,
                    ReceiverPhone = receiverPhone,
                    CreatedAt = DateTime.UtcNow,
                    Confirmed = false
                };

                var filePath = GetStoreFilePath(transferId);
                var json = JsonSerializer.Serialize(entry, JsonOptions);
                
                // Write atomically using temp file
                var tempPath = filePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, filePath, overwrite: true);
                
                // Update cache and indexes
                _cache[transferId] = entry;
                UpdateIndexes(entry, isAdd: true);
                
                Console.WriteLine($"[TransferStore] Saved transfer | transferId={transferId} | sender={senderPhone} | receiver={receiverPhone}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TransferStore] Error saving transfer {transferId}: {ex.Message}");
                // Don't throw - transfer storage is optional
            }
        }
    }

    public static TransferStoreEntry? LoadTransfer(string transferId)
    {
        if (string.IsNullOrEmpty(transferId))
            return null;
        
        lock (_lock)
        {
            try
            {
                // Check cache first
                if (_cache.TryGetValue(transferId, out var cachedEntry))
                {
                    return cachedEntry;
                }
                
                // Cache miss - load from file
                var filePath = GetStoreFilePath(transferId);
                if (!File.Exists(filePath))
                {
                    return null;
                }

                var json = File.ReadAllText(filePath);
                var entry = JsonSerializer.Deserialize<TransferStoreEntry>(json, JsonOptions);
                
                if (entry != null && !string.IsNullOrEmpty(entry.TransferId))
                {
                    // Update cache
                    _cache[transferId] = entry;
                    UpdateIndexes(entry, isAdd: true);
                }

                return entry;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TransferStore] Error loading transfer {transferId}: {ex.Message}");
                return null;
            }
        }
    }

    public static TransferStoreEntry? GetPendingTransfer(string? senderPhone = null, string? receiverPhone = null)
    {
        lock (_lock)
        {
            try
            {
                RefreshCacheIfNeeded();
                
                // Use indexes for faster lookup
                List<string>? candidateTransferIds = null;
                
                if (!string.IsNullOrEmpty(senderPhone) && !string.IsNullOrEmpty(receiverPhone))
                {
                    // Both specified - use sender-receiver index
                    var groupKey = GetGroupKey(senderPhone, receiverPhone);
                    if (_senderReceiverIndex.TryGetValue(groupKey, out var transferIds))
                    {
                        candidateTransferIds = transferIds;
                    }
                }
                else if (!string.IsNullOrEmpty(senderPhone))
                {
                    // Only sender specified - use sender index
                    if (_senderIndex.TryGetValue(senderPhone, out var transferIds))
                    {
                        candidateTransferIds = transferIds;
                    }
                }
                else
                {
                    // No filter - get all pending transfers from cache
                    candidateTransferIds = _cache.Values
                        .Where(e => !e.Confirmed)
                        .Select(e => e.TransferId)
                        .ToList();
                }
                
                if (candidateTransferIds == null || candidateTransferIds.Count == 0)
                {
                    return null;
                }
                
                // Find first unclaimed pending transfer (sorted by CreatedAt)
                var pendingEntries = candidateTransferIds
                    .Select(id => _cache.TryGetValue(id, out var entry) ? entry : null)
                    .Where(e => e != null && !e.Confirmed && !e.Claimed)
                    .OrderBy(e => e!.CreatedAt)
                    .ToList();
                
                return pendingEntries.FirstOrDefault();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TransferStore] Error getting pending transfer: {ex.Message}");
                return null;
            }
        }
    }
    
    /// <summary>
    /// Atomically claims a pending transfer for processing by this worker.
    /// Returns the claimed transfer entry if successful, null if no unclaimed transfer available.
    /// </summary>
    public static TransferStoreEntry? ClaimPendingTransfer(string? senderPhone = null, string? receiverPhone = null)
    {
        lock (_lock)
        {
            try
            {
                RefreshCacheIfNeeded();
                
                // Use indexes for faster lookup
                List<string>? candidateTransferIds = null;
                
                if (!string.IsNullOrEmpty(senderPhone) && !string.IsNullOrEmpty(receiverPhone))
                {
                    // Both specified - use sender-receiver index
                    var groupKey = GetGroupKey(senderPhone, receiverPhone);
                    if (_senderReceiverIndex.TryGetValue(groupKey, out var transferIds))
                    {
                        candidateTransferIds = transferIds;
                    }
                }
                else if (!string.IsNullOrEmpty(senderPhone))
                {
                    // Only sender specified - use sender index
                    if (_senderIndex.TryGetValue(senderPhone, out var transferIds))
                    {
                        candidateTransferIds = transferIds;
                    }
                }
                else
                {
                    // No filter - get all pending transfers from cache
                    candidateTransferIds = _cache.Values
                        .Where(e => !e.Confirmed)
                        .Select(e => e.TransferId)
                        .ToList();
                }
                
                if (candidateTransferIds == null || candidateTransferIds.Count == 0)
                {
                    return null;
                }
                
                // Find first unclaimed pending transfer (sorted by CreatedAt)
                var unclaimedEntries = candidateTransferIds
                    .Select(id => _cache.TryGetValue(id, out var entry) ? entry : null)
                    .Where(e => e != null && !e.Confirmed && !e.Claimed)
                    .OrderBy(e => e!.CreatedAt)
                    .ToList();
                
                if (unclaimedEntries.Count == 0)
                {
                    return null;
                }
                
                // Atomically claim the first unclaimed transfer
                var transferToClaim = unclaimedEntries.First()!;
                
                // Double-check it's still unclaimed (another thread might have claimed it)
                if (transferToClaim.Claimed)
                {
                    // Try next one
                    return unclaimedEntries.Skip(1).FirstOrDefault();
                }
                
                // Claim it
                transferToClaim.Claimed = true;
                transferToClaim.ClaimedByWorker = WorkerId;
                transferToClaim.ClaimedAt = DateTime.UtcNow;
                
                // Persist the claim atomically
                var filePath = GetStoreFilePath(transferToClaim.TransferId);
                var json = JsonSerializer.Serialize(transferToClaim, JsonOptions);
                var tempPath = filePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, filePath, overwrite: true);
                
                // Update cache
                _cache[transferToClaim.TransferId] = transferToClaim;
                
                Console.WriteLine($"[TransferStore] Claimed transfer | transferId={transferToClaim.TransferId} | worker={WorkerId} | sender={transferToClaim.SenderPhone} | receiver={transferToClaim.ReceiverPhone}");
                
                return transferToClaim;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TransferStore] Error claiming pending transfer: {ex.Message}");
                return null;
            }
        }
    }
    
    /// <summary>
    /// Releases a claim on a transfer (e.g., if processing failed and should be retried).
    /// </summary>
    public static void ReleaseClaim(string transferId)
    {
        if (string.IsNullOrEmpty(transferId))
            return;
        
        lock (_lock)
        {
            try
            {
                var entry = LoadTransfer(transferId);
                if (entry == null)
                {
                    return;
                }
                
                if (!entry.Claimed || entry.ClaimedByWorker != WorkerId)
                {
                    // Not claimed or claimed by different worker
                    return;
                }
                
                // Release claim
                entry.Claimed = false;
                entry.ClaimedByWorker = null;
                entry.ClaimedAt = null;
                
                // Persist the release
                var filePath = GetStoreFilePath(transferId);
                var json = JsonSerializer.Serialize(entry, JsonOptions);
                var tempPath = filePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, filePath, overwrite: true);
                
                // Update cache
                _cache[transferId] = entry;
                
                Console.WriteLine($"[TransferStore] Released claim | transferId={transferId} | worker={WorkerId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TransferStore] Error releasing claim for transfer {transferId}: {ex.Message}");
            }
        }
    }

    public static List<TransferStoreEntry> GetPendingTransfersBySenderReceiver(string senderPhone, string receiverPhone)
    {
        if (string.IsNullOrEmpty(senderPhone) || string.IsNullOrEmpty(receiverPhone))
        {
            return new List<TransferStoreEntry>();
        }
        
        lock (_lock)
        {
            try
            {
                RefreshCacheIfNeeded();
                
                var groupKey = GetGroupKey(senderPhone, receiverPhone);
                if (!_senderReceiverIndex.TryGetValue(groupKey, out var transferIds))
                {
                    return new List<TransferStoreEntry>();
                }
                
                var results = transferIds
                    .Select(id => _cache.TryGetValue(id, out var entry) ? entry : null)
                    .Where(e => e != null && !e.Confirmed)
                    .OrderBy(e => e!.CreatedAt)
                    .ToList();
                
                return results!;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TransferStore] Error getting pending transfers by sender/receiver: {ex.Message}");
                return new List<TransferStoreEntry>();
            }
        }
    }

    public static Dictionary<string, List<TransferStoreEntry>> GetPendingTransfersGroupedBySenderReceiver()
    {
        lock (_lock)
        {
            var grouped = new Dictionary<string, List<TransferStoreEntry>>();
            
            try
            {
                RefreshCacheIfNeeded();
                
                // Use sender-receiver index for fast grouping
                foreach (var kvp in _senderReceiverIndex)
                {
                    var groupKey = kvp.Key;
                    var transferIds = kvp.Value;
                    
                    var entries = transferIds
                        .Select(id => _cache.TryGetValue(id, out var entry) ? entry : null)
                        .Where(e => e != null && !e.Confirmed && !e.Claimed)
                        .OrderBy(e => e!.CreatedAt)
                        .ToList();
                    
                    if (entries.Count > 0)
                    {
                        grouped[groupKey] = entries!;
                    }
                }
                
                return grouped;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TransferStore] Error getting grouped pending transfers: {ex.Message}");
                return grouped;
            }
        }
    }

    public static void MarkAsConfirmed(string transferId)
    {
        if (string.IsNullOrEmpty(transferId))
            return;
        
        lock (_lock)
        {
            try
            {
                var entry = LoadTransfer(transferId);
                if (entry == null)
                {
                    Console.WriteLine($"[TransferStore] Transfer not found for confirmation | transferId={transferId}");
                    return;
                }
                
                if (entry.Confirmed)
                {
                    // Already confirmed, skip
                    return;
                }
                
                entry.Confirmed = true;
                entry.ConfirmedAt = DateTime.UtcNow;
                
                // Release claim when confirming
                entry.Claimed = false;
                entry.ClaimedByWorker = null;
                entry.ClaimedAt = null;
                
                var filePath = GetStoreFilePath(transferId);
                var json = JsonSerializer.Serialize(entry, JsonOptions);
                
                var tempPath = filePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, filePath, overwrite: true);
                
                // Update cache and indexes
                _cache[transferId] = entry;
                UpdateIndexes(entry, isAdd: false); // Remove from pending indexes
                
                Console.WriteLine($"[TransferStore] Marked transfer as confirmed | transferId={transferId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TransferStore] Error marking transfer {transferId} as confirmed: {ex.Message}");
            }
        }
    }
    
    public static TransferStoreStatistics GetStatistics()
    {
        lock (_lock)
        {
            RefreshCacheIfNeeded();
            
            var total = _cache.Count;
            var pending = _cache.Values.Count(e => !e.Confirmed);
            var confirmed = _cache.Values.Count(e => e.Confirmed);
            var senderReceiverPairs = _senderReceiverIndex.Count;
            
            return new TransferStoreStatistics
            {
                TotalTransfers = total,
                PendingTransfers = pending,
                ConfirmedTransfers = confirmed,
                UniqueSenderReceiverPairs = senderReceiverPairs,
                LastCacheRefresh = _lastCacheRefresh
            };
        }
    }
    
    public static void ClearCache()
    {
        lock (_lock)
        {
            _cache.Clear();
            _senderReceiverIndex.Clear();
            _senderIndex.Clear();
            _lastCacheRefresh = DateTime.MinValue;
            Console.WriteLine("[TransferStore] Cache cleared");
        }
    }

    public static void ClearConfirmedTransfers(int olderThanHours = 24)
    {
        lock (_lock)
        {
            try
            {
                RefreshCacheIfNeeded();
                
                var clearedCount = 0;
                var cutoffTime = DateTime.UtcNow.AddHours(-olderThanHours);
                var transfersToDelete = new List<string>();

                // Find transfers to delete using cache
                foreach (var kvp in _cache)
                {
                    var entry = kvp.Value;
                    if (entry.Confirmed && entry.ConfirmedAt.HasValue && entry.ConfirmedAt.Value < cutoffTime)
                    {
                        transfersToDelete.Add(entry.TransferId);
                    }
                }

                // Delete files and remove from cache/indexes
                foreach (var transferId in transfersToDelete)
                {
                    try
                    {
                        var filePath = GetStoreFilePath(transferId);
                        if (File.Exists(filePath))
                        {
                            File.Delete(filePath);
                        }
                        
                        // Remove from cache and indexes
                        if (_cache.TryGetValue(transferId, out var entry))
                        {
                            UpdateIndexes(entry, isAdd: false);
                            _cache.Remove(transferId);
                        }
                        
                        clearedCount++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[TransferStore] Error deleting transfer file {transferId}: {ex.Message}");
                    }
                }

                if (clearedCount > 0)
                {
                    Console.WriteLine($"[TransferStore] Cleared {clearedCount} confirmed transfer(s) older than {olderThanHours} hours");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TransferStore] Error clearing confirmed transfers: {ex.Message}");
            }
        }
    }
}
