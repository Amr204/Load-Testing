using System.Text.Json;
using System.Text.Json.Nodes;
using DFrame;
using CashlessLoadTest.Worker.Common;

namespace CashlessLoadTest.Worker.Scenarios.SignupActors;

/// <summary>
/// Register response from API.
/// </summary>
public class RegisterResponse
{
    public string publicIdentifier { get; set; } = "";
    public bool otpRequired { get; set; }
    public string requestId { get; set; } = "";
    public string? error { get; set; }
    public string? errorMessage { get; set; }
}

/// <summary>
/// Verify response from API.
/// </summary>
public class VerifyResponse
{
    public string id { get; set; } = "";
    public string publicIdentifier { get; set; } = "";
    public string? fullName { get; set; }
    public string? displayName { get; set; }
    public string? error { get; set; }
    public string? errorMessage { get; set; }
}

/// <summary>
/// SignupActors Workload - Register and Verify actors/customers.
/// Uses constructor injection for all dependencies.
/// </summary>
[Workload("SignupActors")]
public class SignupActorsWorkload : Workload
{
    private readonly HttpClient _httpClient;
    private readonly SignupActorsSettings _settings;
    private readonly SignupActorsDataGenerator _dataGenerator;
    private readonly SignupActorsTokenProvider _tokenProvider;

    // Counters
    private int _createdOk;
    private int _verifiedOk;
    private int _httpErrors;
    private int _authErrors;
    private int _validationErrors;
    private int _duplicateErrors;

    // Constructor injection - DFrame way
    public SignupActorsWorkload(
        HttpClient httpClient,
        SignupActorsSettings settings,
        SignupActorsDataGenerator dataGenerator,
        SignupActorsTokenProvider tokenProvider)
    {
        _httpClient = httpClient;
        _settings = settings;
        _dataGenerator = dataGenerator;
        _tokenProvider = tokenProvider;
    }

    public override async Task SetupAsync(WorkloadContext context)
    {
        // Validate required settings
        if (string.IsNullOrEmpty(_settings.ProfileId))
        {
            Console.WriteLine("[SignupActors] WARNING: ProfileId not set. Registration will fail.");
        }

        // Pre-fetch token
        var token = await _tokenProvider.GetTokenAsync(context.CancellationToken);
        if (string.IsNullOrEmpty(token))
        {
            Console.WriteLine("[SignupActors] WARNING: Failed to obtain auth token in setup.");
        }
        else
        {
            Console.WriteLine("[SignupActors] Setup complete. Worker ready.");
        }
    }

    public override async Task ExecuteAsync(WorkloadContext context)
    {
        var cancellationToken = context.CancellationToken;

        // Step 1: Get token
        var token = await _tokenProvider.GetTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(token))
        {
            Interlocked.Increment(ref _authErrors);
            return;
        }

        // Step 2: Generate unique data with workload index for VU uniqueness
        var actorData = _dataGenerator.Generate(context.WorkloadIndex);

        // Step 3: Build register body from Postman template
        var registerBody = BuildRegisterBody(actorData);
        if (registerBody == null)
        {
            Interlocked.Increment(ref _validationErrors);
            Console.WriteLine("[SignupActors] Failed to build register body");
            return;
        }

        // Step 4: Send register request
        var registerUrl = $"{_settings.BaseUrl}/api/actors/registration/register/{_settings.ProfileId}";
        var registerResult = await HttpHelper.SendRequestAsync<RegisterResponse>(
            _httpClient,
            HttpMethod.Post,
            registerUrl,
            registerBody,
            token,
            cancellationToken,
            maxRetries: 2,
            retryDelayMs: 1000
        );

        if (!registerResult.IsSuccess || registerResult.Data == null)
        {
            HandleError(registerResult.StatusCode, registerResult.ResponseBody);
            Console.WriteLine($"[SignupActors] Register failed for {actorData.Mobile}: {registerResult.ErrorMessage}");
            return;
        }

        Interlocked.Increment(ref _createdOk);
        var registerData = registerResult.Data;

        // Step 5: Send verify request
        var verifyBody = new
        {
            requestId = registerData.requestId,
            publicIdentifier = registerData.publicIdentifier,
            code = _settings.OtpCode
        };

        var verifyUrl = $"{_settings.BaseUrl}/api/actors/registration/verify";
        var verifyResult = await HttpHelper.SendRequestAsync<VerifyResponse>(
            _httpClient,
            HttpMethod.Post,
            verifyUrl,
            verifyBody,
            token,
            cancellationToken,
            maxRetries: 2,
            retryDelayMs: 1000
        );

        if (!verifyResult.IsSuccess || verifyResult.Data == null)
        {
            HandleError(verifyResult.StatusCode, verifyResult.ResponseBody);
            Console.WriteLine($"[SignupActors] Verify failed for {actorData.Mobile}: {verifyResult.ErrorMessage}");
            return;
        }

        Interlocked.Increment(ref _verifiedOk);
        Console.WriteLine($"[SignupActors] SUCCESS: {actorData.Mobile} -> ID: {verifyResult.Data.id}");
    }

    /// <summary>
    /// Builds register body by cloning Postman template and modifying Mobile/UserName/Names.
    /// </summary>
    private object? BuildRegisterBody(GeneratedActorData actorData)
    {
        if (_settings.RegisterBodyTemplate == null)
        {
            // Fallback: use hardcoded structure matching Postman EXACTLY
            return new
            {
                individual = new
                {
                    Mobile = actorData.Mobile,
                    FirstName = actorData.FirstName,
                    SecondName = actorData.SecondName,
                    ThirdName = actorData.ThirdName,
                    LastName = actorData.LastName,
                    Gender = "M",
                    CountryDistrict = "YE-SA-SanaaAlqdimah",
                    Country = "YEM",
                    CountrySubdivision = "YE-SA",
                    UserName = actorData.UserName,
                    extraProperties = new Dictionary<string, object?>
                    {
                        ["NationalNo"] = "",
                        ["ِActorNumber"] = null,  // Note: has Arabic kasra before 'A'
                        ["decimalActor"] = null,
                        ["Educationlevel"] = "2",
                        ["IdentityTypes"] = "1",
                        ["CreationDate"] = null,
                        ["boolActor"] = "true"
                    }
                }
            };
        }

        try
        {
            // Clone the template
            var cloned = JsonNode.Parse(_settings.RegisterBodyTemplate.ToJsonString());
            if (cloned == null) return null;

            // Modify individual fields
            var individual = cloned["individual"];
            if (individual != null)
            {
                // Try various field names for mobile
                TrySetValue(individual, "Mobile", actorData.Mobile);
                TrySetValue(individual, "mobile", actorData.Mobile);
                
                // UserName
                TrySetValue(individual, "UserName", actorData.UserName);
                TrySetValue(individual, "userName", actorData.UserName);

                // Names
                TrySetValue(individual, "FirstName", actorData.FirstName);
                TrySetValue(individual, "firstName", actorData.FirstName);
                TrySetValue(individual, "SecondName", actorData.SecondName);
                TrySetValue(individual, "secondName", actorData.SecondName);
                TrySetValue(individual, "fatherName", actorData.SecondName);
                TrySetValue(individual, "ThirdName", actorData.ThirdName);
                TrySetValue(individual, "thirdName", actorData.ThirdName);
                TrySetValue(individual, "grandFatherName", actorData.ThirdName);
                TrySetValue(individual, "LastName", actorData.LastName);
                TrySetValue(individual, "lastName", actorData.LastName);
                TrySetValue(individual, "surName", actorData.LastName);
            }

            // Return as deserialized object for proper JSON serialization
            return JsonSerializer.Deserialize<object>(cloned.ToJsonString());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SignupActors] Error building body from template: {ex.Message}");
            return null;
        }
    }

    private static void TrySetValue(JsonNode node, string key, string value)
    {
        if (node is JsonObject obj && obj.ContainsKey(key))
        {
            obj[key] = value;
        }
    }

    private void HandleError(int statusCode, string? responseBody)
    {
        if (statusCode == 401 || statusCode == 403)
        {
            Interlocked.Increment(ref _authErrors);
        }
        else if (statusCode == 400)
        {
            Interlocked.Increment(ref _validationErrors);
        }
        else if (statusCode == 409 || 
                 (responseBody?.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true) ||
                 (responseBody?.Contains("already exists", StringComparison.OrdinalIgnoreCase) == true))
        {
            Interlocked.Increment(ref _duplicateErrors);
        }
        else
        {
            Interlocked.Increment(ref _httpErrors);
        }
    }

    public override Dictionary<string, string> Complete(WorkloadContext context)
    {
        return new Dictionary<string, string>
        {
            ["created_ok"] = _createdOk.ToString(),
            ["verified_ok"] = _verifiedOk.ToString(),
            ["http_errors"] = _httpErrors.ToString(),
            ["auth_errors"] = _authErrors.ToString(),
            ["validation_errors"] = _validationErrors.ToString(),
            ["duplicate_errors"] = _duplicateErrors.ToString(),
            ["token_cache_hits"] = _tokenProvider.CacheHits.ToString(),
            ["token_cache_misses"] = _tokenProvider.CacheMisses.ToString(),
            ["total_generated"] = SignupActorsDataGenerator.GetCounter().ToString()
        };
    }

    public override Task TeardownAsync(WorkloadContext context)
    {
        Console.WriteLine($"[SignupActors] Teardown - Created: {_createdOk}, Verified: {_verifiedOk}, Errors: HTTP={_httpErrors}, Auth={_authErrors}, Validation={_validationErrors}, Duplicate={_duplicateErrors}");
        return Task.CompletedTask;
    }
}
