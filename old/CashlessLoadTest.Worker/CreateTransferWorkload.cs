using DFrame;

namespace CashlessLoadTest.Worker;

// ============================================================================
// Create Transfer Workload (Create Only)
// ============================================================================
public class CreateTransferWorkload : BaseWorkload
{
    // Store transfer info for TeardownAsync
    private string? _lastTransferId;
    private string? _lastReceiverPhone;

    public CreateTransferWorkload(HttpClient httpClient, string baseUrl = "") : base(httpClient, baseUrl)
    {
    }

    public override async Task SetupAsync(WorkloadContext context)
    {
        _senderPhone = Config.Users[context.WorkloadIndex % Config.Users.Length];

        // Load and validate token in SetupAsync
        await EnsureValidTokenAsync(context.CancellationToken);

        if (_token == null)
        {
            throw new InvalidOperationException($"Failed to login during setup for user {_senderPhone}");
        }
    }

    public override async Task ExecuteAsync(WorkloadContext context)
    {
        Console.WriteLine($"[CreateTransferWorkload] [VU {_senderPhone}] ExecuteAsync START | ExecuteCount={context.ExecuteCount}");

        // Ensure valid token before each execution (token may expire during long runs)
        await EnsureValidTokenAsync(context.CancellationToken);
        if (_token == null)
            throw new InvalidOperationException("Failed to obtain valid token");

        var receiverPhone = PickReceiverDifferentFrom(_senderPhone!);

        // ========================= MEASURED EXECUTION - Create Transfer API Call =========================
        var createRequest = new CreateTransferRequest
        {
            PublicIdentifier = receiverPhone,
            Currency = "YER",
            Amount = 100,
            Notes = "DFrame stress test"
        };

        var createResult = await HttpHelper.SendRequestAsync<CreateTransferResponse>(
            _httpClient,
            HttpMethod.Post,
            $"{_baseUrl}/api/wallet/demostictransfer",
            createRequest,
            _token,
            context.CancellationToken,
            Config.MaxRetries,
            Config.RetryDelayMs
        );

        // Validate response
        if (!createResult.IsSuccess || createResult.Data == null || string.IsNullOrEmpty(createResult.Data.id) || !string.IsNullOrEmpty(createResult.Data.error))
        {
            _failedRequests++;
            throw new HttpRequestException($"Create transfer failed: {createResult.ErrorMessage}");
        }

        _successfulRequests++;
        _lastTransferId = createResult.Data.id;
        _lastReceiverPhone = receiverPhone;

        await Task.Delay(1, context.CancellationToken);

        Console.WriteLine($"[CreateTransferWorkload] [VU {_senderPhone}] ExecuteAsync END | ExecuteCount={context.ExecuteCount} | success={_successfulRequests} | failed={_failedRequests} | transferId={_lastTransferId}");
    }

    public override async Task TeardownAsync(WorkloadContext context)
    {
        // Save transfer ID to store for later confirmation (in TeardownAsync, not ExecuteAsync)
        if (!string.IsNullOrEmpty(_lastTransferId) && !string.IsNullOrEmpty(_lastReceiverPhone))
        {
            TransferStore.SaveTransfer(_lastTransferId, _senderPhone!, _lastReceiverPhone);

            // Get count of pending transfers for this sender-receiver pair
            var transfersForPair = TransferStore.GetPendingTransfersBySenderReceiver(_senderPhone!, _lastReceiverPhone);
            Console.WriteLine($"[CreateTransferWorkload] [VU {_senderPhone}] Saved transfer to store | transferId={_lastTransferId} | receiver={_lastReceiverPhone} | pendingForPair={transfersForPair.Count}");
        }
        await Task.CompletedTask;
    }

    public override Dictionary<string, string>? Complete(WorkloadContext context)
    {
        return new Dictionary<string, string>
        {
            { "SenderPhone", _senderPhone ?? "Unknown" },
            { "SuccessfulCreates", _successfulRequests.ToString() },
            { "FailedCreates", _failedRequests.ToString() },
            { "TokenCacheHits", _tokenCacheHits.ToString() },
            { "TokenCacheMisses", _tokenCacheMisses.ToString() },
            { "TotalExecutions", context.ExecuteCount.ToString() }
        };
    }
}
