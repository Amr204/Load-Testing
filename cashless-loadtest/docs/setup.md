# CashlessLoadTest Setup Guide

## Quick Start (No ENV Setup Required!)

The worker **automatically reads** settings from Postman files in `SignupActors/` folder:
- `*.postman_environment.json` - credentials, base URL
- `*.postman_collection.json` - request templates

### 1. Run Controller

```powershell
cd CashlessLoadTest.Controller
dotnet run
```
- Web UI: http://localhost:7312
- gRPC: http://localhost:7313

### 2. Run Worker

```powershell
cd CashlessLoadTest.Worker
dotnet run -- http://localhost:7313 --vp 5
```

---

## CLI Arguments

| Argument | Description | Example |
|----------|-------------|---------|
| `<controller>` | gRPC Controller address | `http://192.168.10.14:7313` |
| `--vp <int>` | Virtual Processes per worker | `--vp 10` |
| `--url <string>` | Override Base URL | `--url https://mada21.com:2401` |
| `--run-id <string>` | Unique run identifier | `--run-id LT_TEST_01` |

**Full Example:**
```powershell
.\CashlessLoadTest.Worker.exe http://192.168.10.14:7313 --vp 10 --url https://mada21.com:2401 --run-id LT_TEST_01
```

---

## Test 5 Users

1. Open Controller UI: `http://localhost:7312`
2. Select **SignupActors** workload
3. Configure:
   - Mode: `Request`
   - TotalRequest: `5`
   - Concurrency: `1`
4. Click **Execute**

### Expected Results
- `created_ok`: 5
- `verified_ok`: 5
- `duplicate_errors`: 0

---

## Publishing to EXE

### Self-Contained (recommended)
```powershell
cd CashlessLoadTest.Worker
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

The `SignupActors/*.json` files are automatically copied to output.

### Controller
```powershell
cd CashlessLoadTest.Controller
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

---

## Data Uniqueness

Mobile and UserName are guaranteed unique using:
- **NodeCode** = hash(MachineName + RunId) % 100
- **Counter** = atomic increment per worker

**Mobile format:** `7{nodeCode:2}{counter:6}` = 9 digits  
**UserName format:** letters-only using base26 encoding

Use different `--run-id` for each test run to avoid duplicates.

---

## Ports & Firewall

| Port | Protocol | Purpose |
|------|----------|---------|
| 7312 | HTTP/1.1 | Controller Web UI |
| 7313 | HTTP/2 | gRPC Worker connection |

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Worker can't connect | Check firewall ports 7312-7313 |
| 401 Auth errors | Verify Postman env file has correct credentials |
| Duplicate errors | Use new `--run-id` |
| Registration fails | Ensure `profileId` is set in Postman env file |

---

## ENV Overrides (Optional)

Override Postman values if needed:

```powershell
$env:BASE_URL = "https://custom-api.com"
$env:PROFILE_ID = "guid-here"
$env:OTP_CODE = "004121"
```

---

## Postman Files Location

```
SignupActors/
├── 1- file Cashless-Local-Marketing.postman_environment.json
├── Public-Marketing.postman_collection.json
└── resopnses.json
```

These files are the **source of truth** for API configuration.
