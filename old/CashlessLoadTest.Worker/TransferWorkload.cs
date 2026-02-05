using DFrame;

namespace CashlessLoadTest.Worker;

// ============================================================================
// Transfer Workload (Combined Create + Confirm)
// ============================================================================
public class TransferWorkload : BaseWorkload
{
    public TransferWorkload(HttpClient httpClient, string baseUrl = "") : base(httpClient, baseUrl)
    {
    }

    public override async Task SetupAsync(WorkloadContext context)
    {
        // Initialize sender phone based on workload index
        _senderPhone = Config.Users[context.WorkloadIndex % Config.Users.Length];

        Console.WriteLine($"[TransferWorkload] [VU {_senderPhone}] Setup started | WorkloadIndex={context.WorkloadIndex}");

        // Initial login (uses token cache from BaseWorkload)
        await EnsureValidTokenAsync(context.CancellationToken);

        if (_token == null)
        {
            Console.WriteLine($"[TransferWorkload] [VU {_senderPhone}] Setup FAILED - Login failed");
            throw new InvalidOperationException($"Failed to login during setup for user {_senderPhone}");
        }

        Console.WriteLine($"[TransferWorkload] [VU {_senderPhone}] Setup completed successfully");
    }

    public override async Task ExecuteAsync(WorkloadContext context)
    {
        Console.WriteLine($"[TransferWorkload] [VU {_senderPhone}] ExecuteAsync START | ExecuteCount={context.ExecuteCount}");

        // Ensure valid token before each execution (token may expire)
        await EnsureValidTokenAsync(context.CancellationToken);
        if (_token == null)
            throw new InvalidOperationException("Failed to obtain valid token");

        var receiverPhone = PickReceiverDifferentFrom(_senderPhone!);

        // ========================= MEASURED EXECUTION - Create Transfer =========================
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

        // Validate create response
        if (!createResult.IsSuccess || createResult.Data == null || string.IsNullOrEmpty(createResult.Data.id) || !string.IsNullOrEmpty(createResult.Data.error))
        {
            _failedRequests++;
            throw new HttpRequestException($"Create transfer failed: {createResult.ErrorMessage}");
        }

        var transferId = createResult.Data.id;
        await Task.Delay(1, context.CancellationToken); // Sleep between create and confirm

        // Ensure token is still valid before confirm
        await EnsureValidTokenAsync(context.CancellationToken);
        if (_token == null)
            throw new InvalidOperationException("Token expired before confirm step");

        // ========================= MEASURED EXECUTION - Confirm Transfer =========================
        var confirmRequest = new ConfirmTransferRequest
        {
            PublicIdentifier = receiverPhone,
            Id = transferId,
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

        // Validate confirm response
        if (!confirmResult.IsSuccess || (confirmResult.Data != null && !string.IsNullOrEmpty(confirmResult.Data.error)))
        {
            _failedRequests++;
            throw new HttpRequestException($"Confirm transfer failed: {confirmResult.ErrorMessage}");
        }

        // Both create and confirm succeeded
        _successfulRequests++;
        await Task.Delay(1, context.CancellationToken);

        Console.WriteLine($"[TransferWorkload] [VU {_senderPhone}] ExecuteAsync END | ExecuteCount={context.ExecuteCount} | success={_successfulRequests} | failed={_failedRequests}");
    }

    public override Dictionary<string, string>? Complete(WorkloadContext context)
    {
        return new Dictionary<string, string>
        {
            { "SenderPhone", _senderPhone ?? "Unknown" },
            { "SuccessfulRequests", _successfulRequests.ToString() },
            { "FailedRequests", _failedRequests.ToString() },
            { "TokenCacheHits", _tokenCacheHits.ToString() },
            { "TokenCacheMisses", _tokenCacheMisses.ToString() },
            { "TotalExecutions", context.ExecuteCount.ToString() }
        };
    }

    public override async Task TeardownAsync(WorkloadContext context)
    {
        Console.WriteLine($"[TransferWorkload] [VU {_senderPhone}] Teardown started");
        // Cleanup if needed
        await Task.CompletedTask;
        Console.WriteLine($"[TransferWorkload] [VU {_senderPhone}] Teardown completed");
    }
}
