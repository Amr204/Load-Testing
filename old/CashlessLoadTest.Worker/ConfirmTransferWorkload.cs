using DFrame;

namespace CashlessLoadTest.Worker;

// ============================================================================
// Confirm Transfer Workload (Confirm Only)
// ============================================================================
public class ConfirmTransferWorkload : BaseWorkload
{
    // Parameters: transferId and receiverPhone (can be set from Web UI)
    public ConfirmTransferWorkload(HttpClient httpClient, string transferId = "", string receiverPhone = "", string baseUrl = "")
        : base(httpClient, baseUrl)
    {
        // If transferId not provided, it will be loaded from TransferStore in SetupAsync
        TransferId = transferId;
        ReceiverPhone = receiverPhone;
    }

    public string TransferId { get; private set; }
    public string ReceiverPhone { get; private set; }

    public override async Task SetupAsync(WorkloadContext context)
    {
        _senderPhone = Config.Users[context.WorkloadIndex % Config.Users.Length];

        // Load token in SetupAsync
        await EnsureValidTokenAsync(context.CancellationToken);
        if (_token == null)
        {
            Console.WriteLine($"[ConfirmTransferWorkload] [VU {_senderPhone}] Setup FAILED - Login failed");
            throw new InvalidOperationException($"Failed to login during setup for user {_senderPhone}");
        }

        // Cached values loaded from store (if TransferId was not provided)
        string? _loadedTransferId = null;
        string? _loadedReceiverPhone = null;

        // Load transfer from store in SetupAsync
        if (string.IsNullOrEmpty(TransferId))
        {
            // Try to get transfer matching both sender and receiver if receiverPhone is provided
            // Claim a pending transfer atomically (ensures only one worker processes it)
            TransferStoreEntry? pendingTransfer = null;
            if (!string.IsNullOrEmpty(ReceiverPhone))
            {
                pendingTransfer = TransferStore.ClaimPendingTransfer(_senderPhone, ReceiverPhone);
            }

            // Fallback to any pending transfer from this sender
            pendingTransfer ??= TransferStore.ClaimPendingTransfer(_senderPhone);

            if (pendingTransfer != null)
            {
                _loadedTransferId = pendingTransfer.TransferId;
                _loadedReceiverPhone = pendingTransfer.ReceiverPhone;
                Console.WriteLine($"[ConfirmTransferWorkload] [VU {_senderPhone}] Claimed pending transfer from store | transferId={_loadedTransferId} | receiver={_loadedReceiverPhone}");
            }
        }
        else
        {
            // If TransferId provided, load the transfer entry to get receiverPhone
            var transferEntry = TransferStore.LoadTransfer(TransferId);
            if (transferEntry != null)
            {
                _loadedTransferId = TransferId;
                _loadedReceiverPhone = transferEntry.ReceiverPhone;
                Console.WriteLine($"[ConfirmTransferWorkload] [VU {_senderPhone}] Loaded transfer from store | transferId={_loadedTransferId} | receiver={_loadedReceiverPhone}");
            }
        }

        var effectiveTransferId = string.IsNullOrWhiteSpace(_loadedTransferId) ? TransferId : _loadedTransferId;
        Console.WriteLine($"[ConfirmTransferWorkload] [VU {_senderPhone}] Setup started | WorkloadIndex={context.WorkloadIndex} | TransferId={effectiveTransferId}");

        if (string.IsNullOrEmpty(_loadedTransferId) && string.IsNullOrEmpty(TransferId))
        {
            throw new InvalidOperationException("TransferId parameter is required for ConfirmTransferWorkload. Please provide a valid transfer ID or ensure CreateTransferWorkload has created transfers.");
        }

        TransferId = effectiveTransferId;
        ReceiverPhone = _loadedReceiverPhone ?? ReceiverPhone;
        Console.WriteLine($"[ConfirmTransferWorkload] [VU {_senderPhone}] Setup completed successfully");
    }

    public override async Task ExecuteAsync(WorkloadContext context)
    {
        Console.WriteLine($"[ConfirmTransferWorkload] [VU {_senderPhone}] ExecuteAsync START | ExecuteCount={context.ExecuteCount}");

        // Ensure valid token before each execution (token may expire during long runs)
        await EnsureValidTokenAsync(context.CancellationToken);
        if (_token == null)
            throw new InvalidOperationException("Failed to obtain valid token");

        // Use transferId and receiverPhone loaded in SetupAsync
        if (string.IsNullOrEmpty(TransferId))
            throw new InvalidOperationException("TransferId is required but not loaded in SetupAsync");

        if (string.IsNullOrEmpty(ReceiverPhone))
            throw new InvalidOperationException("ReceiverPhone is required but not loaded in SetupAsync");

        // ========================= MEASURED EXECUTION - Confirm Transfer API Call =========================
        var confirmRequest = new ConfirmTransferRequest
        {
            PublicIdentifier = ReceiverPhone,
            Id = TransferId,
            Code = "0"
        };

        var confirmResult = await HttpHelper.SendRequestAsync<ConfirmTransferResponse>(
            _httpClient,
            HttpMethod.Post,
            $"{_baseUrl}/api/wallet/demostictransfer/confirm",
            confirmRequest,
            _token,
            context.CancellationToken,
            Config.MaxRetries,
            Config.RetryDelayMs
        );

        // Validate response
        if (!confirmResult.IsSuccess || (confirmResult.Data != null && !string.IsNullOrEmpty(confirmResult.Data.error)))
        {
            _failedRequests++;
            // Release claim on failure so another worker can retry
            try
            {
                var transferEntry = TransferStore.LoadTransfer(TransferId);
                if (transferEntry != null && transferEntry.ClaimedByWorker == TransferStore.WorkerId)
                {
                    TransferStore.ReleaseClaim(TransferId);
                    Console.WriteLine($"[ConfirmTransferWorkload] [VU {_senderPhone}] Released claim due to failure | transferId={TransferId}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConfirmTransferWorkload] [VU {_senderPhone}] Error releasing claim: {ex.Message}");
            }
            throw new HttpRequestException($"Confirm transfer failed: {confirmResult.ErrorMessage}");
        }

        _successfulRequests++;

        await Task.Delay(1, context.CancellationToken);

        Console.WriteLine($"[ConfirmTransferWorkload] [VU {_senderPhone}] ExecuteAsync END | ExecuteCount={context.ExecuteCount} | success={_successfulRequests} | failed={_failedRequests} | transferId={TransferId}");
    }

    public override async Task TeardownAsync(WorkloadContext context)
    {
        // Mark transfer as confirmed in store (in TeardownAsync, not ExecuteAsync)

        if (!string.IsNullOrEmpty(TransferId))
        {
            TransferStore.MarkAsConfirmed(TransferId);

            // Get remaining pending transfers for this sender-receiver pair
            if (!string.IsNullOrEmpty(ReceiverPhone))
            {
                var remainingTransfers = TransferStore.GetPendingTransfersBySenderReceiver(_senderPhone!, ReceiverPhone);
                Console.WriteLine($"[ConfirmTransferWorkload] [VU {_senderPhone}] Marked transfer as confirmed | transferId={TransferId} | receiver={ReceiverPhone} | remainingForPair={remainingTransfers.Count}");
            }
            else
            {
                Console.WriteLine($"[ConfirmTransferWorkload] [VU {_senderPhone}] Marked transfer as confirmed | transferId={TransferId}");
            }
        }
        await Task.CompletedTask;
    }

    public override Dictionary<string, string>? Complete(WorkloadContext context)
    {
        return new Dictionary<string, string>
        {
            { "SenderPhone", _senderPhone ?? "Unknown" },
            { "SuccessfulConfirms", _successfulRequests.ToString() },
            { "FailedConfirms", _failedRequests.ToString() },
            { "TokenCacheHits", _tokenCacheHits.ToString() },
            { "TokenCacheMisses", _tokenCacheMisses.ToString() },
            { "TotalExecutions", context.ExecuteCount.ToString() },
            { "TransferId", TransferId }
        };
    }
}
