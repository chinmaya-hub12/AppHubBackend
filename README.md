# AppHub Backend — Enterprise Edition
# Azure Key Vault + Blob Storage + Email OTP MFA + Kubernetes

## Architecture

```
Internet
   |
   v
NGINX Ingress (Kubernetes)
   |-- /auth/*   --> AppHub.Auth.Service  (port 80)
   +-- /api/*    --> AppHub.WebApi        (port 80)
          |
          |-- SQL Server       (EF Core, original unchanged)
          |-- Azure Cosmos DB  (AuditLog + MfaSession containers)
          |-- Azure Blob Storage  (file uploads)
          +-- Azure Key Vault  (ALL secrets loaded at startup)
```

## Email OTP MFA Login Flow

```
STEP 1 — POST /api/login
  Request:  { "username": "john", "password": "pass123", "email": "john@company.com" }
  Action:   Validates credentials -> Generates 6-digit OTP -> Sends email via ACS
  Response: { "success": true, "data": { "requiresMfa": true, "mfaToken": "abc123..." },
              "message": "A 6-digit code has been sent to j***n@company.com" }

STEP 2 — POST /api/login/verify-otp
  Request:  { "mfaToken": "abc123...", "otpCode": "847392" }
  Action:   Validates OTP (5-min expiry, 3 attempts max, one-time use) -> Issues JWT
  Response: { "success": true, "data": { "token": "eyJ...", "username": "john",
              "userType": "Admin", "expiresAt": "2026-03-25T11:30:00Z" } }
```

## Azure Key Vault — ALL Secrets Stored Here

Secret naming: replace ":" with "--" in Key Vault names.

| Secret Name in Key Vault | What it holds |
|---|---|
| ConnectionStrings--DefaultConnection | SQL Server connection string |
| IdentityServerSettings--ApiSecret | JWT signing secret |
| CosmosDb--AccountKey | Cosmos DB primary key |
| BlobStorage--ConnectionString | Storage account connection string |
| BlobStorage--AccountKey | Storage account key |
| AzureAd--TenantId | Azure Entra ID tenant ID |
| AzureAd--ClientId | Azure Entra ID client/app ID |
| AzureAd--ClientSecret | Azure Entra ID client secret |
| AzureCommunicationServices--ConnectionString | ACS connection string for OTP emails |

### Setup Key Vault
```
az keyvault create --name YOUR-KV --resource-group YOUR-RG --location eastus

KV=YOUR-KV
az keyvault secret set --vault-name $KV --name "ConnectionStrings--DefaultConnection" --value "Server=...;"
az keyvault secret set --vault-name $KV --name "IdentityServerSettings--ApiSecret" --value "your-secret"
az keyvault secret set --vault-name $KV --name "CosmosDb--AccountKey" --value "cosmos-key"
az keyvault secret set --vault-name $KV --name "BlobStorage--ConnectionString" --value "DefaultEndpointsProtocol=https;..."
az keyvault secret set --vault-name $KV --name "BlobStorage--AccountKey" --value "storage-key"
az keyvault secret set --vault-name $KV --name "AzureAd--TenantId" --value "your-tenant-id"
az keyvault secret set --vault-name $KV --name "AzureAd--ClientId" --value "your-client-id"
az keyvault secret set --vault-name $KV --name "AzureAd--ClientSecret" --value "your-client-secret"
az keyvault secret set --vault-name $KV --name "AzureCommunicationServices--ConnectionString" --value "endpoint=https://...;accesskey=..."
```

### Grant AKS Managed Identity Access
```
PRINCIPAL_ID=$(az aks show -g YOUR-RG -n YOUR-AKS --query identityProfile.kubeletidentity.objectId -o tsv)
az keyvault set-policy --name $KV --object-id $PRINCIPAL_ID --secret-permissions get list
```

## Azure Communication Services (OTP Emails)

```
az communication create --name YOUR-ACS --resource-group YOUR-RG --data-location unitedstates

# Get connection string and add to Key Vault
az communication list-key --name YOUR-ACS --resource-group YOUR-RG

# Set up a verified sender domain
az communication email domain create --domain-name YOUR_DOMAIN --email-service-name YOUR-ACS --resource-group YOUR-RG --location global --domain-management CustomerManaged
```

## Azure Blob Storage

Containers (auto-created on first use):
| Container | Purpose |
|---|---|
| apphub-documents | User file uploads (POST /api/blob/upload) |
| apphub-exports | Generated reports |
| apphub-avatars | Profile images |
| apphub-backups | Audit log archives |

## API Endpoints

### Login (Email OTP MFA)
| Method | Route | Description |
|---|---|---|
| POST | /api/login | Step 1: validate credentials, send OTP email |
| POST | /api/login/verify-otp | Step 2: submit OTP code, receive JWT |
| GET | /api/login/Encrypt?password= | Encrypt password utility (original) |
| GET | /api/home/Decrypt?password= | Decrypt password utility (original) |

### Blob Storage (requires JWT)
| Method | Route | Description |
|---|---|---|
| POST | /api/blob/upload | Upload file (multipart/form-data, max 50MB) |
| POST | /api/blob/upload/base64 | Upload file (base64 JSON body) |
| GET | /api/blob/download/{container}/{name} | Download file |
| GET | /api/blob/sas/{container}/{name} | Get time-limited SAS URL |
| GET | /api/blob/list/{container} | List files in container |
| DELETE | /api/blob/{container}/{name} | Delete file |

### Azure Entra ID (AAD)
| Method | Route | Description |
|---|---|---|
| GET | /api/auth/aad/profile | Get AAD claims (requires AAD token) |
| GET | /api/auth/aad/ping | Auth health check (JWT or AAD) |

### Health Checks (Kubernetes probes)
| Route | Purpose |
|---|---|
| /health/live | Liveness probe |
| /health/ready | Readiness probe |

## Kubernetes Deployment

```
# 1. Create namespace
kubectl apply -f k8s/namespace.yaml

# 2. Update k8s/secrets.yaml with your Key Vault URI, then apply
kubectl apply -f k8s/secrets.yaml
kubectl apply -f k8s/configmap.yaml

# 3. Build and push Docker images
az acr build --registry YOUR_ACR --image apphub-webapi:latest ./AppHub.WebApi
az acr build --registry YOUR_ACR --image apphub-auth-service:latest ./AppHub.Auth.Service

# 4. Deploy
kubectl apply -f k8s/apphub-webapi-deployment.yaml
kubectl apply -f k8s/apphub-auth-deployment.yaml
kubectl apply -f k8s/ingress.yaml
kubectl apply -f k8s/hpa.yaml

# 5. Check pods
kubectl get pods -n apphub
kubectl logs -n apphub deployment/apphub-webapi
```

## Local Development (without Key Vault)

Set KeyVault:VaultUri to empty in appsettings.Development.json.
App skips Key Vault and reads local appsettings only.

```json
{
  "KeyVault": { "VaultUri": "" },
  "ConnectionStrings": { "DefaultConnection": "Server=localhost;..." },
  "IdentityServerSettings": { "ApiSecret": "local-dev-secret" },
  "AzureCommunicationServices": {
    "ConnectionString": "",
    "SenderEmail": ""
  }
}
```

When AzureCommunicationServices:ConnectionString is empty,
the OTP is logged to console only (dev mode). Never use this in production.
