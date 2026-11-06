# AgriStream IoT – CLDV7112 Practicum

ASP.NET Core 8 MVC application for the AgriStream IoT scenario. The solution integrates Azure App Service, Azure SQL Database, Blob Storage, Table Storage, Queue Storage and **exactly four Azure Functions**.

## Solution layout

- `AgriStreamIoT/` – MVC application (login/roles, dashboard, media upload and irrigation commands).
- `AgriStreamFunctions/` – deployable .NET 8 isolated-worker Function App containing the four required triggers.
- `database/01-create-schema.sql` – creates the SQL tables and seed irrigation devices.
- `database/02-create-function-sql-user.sql` – example least-privilege Function SQL user setup.
- `STEP_BY_STEP_AZURE_GUIDE.md` – Azure setup, configuration, deployment and evidence guide.

## Important security notes

1. `appsettings.json` intentionally contains **no credentials**. Never commit SQL passwords, Storage account keys, Function keys or publish profiles.
2. The original uploaded ZIP contained a Storage account key in its settings file. **Regenerate/rotate that key in Azure Portal** before using the Storage account. The corrected source no longer contains it.
3. SQL Server Authentication is used for Azure SQL. Supply the SQL connection string through .NET User Secrets locally and the App Service **Connection strings** setting in Azure. The connection string must contain `User ID` and `Password` and must not contain `Authentication=Active Directory Default`.
4. The Function App uses a dedicated contained SQL database user with only `SELECT` and `UPDATE` permissions on `dbo.DeviceStatus`.
5. The sample demo accounts (`admin` / `Admin@123`, `farmer1` / `Farmer@123`) exist **only in in-memory demo mode** when no Azure SQL connection is configured. Do not use these credentials in a deployed environment.

## Local MVC development

Install the .NET 8 SDK and Visual Studio 2022 (ASP.NET and web development workload). From a terminal in `AgriStreamIoT/`:

```powershell
dotnet restore
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:AzureSql" "Server=tcp:YOUR-SERVER.database.windows.net,1433;Initial Catalog=AgriStreamDB;Persist Security Info=False;User ID=YOUR-SQL-USER;Password=YOUR-SQL-PASSWORD;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
dotnet user-secrets set "AzureStorage:ConnectionString" "YOUR-ROTATED-STORAGE-CONNECTION-STRING"
dotnet user-secrets set "BootstrapAdmin:Username" "YOUR-ADMIN-USERNAME"
dotnet user-secrets set "BootstrapAdmin:Password" "YOUR-STRONG-ADMIN-PASSWORD"
dotnet build
dotnet run
```

For local demo without cloud resources, leave the SQL and Storage settings unset. The app uses clearly logged in-memory demo data. A live Azure SQL connection is required for assessment evidence of relational persistence.

The first live startup creates `dbo.Users` and `dbo.DeviceStatus` if they are missing. For least privilege, execute `database/01-create-schema.sql` as the SQL administrator first, then configure an MVC SQL user with data reader/writer permissions. See the Azure guide for a least-privilege setup option after provisioning.

## Demo-mode accounts

| Username | Password | Role |
|---|---|---|
| `admin` | `Admin@123` | Admin |
| `farmer1` | `Farmer@123` | Farmer |

These are not inserted into Azure SQL. For a live database, set `BootstrapAdmin:Username` and `BootstrapAdmin:Password` through User Secrets (or the corresponding `BootstrapAdmin__Username` and `BootstrapAdmin__Password` environment variables) for the first run, then remove those bootstrap settings after the first successful run. The password is hashed with ASP.NET Core `PasswordHasher<TUser>`.

## Build the Functions project

From `AgriStreamFunctions/`:

```powershell
dotnet restore
dotnet build
```

For local Functions execution, install Azure Functions Core Tools v4 and Azurite or use a real Storage account. Copy `local.settings.sample.json` to `local.settings.json`, replace the sample settings, and never commit `local.settings.json`.

## Assessment implementation map

| Assessment requirement | Implementation |
|---|---|
| MVC hosted on Azure App Service | `AgriStreamIoT` ASP.NET Core MVC project |
| Azure SQL user management | `Services/UserService.cs`; `Users` table stores username, password hash and role |
| Blob media storage | `AzureStorageService.UploadDroneImageAsync`; private `drone-images` container |
| NoSQL telemetry and audit | `TelemetryData` and `ImageAudit` Azure Tables |
| Queue irrigation commands | `irrigation-commands` queue |
| Exactly four Functions | `ProcessDroneImage`, `GetTelemetry`, `PostIrrigation`, `ProcessIrrigationCommand` |
| Load testing | anonymous `/Dashboard/Health` endpoint and guide scripts |
| Monitoring | Application Insights and Azure Monitor setup guide |
| Autoscaling | App Service Plan autoscale configuration and evidence checklist |

## Verification boundary

The project source has been statically reviewed and configuration files checked. A live .NET compilation, Azure resource deployment, load test and scale-out run must be performed in your own Visual Studio/Azure environment; those cannot be truthfully marked complete from a source ZIP alone.
