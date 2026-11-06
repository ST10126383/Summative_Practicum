using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using AgriStreamIoT.Models;
using System.Text;
using System.Text.Json;

namespace AgriStreamIoT.Services
{
    public interface IAzureStorageService
    {
        Task<string> UploadDroneImageAsync(Stream fileStream, string fileName, string contentType, string uploadedBy);
        Task<IEnumerable<TelemetryEntity>> GetLatestTelemetryAsync(string partitionKey = "Farm-001", int max = 50);
        Task AddTelemetryAsync(TelemetryEntity entity);
        Task<IEnumerable<ImageAuditEntity>> GetRecentImageAuditsAsync(int max = 20);
        Task SendIrrigationCommandAsync(IrrigationCommand command);
        Task EnsureContainersAndTablesAsync();
    }

    /// <summary>
    /// Central service for Azure Blob, Table and Queue Storage.
    /// When connection strings are empty the service falls back to in-memory simulation
    /// so the MVC app can be demonstrated without live Azure resources.
    /// </summary>
    public class AzureStorageService : IAzureStorageService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<AzureStorageService> _logger;
        private readonly BlobServiceClient? _blobClient;
        private readonly TableServiceClient? _tableClient;
        private readonly QueueClient? _queueClient;

        // In-memory fallbacks for simulation mode
        private static readonly List<TelemetryEntity> _simTelemetry = new();
        private static readonly List<ImageAuditEntity> _simAudits = new();
        private static readonly List<IrrigationCommand> _simQueue = new();
        private static readonly object _lock = new();

        private readonly string _blobContainerName = "drone-images";
        private readonly string _telemetryTableName = "TelemetryData";
        private readonly string _auditTableName = "ImageAudit";
        private readonly string _queueName = "irrigation-commands";

        public AzureStorageService(IConfiguration config, ILogger<AzureStorageService> logger)
        {
            _config = config;
            _logger = logger;

            var storageConn = _config["AzureStorage:ConnectionString"] ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(storageConn))
            {
                try
                {
                    _blobClient = new BlobServiceClient(storageConn);
                    _tableClient = new TableServiceClient(storageConn);
                    _queueClient = new QueueClient(storageConn, _queueName);
                    _logger.LogInformation("Azure Storage clients initialised.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create Azure Storage clients. Running in simulation mode.");
                }
            }
            else
            {
                _logger.LogWarning("AzureStorage:ConnectionString not set. Running in simulation mode.");
            }
        }

        public async Task EnsureContainersAndTablesAsync()
        {
            if (_blobClient != null)
            {
                var container = _blobClient.GetBlobContainerClient(_blobContainerName);
                await container.CreateIfNotExistsAsync(PublicAccessType.None);
            }
            if (_tableClient != null)
            {
                await _tableClient.CreateTableIfNotExistsAsync(_telemetryTableName);
                await _tableClient.CreateTableIfNotExistsAsync(_auditTableName);
            }
            if (_queueClient != null)
            {
                await _queueClient.CreateIfNotExistsAsync();
            }
            _logger.LogInformation("Storage resources ensured (or simulation ready).");
        }

        public async Task<string> UploadDroneImageAsync(Stream fileStream, string fileName, string contentType, string uploadedBy)
        {
            var safeFileName = Path.GetFileName(fileName);
            var uniqueName = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}_{safeFileName}";

            if (_blobClient == null)
            {
                // Simulation
                var simUrl = $"https://simulation.blob.core.windows.net/{_blobContainerName}/{uniqueName}";
                lock (_lock)
                {
                    _simAudits.Insert(0, new ImageAuditEntity
                    {
                        RowKey = Guid.NewGuid().ToString(),
                        BlobName = uniqueName,
                        BlobUrl = simUrl,
                        UploadedBy = uploadedBy,
                        FileSizeBytes = fileStream.Length,
                        ContentType = contentType,
                        ProcessingStatus = "Pending (simulation)",
                        UploadTime = DateTime.UtcNow
                    });
                }
                _logger.LogInformation("[SIM] Uploaded image {Name}", uniqueName);
                return simUrl;
            }

            var container = _blobClient.GetBlobContainerClient(_blobContainerName);
            var blob = container.GetBlobClient(uniqueName);
            await blob.UploadAsync(fileStream, new BlobHttpHeaders { ContentType = contentType });

            // The Blob-triggered Azure Function will pick this up and write the audit entry.
            // For immediate UI feedback we also write a local audit if needed.
            return blob.Uri.ToString();
        }

        public async Task<IEnumerable<TelemetryEntity>> GetLatestTelemetryAsync(string partitionKey = "Farm-001", int max = 50)
        {
            if (_tableClient == null)
            {
                lock (_lock)
                {
                    return _simTelemetry
                        .Where(t => t.PartitionKey == partitionKey)
                        .OrderByDescending(t => t.ReadingTime)
                        .Take(max)
                        .ToList();
                }
            }

            var table = _tableClient.GetTableClient(_telemetryTableName);
            var results = new List<TelemetryEntity>();
            var safePartition = partitionKey.Replace("'", "''", StringComparison.Ordinal);
            await foreach (var entity in table.QueryAsync<TelemetryEntity>(filter: $"PartitionKey eq '{safePartition}'", maxPerPage: max))
            {
                results.Add(entity);
                if (results.Count >= max) break;
            }
            return results.OrderByDescending(t => t.ReadingTime).Take(max);
        }

        public async Task AddTelemetryAsync(TelemetryEntity entity)
        {
            entity.ReadingTime = DateTime.UtcNow;
            if (string.IsNullOrWhiteSpace(entity.PartitionKey)) entity.PartitionKey = "Farm-001";
            // Reverse UTC ticks so newest readings sort first; GUID suffix prevents same-tick collisions.
            entity.RowKey = $"{(DateTime.MaxValue.Ticks - entity.ReadingTime.Ticks):D19}_{Guid.NewGuid():N}";

            if (_tableClient == null)
            {
                lock (_lock) { _simTelemetry.Insert(0, entity); }
                return;
            }

            var table = _tableClient.GetTableClient(_telemetryTableName);
            await table.AddEntityAsync(entity);
        }

        public async Task<IEnumerable<ImageAuditEntity>> GetRecentImageAuditsAsync(int max = 20)
        {
            if (_tableClient == null)
            {
                lock (_lock) { return _simAudits.Take(max).ToList(); }
            }

            var table = _tableClient.GetTableClient(_auditTableName);
            var results = new List<ImageAuditEntity>();
            await foreach (var entity in table.QueryAsync<ImageAuditEntity>(e => e.PartitionKey == "ImageUploads"))
            {
                results.Add(entity);
                if (results.Count >= max) break;
            }
            return results.OrderByDescending(a => a.UploadTime).Take(max);
        }

        public async Task SendIrrigationCommandAsync(IrrigationCommand command)
        {
            var json = JsonSerializer.Serialize(command);
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

            if (_queueClient == null)
            {
                lock (_lock) { _simQueue.Add(command); }
                _logger.LogInformation("[SIM] Queued irrigation command for device {Device}", command.DeviceId);
                return;
            }

            await _queueClient.SendMessageAsync(base64);
            _logger.LogInformation("Irrigation command queued for device {Device}", command.DeviceId);
        }
    }
}
