using Azure;
using Azure.Data.Tables;

namespace AgriStreamIoT.Models
{
    /// <summary>
    /// High-frequency telemetry stored in Azure Table Storage.
    /// PartitionKey = FarmId or DeviceId, RowKey = timestamp ticks or GUID.
    /// </summary>
    public class TelemetryEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty; // e.g. "Farm-001" or device ID
        public string RowKey { get; set; } = string.Empty;       // unique per reading
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string DeviceId { get; set; } = string.Empty;
        public string SensorType { get; set; } = string.Empty; // SoilMoisture, Temperature, Humidity, etc.
        public double Value { get; set; }
        public string Unit { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public DateTime ReadingTime { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Audit entry written by the Blob-triggered Function when a drone image is uploaded.
    /// </summary>
    public class ImageAuditEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = "ImageUploads";
        public string RowKey { get; set; } = string.Empty;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string BlobName { get; set; } = string.Empty;
        public string BlobUrl { get; set; } = string.Empty;
        public string UploadedBy { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public string ProcessingStatus { get; set; } = "Pending"; // Pending, Processed, Failed
        public DateTime UploadTime { get; set; } = DateTime.UtcNow;
    }

    public class IrrigationCommand
    {
        public string CommandId { get; set; } = Guid.NewGuid().ToString();
        public string DeviceId { get; set; } = string.Empty;
        public string FieldId { get; set; } = string.Empty;
        public string Action { get; set; } = "StartIrrigation"; // StartIrrigation, StopIrrigation
        public int DurationMinutes { get; set; } = 30;
        public string RequestedBy { get; set; } = string.Empty;
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    }

    public class DeviceStatus
    {
        public int Id { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public string FieldId { get; set; } = string.Empty;
        public string Status { get; set; } = "Idle"; // Idle, Irrigating, Offline, Error
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        public string LastCommandId { get; set; } = string.Empty;
    }

    public class DashboardViewModel
    {
        public List<TelemetryEntity> LatestTelemetry { get; set; } = new();
        public List<ImageAuditEntity> RecentUploads { get; set; } = new();
        public List<DeviceStatus> Devices { get; set; } = new();
        public string CurrentUser { get; set; } = string.Empty;
        public string CurrentRole { get; set; } = string.Empty;
    }
}
