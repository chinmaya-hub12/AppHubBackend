# AppHub Backend — Developer Guide
## How to Clone, Run, and Extend This Solution

---

## 1. SOLUTION OVERVIEW

```
AppHubBackend_Enhanced/
├── AppHub.WebApi/                  ← Main solution folder
│   ├── AppHub.Core/                ← DTOs, Entities, Enums  (no dependencies)
│   ├── AppHub.SharedKernel/        ← Base classes, interfaces (no dependencies)
│   ├── AppHub.Infrastructure/      ← Data access, services (depends on Core + SharedKernel)
│   └── AppHub.WebApi/              ← ASP.NET Core host (depends on all above)
├── AppHub.Auth.Service/            ← Standalone auth microservice (login + MFA)
└── k8s/                            ← Kubernetes deployment YAML files
```

### Dependency direction (always one-way):
```
WebApi → Infrastructure → Core
WebApi → SharedKernel
Infrastructure → Core
Infrastructure → SharedKernel
```

### Technology stack:
| Layer | Technology |
|---|---|
| API Framework | ASP.NET Core 8 |
| Database | SQL Server (EF Core, original stored procs) |
| Distributed Cache | Redis (Azure Cache for Redis in production) |
| Document Store | Azure Cosmos DB (AuditLog, MfaSession, IdempotencyRecord) |
| File Storage | Azure Blob Storage |
| Secrets | Azure Key Vault |
| Auth | JWT (local) + Azure Entra ID (AAD) |
| MFA | 6-digit email OTP via Azure Communication Services |
| Idempotency | Redis L1 + Cosmos L2, 24-hour TTL |
| Logging | Serilog → Console + File |
| Container | Docker |
| Orchestration | Kubernetes (AKS) |

---

## 2. PREREQUISITES (install once)

| Tool | Version | Download |
|---|---|---|
| .NET SDK | 8.0+ | https://dotnet.microsoft.com/download |
| Visual Studio | 2022 17.8+ | With "ASP.NET and web development" workload |
| Docker Desktop | Latest | https://www.docker.com/products/docker-desktop |
| Git | Latest | https://git-scm.com |
| Azure CLI | Latest | https://learn.microsoft.com/cli/azure/install-azure-cli |
| Redis (local) | Latest | Via Docker (see step 4B) |

---

## 3. FIRST-TIME SETUP (every developer does this once)

### Step 1 — Clone the repository
```bash
git clone https://YOUR_REPO_URL/AppHubBackend.git
cd AppHubBackend
```

### Step 2 — Open in Visual Studio
- Open `AppHub.WebApi/AppHub.WebApi.slnx`
- Wait for NuGet restore to complete (first time: 2–5 minutes)
- Set startup project: right-click `AppHub.WebApi` → Set as Startup Project

### Step 3 — Restore packages (CLI alternative)
```bash
cd AppHub.WebApi
dotnet restore
```

### Step 4A — Run with minimal setup (SQL Server only, no Azure)
The app is designed to start without any Azure services configured.
When Redis / CosmosDB / Blob / ACS are empty in appsettings.json, it uses safe fallbacks.

Edit `AppHub.WebApi/appsettings.json` — only change the SQL Server connection:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=YOUR_SERVER;Database=AppHub;User Id=sa;Password=YOUR_PASS;TrustServerCertificate=True"
  }
}
```

Run:
```bash
cd AppHub.WebApi/AppHub.WebApi
dotnet run
```

Open: http://localhost:5000/swagger

### Step 4B — Run Redis locally (optional but recommended)
```bash
# Start Redis in Docker (one command)
docker run -d --name redis-local -p 6379:6379 redis:latest

# Verify it's running
docker exec redis-local redis-cli ping
# Should print: PONG
```

Then add to appsettings.json:
```json
"Redis": {
  "ConnectionString": "localhost:6379"
}
```

When Redis is set, MFA rate limiting and idempotency L1 cache activate automatically.

---

## 4. CONFIGURATION GUIDE

### appsettings.json — what each section does

```json
{
  "KeyVault": {
    "VaultUri": ""  ← LEAVE EMPTY for local dev. Fill in production Key Vault URI.
                       When empty, app reads ALL config from appsettings.json.
                       When set, Key Vault values OVERRIDE appsettings.json.
  },

  "ConnectionStrings": {
    "DefaultConnection": "..."  ← Your SQL Server. Used by EF Core + UserService stored proc.
  },

  "IdentityServerSettings": {
    "ApiSecret": "..."          ← JWT signing secret. MUST match across WebApi + Auth.Service.
    "ValidIssuer": "..."        ← JWT issuer claim. Use your domain in production.
    "ValidAudience": "..."      ← JWT audience claim. Use your domain in production.
    "AllowedOrigins": [...]     ← CORS origins for your frontend app.
    "Expiry": 60.0              ← JWT token lifetime in MINUTES.
  },

  "AzureAd": {
    "TenantId": "",  ← Leave empty to disable AAD. Fill in for Azure Entra ID login.
    "ClientId": "",  ← App registration Client ID.
    "ClientSecret":""← App registration Client Secret.
  },

  "CosmosDb": {
    "AccountEndpoint": "",  ← Leave empty: uses no-op stubs (local dev works without Cosmos).
    "AccountKey": "",       ← Cosmos primary key.
    "DatabaseName": "AppHubDb"
  },

  "Redis": {
    "ConnectionString": ""  ← Leave empty: uses in-memory cache (not distributed).
                               Set "localhost:6379" for local Redis.
                               Set "YOUR.redis.cache.windows.net:6380,password=...,ssl=True"
                               for Azure Cache for Redis.
  },

  "BlobStorage": {
    "ConnectionString": "", ← Leave empty: blob upload/download will fail gracefully.
    "AccountName": "",
    "AccountKey": ""
  },

  "AzureCommunicationServices": {
    "ConnectionString": "", ← Leave empty: OTP is LOGGED to console instead of emailed.
    "SenderEmail": "..."    ← Verified sender address in your ACS resource.
  }
}
```

### Environment-specific overrides
Use `appsettings.Development.json` for local dev overrides (already in .gitignore):
```json
{
  "Logging": { "LogLevel": { "Default": "Debug" } },
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=AppHub_Dev;..."
  }
}
```

Use `appsettings.Production.json` or environment variables for production:
```bash
# Environment variable overrides appsettings.json
export ConnectionStrings__DefaultConnection="Server=prod-server;..."
export Redis__ConnectionString="prod-redis.redis.cache.windows.net:6380,password=...,ssl=True"
```

---

## 5. LOGIN FLOW — HOW IT WORKS

```
┌─────────┐         ┌──────────────┐        ┌──────────┐     ┌───────────┐
│  Client │         │  LoginCtrl   │        │  Redis   │     │  Cosmos   │
└────┬────┘         └──────┬───────┘        └────┬─────┘     └─────┬─────┘
     │                     │                     │                  │
     │  POST /api/login     │                     │                  │
     │  + Idempotency-Key  │                     │                  │
     │  {user,pass,email}  │                     │                  │
     │────────────────────>│                     │                  │
     │                     │ Check rate limit     │                  │
     │                     │─────────────────────>│                  │
     │                     │ OK (count < 5)       │                  │
     │                     │<─────────────────────│                  │
     │                     │ Validate user (SQL)   │                  │
     │                     │ Verify password hash  │                  │
     │                     │ Generate 6-digit OTP  │                  │
     │                     │ Store OTP hash ───────│────────────────>│
     │                     │ Send email (ACS)      │                  │
     │  {mfaToken, msg}    │                     │                  │
     │<────────────────────│                     │                  │
     │                     │                     │                  │
     │  POST /api/login/verify-otp               │                  │
     │  + NEW Idempotency-Key                     │                  │
     │  {mfaToken, "847392"}                      │                  │
     │────────────────────>│                     │                  │
     │                     │ Lookup session ───────│────────────────>│
     │                     │ Hash compare (safe)   │                  │
     │                     │ Mark session used ────│────────────────>│
     │                     │ Clear rate limit ─────>│                  │
     │                     │ Generate JWT          │                  │
     │  {token, username}  │                     │                  │
     │<────────────────────│                     │                  │
```

### Key rules:
1. ALWAYS use a **different** Idempotency-Key for Step 1 and Step 2
2. If you retry Step 1 with the **same** Idempotency-Key → original response returned (no second email)
3. If you retry Step 2 with the **same** Idempotency-Key → same JWT returned (no second token)
4. OTP expires in 5 minutes
5. Max 3 wrong OTP attempts → session locked
6. Max 5 OTP requests per 15 minutes per user → rate limited by Redis

---

## 6. REDIS CACHE — KEY REFERENCE

| Redis Key | TTL | Purpose |
|---|---|---|
| `otp:ratelimit:{username}` | 15 min | Count OTP requests (max 5) |
| `otp:cooldown:{username}` | 60 sec | Cooldown between OTP sends |
| `idempotency:{key}` | 24 hrs | L1 fast cache before Cosmos lookup |
| `user:session:{userId}` | 60 min | Reserved for future session caching |

### How to check Redis in development:
```bash
# Connect to local Redis
docker exec -it redis-local redis-cli

# List all keys
KEYS *

# Check OTP rate limit for a user
GET otp:ratelimit:john

# Clear all keys (reset dev state)
FLUSHALL
```

---

## 7. IDEMPOTENCY — HOW TO USE

Every POST/PUT/DELETE/PATCH must include the `Idempotency-Key` header.

### Generate a key (C# example):
```csharp
var key = Guid.NewGuid().ToString(); // "550e8400-e29b-41d4-a716-446655440000"
```

### Include in HTTP request:
```
POST /api/login
Idempotency-Key: 550e8400-e29b-41d4-a716-446655440000
Content-Type: application/json

{ "username": "john", "password": "pass123", "email": "john@company.com" }
```

### Behaviour:
| Scenario | Result |
|---|---|
| First request | Executes normally, caches response |
| Retry (same key, within 24h) | Returns cached response, `X-Idempotent-Replayed: true` header |
| Concurrent duplicate | HTTP 409 — wait and retry |
| No header on [RequiresIdempotencyKey] endpoint | HTTP 400 |
| GET requests | Always pass through (naturally idempotent) |

---

## 8. AZURE SERVICES SETUP (production)

### Azure Key Vault
```bash
az keyvault create --name apphub-kv --resource-group apphub-rg --location eastus

# Add all secrets (replace values with real ones)
KV=apphub-kv
az keyvault secret set --vault-name $KV --name "ConnectionStrings--DefaultConnection" --value "Server=...;Database=AppHub;..."
az keyvault secret set --vault-name $KV --name "IdentityServerSettings--ApiSecret" --value "your-jwt-secret-min-32-chars"
az keyvault secret set --vault-name $KV --name "CosmosDb--AccountKey" --value "cosmos-primary-key"
az keyvault secret set --vault-name $KV --name "Redis--ConnectionString" --value "yourredis.redis.cache.windows.net:6380,password=...,ssl=True,abortConnect=False"
az keyvault secret set --vault-name $KV --name "BlobStorage--ConnectionString" --value "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;"
az keyvault secret set --vault-name $KV --name "AzureAd--TenantId" --value "your-tenant-id"
az keyvault secret set --vault-name $KV --name "AzureAd--ClientId" --value "your-client-id"
az keyvault secret set --vault-name $KV --name "AzureAd--ClientSecret" --value "your-client-secret"
az keyvault secret set --vault-name $KV --name "AzureCommunicationServices--ConnectionString" --value "endpoint=https://...;accesskey=..."
```

Then in appsettings.json (production):
```json
"KeyVault": {
  "VaultUri": "https://apphub-kv.vault.azure.net/"
}
```

### Azure Cache for Redis
```bash
az redis create --name apphub-redis --resource-group apphub-rg --location eastus --sku Basic --vm-size C0

# Get connection string
az redis list-keys --name apphub-redis --resource-group apphub-rg
# Add to Key Vault as Redis--ConnectionString
```

### Azure Cosmos DB
```bash
az cosmosdb create --name apphub-cosmos --resource-group apphub-rg

# Create database
az cosmosdb sql database create --account-name apphub-cosmos --name AppHubDb --resource-group apphub-rg

# Create containers (each with TTL enabled)
for container in AuditLog MfaSession IdempotencyRecord; do
  az cosmosdb sql container create \
    --account-name apphub-cosmos \
    --database-name AppHubDb \
    --name $container \
    --partition-key-path "/partitionKey" \
    --default-ttl -1 \
    --resource-group apphub-rg
done
```

### Azure Blob Storage
```bash
az storage account create --name apphubstorage --resource-group apphub-rg --location eastus --sku Standard_LRS
# Containers are auto-created on first use by the app
```

### Azure Communication Services (for OTP email)
```bash
az communication create --name apphub-acs --resource-group apphub-rg --data-location unitedstates
# Add a verified email domain in Azure Portal → ACS → Email → Domains
```

---

## 9. KUBERNETES DEPLOYMENT

```bash
# 1. Create namespace
kubectl apply -f k8s/namespace.yaml

# 2. Edit k8s/secrets.yaml — set your Key Vault URI
kubectl apply -f k8s/secrets.yaml
kubectl apply -f k8s/configmap.yaml

# 3. Build and push to Azure Container Registry
az acr build --registry YOUR_ACR --image apphub-webapi:v1.0 ./AppHub.WebApi
az acr build --registry YOUR_ACR --image apphub-auth:v1.0 ./AppHub.Auth.Service

# 4. Deploy
kubectl apply -f k8s/apphub-webapi-deployment.yaml
kubectl apply -f k8s/apphub-auth-deployment.yaml
kubectl apply -f k8s/ingress.yaml
kubectl apply -f k8s/hpa.yaml

# 5. Watch pods come up
kubectl get pods -n apphub -w

# 6. Check logs
kubectl logs -n apphub deployment/apphub-webapi --follow
```

---

## 10. API ENDPOINT REFERENCE

### Authentication (no JWT needed)
| Method | URL | Body | Description |
|---|---|---|---|
| POST | /api/login | `{username, password, email}` | Step 1: validate + send OTP |
| POST | /api/login/verify-otp | `{mfaToken, otpCode}` | Step 2: verify OTP → JWT |
| GET | /api/login/Encrypt?password= | — | Encrypt password utility |
| GET | /api/home/Decrypt?password= | — | Decrypt password utility |

### Blob Storage (JWT required)
| Method | URL | Description |
|---|---|---|
| POST | /api/blob/upload | Upload file (multipart, max 50MB) |
| POST | /api/blob/upload/base64 | Upload file (base64 JSON) |
| GET | /api/blob/download/{container}/{name} | Download file |
| GET | /api/blob/sas/{container}/{name}?expiryMinutes=60 | Get SAS URL |
| GET | /api/blob/list/{container} | List files |
| DELETE | /api/blob/{container}/{name} | Delete file |

### Azure Entra ID (AAD token required)
| Method | URL | Description |
|---|---|---|
| GET | /api/auth/aad/profile | AAD claims |
| GET | /api/auth/aad/ping | Auth health check (JWT or AAD) |

### Health
| URL | Description |
|---|---|
| /health/live | Kubernetes liveness probe |
| /health/ready | Kubernetes readiness probe |
| /swagger | Interactive API documentation |

---

## 11. PROJECT STRUCTURE — WHERE TO ADD CODE

### Adding a new API endpoint:
1. Add DTO to `AppHub.Core/Dto/`
2. Add interface to `AppHub.Infrastructure/Abstract/`
3. Add implementation to `AppHub.Infrastructure/Concrete/`
4. Register in `AppHub.WebApi/Extensions/ServiceExtension.cs`
5. Add controller to `AppHub.WebApi/Controllers/`
6. Add `[RequiresIdempotencyKey]` to POST/PUT/DELETE actions
7. Add `[Authorize]` if JWT required

### Adding a new Cosmos DB collection:
1. Create entity class in `AppHub.Core/Entity/` extending `CosmosDocument`
2. Register in ServiceExtension: `services.AddScoped<ICosmosRepository<YourEntity>, CosmosRepository<YourEntity>>()`
3. Create the container in Azure Cosmos DB with partition key `/partitionKey`

### Adding a new Redis cache key:
Use `ICacheService` via constructor injection:
```csharp
public class YourService(ICacheService cache)
{
    public async Task<UserDto?> GetUserCachedAsync(int userId)
    {
        var key = $"user:{userId}";
        return await _cache.GetOrSetAsync(key,
            () => _db.GetUserAsync(userId),
            TimeSpan.FromMinutes(30));
    }
}
```

---

## 12. COMMON ERRORS AND FIXES

| Error | Cause | Fix |
|---|---|---|
| HTTP 500 on startup | Missing config section | Check appsettings.json has `IdentityServerSettings` section |
| HTTP 401 on all requests | Wrong JWT issuer/audience | Match `ValidIssuer`+`ValidAudience` in appsettings to what JwtService uses |
| "CosmosClient endpoint null" | Empty CosmosDb config | Normal for local dev — uses no-op stub |
| OTP not received | ACS not configured | Normal for local dev — OTP is logged to console |
| "Too many OTP requests" | Redis rate limit hit | Wait 15 min or run `FLUSHALL` in Redis CLI |
| HTTP 400 "Idempotency-Key required" | Missing header | Add `Idempotency-Key: {uuid}` header |
| HTTP 409 "Request being processed" | Concurrent duplicate | Wait 5 seconds and retry |
| Blob upload fails | BlobStorage not configured | Set BlobStorage:ConnectionString |
| AAD login fails | AzureAd section empty | Normal if not using AAD — use local JWT |

---

## 13. TEAM WORKFLOW

### Daily development:
```bash
git pull origin main
cd AppHub.WebApi/AppHub.WebApi
dotnet run
# Swagger: http://localhost:5000/swagger
```

### Before committing:
```bash
dotnet build       # Must be zero errors
dotnet test        # Must be zero failures (add tests in AppHub.Tests/)
```

### Feature branch workflow:
```bash
git checkout -b feature/YOUR-FEATURE
# make changes
git commit -m "feat: describe what you did"
git push origin feature/YOUR-FEATURE
# open Pull Request → code review → merge to main
```

### Never commit:
- Real passwords or connection strings to appsettings.json (use appsettings.Development.json which is in .gitignore)
- Key Vault URIs pointing to production
- Azure credentials

---

## 14. WHAT EACH SERVICE DOES (quick reference)

| File | What it does |
|---|---|
| `Program.cs` | App entry point. Wires Key Vault → Serilog → Build |
| `HostingExtension.cs` | Registers all services + middleware pipeline |
| `ServiceExtension.cs` | DI registrations (Redis, Cosmos, Blob, MFA, Idempotency) |
| `LoginController.cs` | 2-step OTP login. Step1=credentials→OTP, Step2=OTP→JWT |
| `BlobController.cs` | File upload/download/SAS/list/delete |
| `AzureAdController.cs` | Azure Entra ID token validation |
| `MfaService.cs` | Generates OTP, rate-limits via Redis, sends email via ACS |
| `IdempotencyMiddleware.cs` | Intercepts all POST/PUT/DELETE, deduplicates via Redis+Cosmos |
| `RedisCacheService.cs` | Typed Redis wrapper with JSON serialization + fail-open |
| `InMemoryCacheService.cs` | Local dev fallback when Redis not configured |
| `CosmosRepository.cs` | Generic Cosmos DB CRUD (used by MFA, Audit, Idempotency) |
| `BlobStorageService.cs` | Azure Blob upload/download/SAS generation |
| `KeyVaultService.cs` | Programmatic Key Vault secret read/write |
| `AuditService.cs` | Writes login/action events to Cosmos AuditLog container |
| `JwtService.cs` | Generates JWT with user claims |
| `PasswordHasher.cs` | Base64 password verification (original logic) |
| `UserService.cs` | Calls sp_UserDetail stored procedure via EF Core |
