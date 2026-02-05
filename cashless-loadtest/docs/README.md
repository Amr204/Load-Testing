# SignupActors Load Test - توثيق المشروع

## نظرة عامة

تم تحويل مشروع CashlessLoadTest من سيناريو Transfer (الحوالات) إلى سيناريو SignupActors (تسجيل العملاء).

الميزة الرئيسية: **يقرأ تلقائيًا من ملفات Postman - لا حاجة لإعداد ENV يدوي!**

---

## هيكل الملفات الجديد

```
CashlessLoadTest.Worker/
├── Program.cs                              # نقطة الدخول
├── CashlessLoadTest.Worker.csproj          # ملف المشروع
├── Common/
│   ├── Config.cs                           # إعدادات HTTP الأساسية
│   ├── HttpHelper.cs                       # مساعد HTTP مع retry
│   └── Postman/
│       ├── PostmanEnvironmentLoader.cs     # قارئ environment
│       ├── PostmanCollectionLoader.cs      # قارئ collection
│       └── PostmanVariableResolver.cs      # محلل {{variables}}
└── Scenarios/
    └── SignupActors/
        ├── SignupActorsSettings.cs         # إعدادات السيناريو
        ├── SignupActorsDataGenerator.cs    # مولد البيانات
        ├── SignupActorsTokenProvider.cs    # مزود التوكن
        └── SignupActorsWorkload.cs         # الـ Workload الرئيسي
```

---

## شرح كل ملف

### 📁 Common/Postman/

#### `PostmanEnvironmentLoader.cs`
**الوظيفة:** يقرأ ملف Postman Environment JSON ويستخرج المتغيرات.

```csharp
var loader = PostmanEnvironmentLoader.Load("path/to/env.json");
var baseUrl = loader.GetValue("BaseApi");
var clientId = loader.GetValue("ClientId");
```

**يستخرج:**
- `BaseApi` - رابط API الأساسي
- `ClientId`, `ClientSecret` - بيانات OAuth
- `username`, `password` - بيانات الدخول
- `AppId`, `AppSecret` - بيانات HMAC
- `profileId` - معرف الملف الشخصي

---

#### `PostmanCollectionLoader.cs`
**الوظيفة:** يقرأ Postman Collection ويستخرج الـ requests.

```csharp
var loader = PostmanCollectionLoader.Load("path/to/collection.json");
var tokenRequest = loader.TokenRequest;    // طلب تسجيل الدخول
var registerRequest = loader.RegisterRequest; // طلب التسجيل
var verifyRequest = loader.VerifyRequest;  // طلب التحقق
```

**يستخرج من كل request:**
- `Method` - GET/POST
- `UrlPath` - المسار
- `RawBody` - الـ body template
- `Headers` - الرؤوس
- `FormData` - بيانات النموذج

---

#### `PostmanVariableResolver.cs`
**الوظيفة:** يستبدل `{{variable}}` بقيمها الفعلية.

```csharp
var resolver = new PostmanVariableResolver(envLoader);
resolver.SetVariable("Token", "abc123");
var url = resolver.Resolve("{{BaseApi}}/api/{{endpoint}}");
// النتيجة: https://api.example.com/api/users
```

---

### 📁 Scenarios/SignupActors/

#### `SignupActorsSettings.cs`
**الوظيفة:** يحمّل جميع الإعدادات تلقائيًا من Postman files.

```csharp
var settings = SignupActorsSettings.Load(baseUrlOverride, runIdOverride);
```

**يحتوي على:**
- `BaseUrl` - رابط API
- `ClientId`, `Username`, `Password` - بيانات الدخول
- `AppId`, `AppSecret` - بيانات HMAC
- `ProfileId` - معرف الملف الشخصي
- `OtpCode` - رمز التحقق (افتراضي: 004121)
- `RunId` - معرف التشغيل للتفرد
- `RegisterBodyTemplate` - قالب body التسجيل من Postman

**الأولوية:**
1. CLI arguments (الأعلى)
2. ENV variables
3. Postman files (الافتراضي)

---

#### `SignupActorsDataGenerator.cs`
**الوظيفة:** يولّد Mobile و UserName فريدين لكل عميل.

```csharp
var generator = new SignupActorsDataGenerator(settings);
var data = generator.Generate();
// data.Mobile = "712345678" (9 أرقام تبدأ بـ 7)
// data.UserName = "abcdef" (حروف فقط)
```

**خوارزمية التفرد:**
- `nodeCode` = hash(MachineName + RunId) % 100 → رقمين
- `counter` = Interlocked.Increment() % 1,000,000 → 6 أرقام
- **Mobile** = `7{nodeCode:2}{counter:6}` = 9 أرقام
- **UserName** = حروف عشوائية + base26(nodeCode) + base26(counter)

**الأسماء:** يختار عشوائيًا من قوائم تحتوي 100+ اسم.

---

#### `SignupActorsTokenProvider.cs`
**الوظيفة:** يحصل على access_token مع HMAC signature.

```csharp
var provider = new SignupActorsTokenProvider(httpClient, settings);
var token = await provider.GetTokenAsync(cancellationToken);
```

**HMAC Signature (مطابق لـ Postman):**
```
bodyRaw = ClientId + username + password
data = timestamp\nbodyRaw\nPOST\n/auth/connect/token\n
signature = HMAC-SHA256(data.toLowerCase(), AppSecret)
header = AppId:signature:timestamp:nonce
```

**الميزات:**
- تخزين مؤقت في الذاكرة
- إعادة المحاولة عند الفشل
- عد hits/misses

---

#### `SignupActorsWorkload.cs`
**الوظيفة:** الـ Workload الرئيسي - ينفذ Register + Verify.

```csharp
[Workload("SignupActors")]
public class SignupActorsWorkload : Workload
{
    // Constructor Injection - الطريقة الصحيحة لـ DFrame
    public SignupActorsWorkload(
        HttpClient httpClient,
        SignupActorsSettings settings,
        SignupActorsDataGenerator dataGenerator,
        SignupActorsTokenProvider tokenProvider)
}
```

**ExecuteAsync (لكل iteration):**
1. احصل على token
2. ولّد بيانات فريدة (Mobile, UserName, Names)
3. ابني body من Postman template
4. أرسل Register request
5. استخرج requestId + publicIdentifier
6. أرسل Verify request
7. سجّل النجاح/الفشل

**العدادات:**
- `created_ok` - تسجيل ناجح
- `verified_ok` - تحقق ناجح
- `duplicate_errors` - تكرار
- `auth_errors` - خطأ مصادقة
- `validation_errors` - خطأ تحقق
- `http_errors` - أخطاء أخرى

---

### 📄 Program.cs

**الوظيفة:** نقطة الدخول مع Dependency Injection.

```csharp
// تسجيل الخدمات
services.AddSingleton(httpClient);
services.AddSingleton(settings);
services.AddSingleton(new SignupActorsDataGenerator(settings));
services.AddSingleton(sp => new SignupActorsTokenProvider(...));
```

**CLI Arguments:**
| Argument | الوصف |
|----------|-------|
| `<controller>` | عنوان gRPC |
| `--vp <int>` | عدد VPs |
| `--url <string>` | تجاوز BaseUrl |
| `--run-id <string>` | معرف التشغيل |

---

### 📄 Common/Config.cs

**الوظيفة:** إعدادات HTTP الأساسية (للتوافق مع الكود القديم).

```csharp
Config.HttpTimeoutSeconds  // مهلة HTTP
Config.MaxRetries          // عدد المحاولات
Config.RetryDelayMs        // تأخير بين المحاولات
```

---

### 📄 Common/HttpHelper.cs

**الوظيفة:** إرسال HTTP requests مع retry logic.

```csharp
// JSON request
var result = await HttpHelper.SendRequestAsync<T>(
    httpClient, HttpMethod.Post, url, body, bearerToken, ct);

// Form-urlencoded request
var result = await HttpHelper.SendFormUrlEncodedAsync<T>(
    httpClient, url, formData, ct, customHeaders: hmacHeaders);
```

**الميزات:**
- Retry على 429/5xx
- Exponential backoff مع jitter
- استخراج أخطاء من response

---

## كيفية الاستخدام

### 1. تشغيل Controller
```powershell
cd CashlessLoadTest.Controller
dotnet run
```

### 2. تشغيل Worker
```powershell
cd CashlessLoadTest.Worker
dotnet run -- http://localhost:7313 --vp 5 --run-id TEST001
```

### 3. اختبار 5 مستخدمين
- افتح http://localhost:7312
- اختر SignupActors
- Mode: Request
- TotalRequest: 5
- Execute

---

## ملفات Postman المطلوبة

```
SignupActors/
├── *.postman_environment.json   # المتغيرات
├── *.postman_collection.json    # الـ requests
└── resopnses.json               # أمثلة الردود
```

تم إعداد `csproj` لنسخها تلقائيًا للـ output.
