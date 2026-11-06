# Azure Functions

The deployable implementations are in the sibling `AgriStreamFunctions/` project at the solution root. This folder is retained as a pointer for the MVC project documentation.

| Function | Trigger | Behaviour |
|---|---|---|
| `ProcessDroneImage` | Blob | Watches `drone-images` and writes image metadata/status to `ImageAudit` Table Storage. |
| `GetTelemetry` | HTTP GET | Reads telemetry from `TelemetryData` and returns JSON. |
| `PostIrrigation` | HTTP POST | Validates an irrigation command and enqueues it in `irrigation-commands`. |
| `ProcessIrrigationCommand` | Queue | Processes the queued command and updates `dbo.DeviceStatus` in Azure SQL. |

All four use .NET 8 isolated worker and Functions runtime v4. Configure `AzureWebJobsStorage`, `AzureStorageConnection` and `AzureSqlConnection` in the Function App settings. See `STEP_BY_STEP_AZURE_GUIDE.md`.
