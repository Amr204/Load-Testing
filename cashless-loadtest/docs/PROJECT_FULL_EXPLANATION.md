# CashlessLoadTest - شرح المشروع الكامل

## 📌 المقدمة والهدف

هذا المشروع هو **أداة اختبار حِمل (Load Testing)** مبنية على إطار عمل **DFrame** لاختبار نظام الحوالات المالية (Cashless Transfers). المشروع يتكون من جزئين رئيسيين:

1. **Controller**: الخادم المركزي الذي يدير الاختبارات ويعرض واجهة الويب
2. **Worker**: العامل الذي ينفذ الطلبات الفعلية على النظام المستهدف

> [!IMPORTANT]
> هذا المشروع **لا ينشئ مستخدمين جدد**. هو يستخدم قائمة مستخدمين محددة مسبقاً في الكود لتنفيذ اختبارات الحوالات.

---

## 📁 هيكلية المشروع (Project Tree)

```
CashlessLoadTest/
├── CashlessLoadTest.sln                         # Solution file
│
├── CashlessLoadTest.Controller/                 # مشروع الـ Controller
│   ├── CashlessLoadTest.Controller.csproj       # .NET 8.0 + DFrame.Controller 1.2.2
│   ├── Program.cs                               # نقطة البداية للـ Controller
│   ├── FlatFileLogExecutionResultHistoryProvider.cs  # حفظ نتائج الاختبار في ملفات JSON
│   ├── appsettings.json                         # إعدادات المنافذ (7312 UI + 7313 gRPC)
│   ├── Properties/
│   │   └── launchSettings.json
│   └── logs/                                    # مجلد ملفات النتائج (يُنشأ تلقائياً)
│
└── CashlessLoadTest.Worker/                     # مشروع الـ Worker
    ├── CashlessLoadTest.Worker.csproj           # .NET 8.0 + DFrame.Worker 1.2.2
    ├── Program.cs                               # نقطة البداية للـ Worker + HttpClient setup
    ├── Config.cs                                # الإعدادات الثابتة (Users, Timeouts, Dirs)
    ├── HttpHelper.cs                            # مساعد إرسال طلبات HTTP مع Retry
    ├── Models.cs                                # نماذج الـ Request/Response
    ├── BaseWorkload.cs                          # الـ Workload الأساسي (Login + Token)
    ├── TransferWorkload.cs                      # سيناريو Create + Confirm في دورة واحدة
    ├── CreateTransferWorkload.cs                # سيناريو Create فقط
    ├── ConfirmTransferWorkload.cs               # سيناريو Confirm فقط
    ├── TokenCache.cs                            # كاش التوكنات (ملفات JSON)
    ├── TransferStore.cs                         # مخزن الحوالات (ملفات JSON + Claim)
    ├── token-cache/                             # مجلد ملفات التوكن (يُنشأ تلقائياً)
    └── transfer-store/                          # مجلد ملفات الحوالات (يُنشأ تلقائياً)
```

---

## 🏗️ المعمارية: Controller vs Worker

### ما هو DFrame؟

**DFrame** هو إطار عمل لاختبار الحِمل مبني على .NET، يعمل بنظام **Master-Worker**:
- الـ **Controller** (Master) يوزع المهام ويجمع النتائج
- الـ **Workers** تنفذ الطلبات الفعلية على النظام المستهدف

### كيف يتم الاتصال؟

```
┌─────────────────────────────────────────────────────────────────┐
│                         Controller                               │
│  ┌─────────────────────┐     ┌──────────────────────────────┐   │
│  │   Web UI (HTTP/1.1) │     │      gRPC Server (HTTP/2)    │   │
│  │   Port: 7312        │     │      Port: 7313              │   │
│  │   للمستخدم           │     │      للـ Workers             │   │
│  └─────────────────────┘     └──────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                                        │
                                        │ gRPC (HTTP/2)
                                        ▼
        ┌───────────────┐    ┌───────────────┐    ┌───────────────┐
        │   Worker 1    │    │   Worker 2    │    │   Worker N    │
        │ (VirtualProc) │    │ (VirtualProc) │    │ (VirtualProc) │
        └───────────────┘    └───────────────┘    └───────────────┘
                │                    │                    │
                └────────────────────┴────────────────────┘
                                     │
                                     ▼
                          ┌─────────────────────┐
                          │   Target System     │
                          │   (mada.com:2401)   │
                          └─────────────────────┘
```

### إعدادات المنافذ (appsettings.json)

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:7312",
        "Protocols": "Http1"          // ← Web UI للمتصفح
      },
      "Grpc": {
        "Url": "http://0.0.0.0:7313",
        "Protocols": "Http2"          // ← اتصال الـ Workers
      }
    }
  }
}
```

| المنفذ | البروتوكول | الاستخدام |
|--------|-----------|----------|
| **7312** | HTTP/1.1 | واجهة الويب للتحكم في الاختبارات ومشاهدة النتائج |
| **7313** | HTTP/2 (gRPC) | اتصال الـ Workers بالـ Controller لاستقبال المهام وإرسال النتائج |

> [!NOTE]
> **HTTP/1.1** يستخدم للمتصفحات العادية، بينما **HTTP/2** ضروري لـ gRPC لأنه يدعم multiplexing و bidirectional streaming.

---

## ⚙️ دورة حياة الـ Workload (Lifecycle)

كل **Workload** في DFrame يمر بأربع مراحل:

```
┌──────────────────────────────────────────────────────────────────┐
│                     Workload Lifecycle                            │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│   1. SetupAsync()      → تهيئة أولية (مرة واحدة)                 │
│         │                 - تحديد المستخدم                       │
│         │                 - تسجيل الدخول الأولي                  │
│         ▼                                                        │
│   2. ExecuteAsync()    → التنفيذ الفعلي (يتكرر N مرات)           │
│         │                 - إرسال الطلبات                        │
│         │                 - قياس الأداء                          │
│         ▼                                                        │
│   3. Complete()        → جمع المقاييس (مرة واحدة)                │
│         │                 - إرجاع الإحصائيات                     │
│         ▼                                                        │
│   4. TeardownAsync()   → التنظيف (مرة واحدة)                     │
│                           - حفظ البيانات                         │
│                           - تحرير الموارد                        │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

### كيف مطبقة في المشروع؟

#### في `BaseWorkload.cs`:
```csharp
public abstract class BaseWorkload : Workload
{
    protected readonly HttpClient _httpClient;
    protected string? _token;
    protected string? _senderPhone;
    protected DateTime _tokenExpiresAt;
    
    // Metrics tracking
    protected int _tokenCacheHits = 0;
    protected int _tokenCacheMisses = 0;
    protected int _successfulRequests = 0;
    protected int _failedRequests = 0;
    
    // التحقق من صلاحية التوكن وتجديده إذا لزم
    protected async Task EnsureValidTokenAsync(CancellationToken cancellationToken)
    
    // تسجيل الدخول (form-url-encoded)
    protected async Task<string?> LoginAsync(string phoneNumber, CancellationToken cancellationToken)
    
    // اختيار مستلم مختلف عن المرسل
    protected string PickReceiverDifferentFrom(string senderPhone)
}
```

---

## 💸 سيناريوهات اختبار الحوالات المالية

### السيناريو 1: `TransferWorkload` (Create + Confirm في دورة واحدة)

**الملف:** `TransferWorkload.cs`

```
┌────────────────────────────────────────────────────────────────┐
│                    TransferWorkload Flow                        │
├────────────────────────────────────────────────────────────────┤
│                                                                │
│  SetupAsync:                                                   │
│    1. تحديد _senderPhone من Config.Users[WorkloadIndex % N]   │
│    2. EnsureValidTokenAsync() → تحميل أو إنشاء توكن           │
│                                                                │
│  ExecuteAsync (يتكرر):                                         │
│    1. EnsureValidTokenAsync() → تأكد التوكن صالح              │
│    2. PickReceiverDifferentFrom() → اختيار مستلم عشوائي       │
│    3. POST /api/wallet/demostictransfer (CreateTransfer)      │
│       ├─ إذا فشل → throw + _failedRequests++                  │
│       └─ إذا نجح → احتفظ بـ transferId                        │
│    4. Task.Delay(1ms)                                         │
│    5. POST /api/wallet/demostictransfer/confirm               │
│       ├─ إذا فشل → throw + _failedRequests++                  │
│       └─ إذا نجح → _successfulRequests++                      │
│                                                                │
│  Complete:                                                     │
│    → إرجاع Dictionary يحتوي:                                  │
│       SenderPhone, SuccessfulRequests, FailedRequests,        │
│       TokenCacheHits, TokenCacheMisses, TotalExecutions       │
│                                                                │
│  TeardownAsync:                                                │
│    → لا شيء (التنظيف غير مطلوب)                               │
│                                                                │
└────────────────────────────────────────────────────────────────┘
```

### السيناريو 2: `CreateTransferWorkload` (Create فقط)

**الملف:** `CreateTransferWorkload.cs`

يختلف عن الأول في أنه:
- ينفذ **Create فقط** بدون Confirm
- يحفظ `transferId` في **TeardownAsync** عبر `TransferStore.SaveTransfer()`

```csharp
public override async Task TeardownAsync(WorkloadContext context)
{
    if (!string.IsNullOrEmpty(_lastTransferId) && !string.IsNullOrEmpty(_lastReceiverPhone))
    {
        TransferStore.SaveTransfer(_lastTransferId, _senderPhone!, _lastReceiverPhone);
    }
}
```

### السيناريو 3: `ConfirmTransferWorkload` (Confirm فقط)

**الملف:** `ConfirmTransferWorkload.cs`

يعتمد على الحوالات المحفوظة من `CreateTransferWorkload`:

```
┌────────────────────────────────────────────────────────────────┐
│                  ConfirmTransferWorkload Flow                   │
├────────────────────────────────────────────────────────────────┤
│                                                                │
│  SetupAsync:                                                   │
│    1. تحديد _senderPhone                                      │
│    2. EnsureValidTokenAsync()                                  │
│    3. إذا TransferId فارغ:                                    │
│       → ClaimPendingTransfer(_senderPhone, ReceiverPhone)     │
│       → أو ClaimPendingTransfer(_senderPhone) فقط            │
│    4. إذا لم يُوجد transferId → throw Exception              │
│                                                                │
│  ExecuteAsync:                                                 │
│    1. POST /api/wallet/demostictransfer/confirm               │
│    2. إذا فشل:                                                │
│       → ReleaseClaim(TransferId) لإعادة المحاولة لاحقاً       │
│       → throw Exception                                       │
│                                                                │
│  TeardownAsync:                                                │
│    → MarkAsConfirmed(TransferId) لتجنب تكرار التأكيد          │
│                                                                │
└────────────────────────────────────────────────────────────────┘
```

---

## 🛠️ شرح HttpHelper

**الملف:** `HttpHelper.cs`

### الدالة `SendRequestAsync<T>` (للطلبات JSON)

```csharp
public static async Task<HttpResponseResult<T>> SendRequestAsync<T>(
    HttpClient httpClient,
    HttpMethod method,
    string url,
    object? requestBody = null,
    string? bearerToken = null,
    CancellationToken cancellationToken = default,
    int maxRetries = 3,
    int retryDelayMs = 1000)
```

**السلوك:**
1. إنشاء `HttpRequestMessage` مع Bearer token إذا وُجد
2. تحويل `requestBody` إلى JSON
3. محاولة الإرسال مع **Retry Logic**:
   - إعادة المحاولة عند **5xx** (Server Error)
   - إعادة المحاولة عند **429** (Too Many Requests)
   - إعادة المحاولة عند **Network Errors** أو **Timeout**
   - **Exponential Backoff**: `retryDelayMs * (attempt + 1)`
4. Parse الـ Response إلى النوع `T`

### الدالة `SendFormUrlEncodedAsync<T>` (لتسجيل الدخول)

```csharp
public static async Task<HttpResponseResult<T>> SendFormUrlEncodedAsync<T>(
    HttpClient httpClient,
    string url,
    IEnumerable<KeyValuePair<string, string>> formData,
    CancellationToken cancellationToken = default,
    int maxRetries = 3,
    int retryDelayMs = 1000,
    Dictionary<string, string>? customHeaders = null)
```

**الفرق:**
- يرسل البيانات كـ `application/x-www-form-urlencoded` (مطلوب لـ OAuth2)
- يدعم Custom Headers (مثل `x-device-id`, `x-vcp-loc`)

---

## 🔐 شرح TokenCache

**الملف:** `TokenCache.cs`

### لماذا موجود؟

في اختبار الحِمل، إذا كل Worker يسوي Login جديد كل مرة:
1. **Login Storm**: آلاف الطلبات على `/auth/connect/token` في نفس الوقت
2. **Rate Limiting**: السيرفر يرفض الطلبات (429)
3. **بطء الاختبار**: كل Execute يضيف 200-500ms للـ Login

### كيف يعمل؟

```
┌────────────────────────────────────────────────────────────────┐
│                       TokenCache Flow                           │
├────────────────────────────────────────────────────────────────┤
│                                                                │
│  EnsureValidTokenAsync():                                      │
│    │                                                           │
│    ├─ إذا التوكن موجود وصالح → استخدمه (لا شيء)               │
│    │                                                           │
│    └─ إذا التوكن منتهي أو غير موجود:                          │
│         │                                                      │
│         ├─ TokenCache.LoadToken(phoneNumber)                   │
│         │    ├─ قراءة من token-cache/token_{phone}.json       │
│         │    ├─ إذا الملف موجود والتوكن صالح → إرجاعه        │
│         │    └─ إذا منتهي → حذف الملف وإرجاع null            │
│         │                                                      │
│         ├─ إذا وُجد في الكاش → _tokenCacheHits++              │
│         │                                                      │
│         └─ إذا لم يُوجد:                                      │
│              → _tokenCacheMisses++                             │
│              → LoginAsync() للحصول على توكن جديد              │
│              → TokenCache.SaveToken() لحفظه                   │
│                                                                │
└────────────────────────────────────────────────────────────────┘
```

### الكتابة الذرية (Atomic Write)

```csharp
public static void SaveToken(string phoneNumber, string token, DateTime expiresAt)
{
    var filePath = GetCacheFilePath(phoneNumber);
    var json = JsonSerializer.Serialize(entry, JsonOptions);
    
    // Write atomically using temp file
    var tempPath = filePath + ".tmp";
    File.WriteAllText(tempPath, json);
    File.Move(tempPath, filePath, overwrite: true);  // ← Atomic!
}
```

> [!NOTE]
> الكتابة الذرية تمنع corruption إذا تعطل البرنامج أثناء الكتابة.

---

## 📦 شرح TransferStore

**الملف:** `TransferStore.cs`

### الهدف

عندما نفصل Create عن Confirm (سيناريو 2 و 3)، نحتاج مكان لحفظ `transferId` بين العمليتين.

### بنية `TransferStoreEntry`

```csharp
public class TransferStoreEntry
{
    public string TransferId { get; set; }
    public string SenderPhone { get; set; }
    public string ReceiverPhone { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool Confirmed { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    
    // Claiming mechanism
    public bool Claimed { get; set; }
    public string? ClaimedByWorker { get; set; }
    public DateTime? ClaimedAt { get; set; }
}
```

### آلية Claim/Release

```
┌─────────────────────────────────────────────────────────────────┐
│                    TransferStore Claiming                        │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  Worker A يريد تأكيد حوالة:                                    │
│    │                                                            │
│    ├─ ClaimPendingTransfer(senderPhone)                        │
│    │    ├─ RefreshCacheIfNeeded() → تحميل الملفات كل 30 ثانية  │
│    │    ├─ البحث عن أول حوالة: Confirmed=false AND Claimed=false│
│    │    ├─ تعيين: Claimed=true, ClaimedByWorker=WorkerId       │
│    │    └─ حفظ في الملف (atomic write)                        │
│    │                                                            │
│    ├─ إذا نجح التأكيد:                                         │
│    │    → MarkAsConfirmed(transferId)                          │
│    │    → Confirmed=true, Claimed=false                        │
│    │                                                            │
│    └─ إذا فشل التأكيد:                                         │
│         → ReleaseClaim(transferId)                             │
│         → Claimed=false (يسمح لـ Worker آخر يحاول)             │
│                                                                 │
│  Stale Claim Timeout (5 دقائق):                                │
│    → إذا Worker A تعطل بدون Release                           │
│    → RefreshCacheIfNeeded() تحرر الـ Claims القديمة تلقائياً    │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### الفهارس الداخلية

```csharp
// كاش في الذاكرة (مفتاح: transferId)
private static readonly Dictionary<string, TransferStoreEntry> _cache = new();

// فهرس بـ "sender:receiver" للبحث السريع
private static readonly Dictionary<string, List<string>> _senderReceiverIndex = new();

// فهرس بـ sender فقط
private static readonly Dictionary<string, List<string>> _senderIndex = new();
```

---

## 👥 مصدر بيانات المستخدمين

### أين بيانات المستخدمين؟

**الملف:** `Config.cs`

```csharp
public static class Config
{
    public static readonly string[] Users = new[]
    {
        "776134932", "777462906", "773627506", "777764010", "773909112", "777319144",
        "770014159", "735655831", "730082713", "771511630", "776789028", "773859992",
        // ... وباقي الأرقام
    };
    
    public static string CommonPin { get; } = "[REDACTED]";
}
```

### هل المشروع ينشئ مستخدمين؟

> [!IMPORTANT]
> **لا!** المشروع الحالي **لا يحتوي على أي كود لإنشاء مستخدمين جدد**.
> 
> هو يفترض أن المستخدمين **موجودين مسبقاً** في النظام المستهدف بنفس الـ PIN.

### كيف يعمل اختيار المستخدم؟

```csharp
// في SetupAsync لكل Workload:
_senderPhone = Config.Users[context.WorkloadIndex % Config.Users.Length];
```

- كل Worker يأخذ مستخدم من القائمة بناءً على رقمه
- إذا عندك 42 مستخدم و 100 Worker → المستخدمين يتكررون

### كيف أضيف مستخدم جديد؟

#### الطريقة 1: إضافة مباشرة في الكود

```csharp
// في Config.cs
public static readonly string[] Users = new[]
{
    "776134932", "777462906", 
    "712345678",  // ← المستخدم الجديد
    // ...
};
```

ثم أعد بناء المشروع:
```bash
dotnet build
```

#### الطريقة 2: تحميل من متغير بيئة (مقترح)

عدّل `Config.cs`:

```csharp
public static readonly string[] Users = LoadUsersFromEnv();

private static string[] LoadUsersFromEnv()
{
    var envUsers = Environment.GetEnvironmentVariable("LOAD_TEST_USERS");
    if (!string.IsNullOrEmpty(envUsers))
    {
        return envUsers.Split(',', StringSplitOptions.RemoveEmptyEntries);
    }
    
    // Default fallback
    return new[] { "776134932", "777462906", /* ... */ };
}
```

ثم شغّل:
```bash
set LOAD_TEST_USERS=712345678,712345679,712345680
dotnet run
```

#### الطريقة 3: تحميل من ملف

```csharp
public static readonly string[] Users = LoadUsersFromFile();

private static string[] LoadUsersFromFile()
{
    var filePath = Environment.GetEnvironmentVariable("USERS_FILE") ?? "users.txt";
    if (File.Exists(filePath))
    {
        return File.ReadAllLines(filePath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
    }
    return new[] { /* default users */ };
}
```

---

## 📤 تصدير الـ Worker كـ EXE (Publish)

### الفرق بين Self-Contained و Framework-Dependent

| النوع | الحجم | يحتاج .NET Runtime؟ | الاستخدام |
|-------|-------|-------------------|----------|
| **Framework-Dependent** | صغير (~1-5 MB) | نعم | جميع الأجهزة عليها .NET 8 |
| **Self-Contained** | كبير (~80-150 MB) | لا | أجهزة بدون .NET مثبت |

### أمر Publish للـ Framework-Dependent

```powershell
cd CashlessLoadTest.Worker

dotnet publish -c Release -r win-x64 --self-contained false -o ./publish
```

**الناتج:**
```
publish/
├── CashlessLoadTest.Worker.exe          # الملف التنفيذي
├── CashlessLoadTest.Worker.dll
├── CashlessLoadTest.Worker.deps.json
├── CashlessLoadTest.Worker.runtimeconfig.json
└── DFrame.Worker.dll
```

### أمر Publish للـ Self-Contained

```powershell
cd CashlessLoadTest.Worker

dotnet publish -c Release -r win-x64 --self-contained true -o ./publish-self-contained
```

**الناتج:** نفس الملفات + جميع ملفات .NET Runtime (~150 MB)

### أمر Publish مع Single File (ملف واحد)

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish-single
```

**الناتج:** ملف `CashlessLoadTest.Worker.exe` واحد فقط!

---

## 🚀 تشغيل الـ Worker على جهاز آخر

### الأمر الكامل

```powershell
.\CashlessLoadTest.Worker.exe http://192.168.10.14:7313 --vp 10 --url https://mada21.com:2401
```

### شرح كل جزء

| الجزء | الشرح |
|-------|-------|
| `.\CashlessLoadTest.Worker.exe` | تشغيل الملف التنفيذي |
| `http://192.168.10.14:7313` | عنوان Controller (منفذ gRPC!) - أول argument غير flag |
| `--vp 10` | VirtualProcess = 10 (عدد العمليات المتوازية لهذا Worker) |
| `--url https://mada21.com:2401` | عنوان النظام المستهدف (الـ API الفعلي) |

### كيف يقرأها الكود؟

في `Program.cs`:

```csharp
// قراءة Controller Address (أول argument غير flag)
if (!args[i].StartsWith("--") && string.IsNullOrEmpty(controllerAddress))
{
    controllerAddress = args[i];  // ← http://192.168.10.14:7313
}

// قراءة --vp أو --virtual-process
if ((args[i].Equals("--virtual-process", StringComparison.OrdinalIgnoreCase) ||
     args[i].Equals("--vp", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
{
    virtualProcess = int.Parse(args[i + 1]);  // ← 10
}

// قراءة --url أو --base-url
if ((args[i].Equals("--base-url", StringComparison.OrdinalIgnoreCase) ||
     args[i].Equals("--url", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
{
    baseUrl = args[i + 1];  // ← https://mada21.com:2401
}
```

> [!WARNING]
> **انتبه:** الكود يستخدم `--url` وليس `-url` (شرطتين وليس شرطة واحدة).

---

## 🖥️ خطوات التشغيل

### 1. التشغيل المحلي (جهاز واحد)

**Terminal 1 - Controller:**
```powershell
cd CashlessLoadTest.Controller
dotnet run
```

الخرج:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://0.0.0.0:7312
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://0.0.0.0:7313
```

**Terminal 2 - Worker:**
```powershell
cd CashlessLoadTest.Worker
dotnet run -- http://localhost:7313 --vp 5 --url https://mada.com:2401
```

**المتصفح:**
```
http://localhost:7312
```

### 2. التشغيل الموزع (جهازين أو أكثر)

**على جهاز Controller (IP: 192.168.10.14):**
```powershell
cd CashlessLoadTest.Controller
dotnet run
```

**على جهاز Worker 1:**
```powershell
.\CashlessLoadTest.Worker.exe http://192.168.10.14:7313 --vp 50 --url https://mada.com:2401
```

**على جهاز Worker 2:**
```powershell
.\CashlessLoadTest.Worker.exe http://192.168.10.14:7313 --vp 50 --url https://mada.com:2401
```

الآن عندك **100 VirtualProcess** موزعة على جهازين!

---

## 🔧 Troubleshooting (حل المشاكل)

### 1. Worker لا يتصل بـ Controller

**المشكلة:**
```
Grpc.Core.RpcException: Status(StatusCode="Unavailable")
```

**الحلول:**
- ✅ تأكد الـ Firewall يسمح بالمنفذ **7313** على جهاز Controller
- ✅ تأكد أنك تستخدم IP الصحيح (ليس localhost إذا من جهاز آخر)
- ✅ تأكد أن Controller شغال

```powershell
# على جهاز Controller - افتح المنفذ
netsh advfirewall firewall add rule name="DFrame gRPC" dir=in action=allow protocol=tcp localport=7313
```

### 2. خطأ --url vs -url

**المشكلة:**
```
Unknown argument: -url
```

**الحل:** استخدم `--url` (شرطتين):
```powershell
.\CashlessLoadTest.Worker.exe http://192.168.10.14:7313 --url https://mada.com:2401
```

### 3. خطأ HTTP/2 أو gRPC

**المشكلة:**
```
The server does not support HTTP/2
```

**الحل:** تأكد أنك تتصل بالمنفذ **7313** (gRPC) وليس **7312** (Web UI).

### 4. Login يفشل بـ 409 Conflict

**السبب:** المستخدم مسجل دخول من مكان آخر أو الـ session قديم.

**الحل:** الكود يعيد المحاولة تلقائياً (حتى 3 مرات):
```csharp
// في BaseWorkload.LoginAsync()
for (int attempt = 0; attempt <= Config.LoginMaxRetries; attempt++)
{
    // ...
    if (!result.IsSuccess && result.StatusCode == 409)
    {
        continue; // Retry
    }
}
```

### 5. الملفات مقفولة (File Lock)

**المشكلة:** `The process cannot access the file because it is being used`.

**السبب:** Workers متعددين يحاولون الكتابة لنفس الملف.

**الحل:** الكود يستخدم:
- `lock (_lock)` للتزامن داخل العملية الواحدة
- Atomic write (`File.Move` overwrite) للتعامل مع الـ race conditions

---

## 📊 نتائج الاختبار

بعد انتهاء الاختبار، النتائج تُحفظ في:

```
CashlessLoadTest.Controller/logs/
├── 2024-01-15 14.30.00 TransferWorkload abc123.json
├── 2024-01-15 14.35.00 CreateTransferWorkload def456.json
└── ...
```

كل ملف يحتوي:
```json
{
  "summary": {
    "ExecutionId": "abc123",
    "Workload": "TransferWorkload",
    "StartTime": "2024-01-15T14:30:00Z",
    "CompleteTime": "2024-01-15T14:35:00Z",
    "WorkerCount": 2,
    "TotalRequest": 1000
  },
  "results": [
    {
      "WorkloadName": "TransferWorkload",
      "Results": {
        "SenderPhone": "776134932",
        "SuccessfulRequests": "500",
        "FailedRequests": "0"
      }
    }
  ]
}
```

---

## 📝 ملخص سريع

| المكون | الوظيفة |
|--------|---------|
| **Controller** | يدير الاختبار + Web UI على 7312 + gRPC على 7313 |
| **Worker** | ينفذ الطلبات + يتصل بـ Controller عبر gRPC |
| **TransferWorkload** | Create + Confirm في نفس Execute |
| **CreateTransferWorkload** | Create فقط + حفظ في TransferStore |
| **ConfirmTransferWorkload** | Confirm فقط + قراءة من TransferStore |
| **TokenCache** | تقليل Login Storm عبر حفظ التوكنات |
| **TransferStore** | ربط Create و Confirm مع آلية Claim |
| **Config.Users** | **قائمة ثابتة من المستخدمين الموجودين مسبقاً** |

---

*آخر تحديث: 2026-02-04*
