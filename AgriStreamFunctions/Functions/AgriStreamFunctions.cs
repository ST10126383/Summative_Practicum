using System.Data;
using System.Net;
using System.Text.Json;
using Azure.Data.Tables;
using Azure.Storage.Queues;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgriStreamFunctions.Functions;

public sealed class AgriStreamFunctions
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AgriStreamFunctions> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    public AgriStreamFunctions(IConfiguration configuration, ILogger<AgriStreamFunctions> logger)
    { _configuration=configuration; _logger=logger; }

    // 1. Blob trigger: audit every newly uploaded drone image in Table Storage.
    [Function(nameof(ProcessDroneImage))]
    public async Task ProcessDroneImage([BlobTrigger("drone-images/{name}", Connection="AzureWebJobsStorage")] Stream image, string name)
    {
        var connection=Required("AzureStorageConnection");
        var tables=new TableServiceClient(connection);
        var table=tables.GetTableClient("ImageAudit");
        await table.CreateIfNotExistsAsync();
        var fileSize = image.CanSeek ? image.Length : -1L;
        var entity=new TableEntity("ImageUploads",Guid.NewGuid().ToString("N"))
        {
            ["BlobName"]=name,["FileSizeBytes"]=fileSize,["ContentType"]=GuessContentType(name),
            ["ProcessingStatus"]="Processed",["UploadTime"]=DateTime.UtcNow
        };
        await table.AddEntityAsync(entity);
        _logger.LogInformation("Audited drone image {BlobName} ({Length} bytes)",name,fileSize);
    }

    // 2. HTTP GET trigger: return the most recent telemetry readings.
    [Function(nameof(GetTelemetry))]
    public async Task<HttpResponseData> GetTelemetry([HttpTrigger(AuthorizationLevel.Function,"get",Route="GetTelemetry")] HttpRequestData request)
    {
        var table=new TableServiceClient(Required("AzureStorageConnection")).GetTableClient("TelemetryData");
        var output=new List<Dictionary<string,object?>>();
        await foreach(var row in table.QueryAsync<TableEntity>(filter: "PartitionKey eq 'Farm-001'", maxPerPage:50))
        {
            output.Add(new Dictionary<string,object?> { ["partitionKey"]=row.PartitionKey,["rowKey"]=row.RowKey,["deviceId"]=row.GetString("DeviceId"),["sensorType"]=row.GetString("SensorType"),["value"]=row.GetDouble("Value"),["unit"]=row.GetString("Unit"),["location"]=row.GetString("Location"),["readingTime"]=row.GetDateTime("ReadingTime") ?? row.Timestamp });
        }
        var response=request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(output.Take(50));
        return response;
    }

    // 3. HTTP POST trigger: validate command and enqueue it for asynchronous processing.
    [Function(nameof(PostIrrigation))]
    public async Task<HttpResponseData> PostIrrigation([HttpTrigger(AuthorizationLevel.Function,"post",Route="PostIrrigation")] HttpRequestData request)
    {
        IrrigationCommand? command;
        try { command=await JsonSerializer.DeserializeAsync<IrrigationCommand>(request.Body,JsonOptions); }
        catch(JsonException) { var bad=request.CreateResponse(HttpStatusCode.BadRequest);await bad.WriteStringAsync("Request body must contain valid irrigation command JSON.");return bad; }
        if(command is null || string.IsNullOrWhiteSpace(command.DeviceId) || command.DurationMinutes is < 1 or > 1440 || command.Action is not ("StartIrrigation" or "StopIrrigation"))
        { var bad=request.CreateResponse(HttpStatusCode.BadRequest);await bad.WriteStringAsync("Invalid irrigation command.");return bad; }
        command.CommandId=string.IsNullOrWhiteSpace(command.CommandId)?Guid.NewGuid().ToString("N"):command.CommandId;
        var queue=new QueueClient(Required("AzureStorageConnection"),"irrigation-commands");
        await queue.CreateIfNotExistsAsync();
        await queue.SendMessageAsync(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command,JsonOptions))));
        var response=request.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(new { message="Irrigation command queued",commandId=command.CommandId,deviceId=command.DeviceId });
        return response;
    }

    // 4. Queue trigger: update SQL device state only after the command is consumed.
    [Function(nameof(ProcessIrrigationCommand))]
    public async Task ProcessIrrigationCommand([QueueTrigger("irrigation-commands",Connection="AzureWebJobsStorage")] string message)
    {
        var command=JsonSerializer.Deserialize<IrrigationCommand>(message,JsonOptions) ?? throw new InvalidOperationException("Queue message could not be deserialized.");
        var status=command.Action=="StartIrrigation"?"Irrigating":"Idle";
        const string sql="UPDATE dbo.DeviceStatus SET Status=@status,LastUpdated=SYSUTCDATETIME(),LastCommandId=@commandId WHERE DeviceId=@deviceId";
        await using var connection=new SqlConnection(Required("AzureSqlConnection"));
        await connection.OpenAsync();
        await using var dbCommand=new SqlCommand(sql,connection);
        dbCommand.Parameters.Add("@status",SqlDbType.NVarChar,30).Value=status;
        dbCommand.Parameters.Add("@commandId",SqlDbType.NVarChar,50).Value=command.CommandId;
        dbCommand.Parameters.Add("@deviceId",SqlDbType.NVarChar,50).Value=command.DeviceId;
        if(await dbCommand.ExecuteNonQueryAsync()==0) throw new InvalidOperationException($"Device {command.DeviceId} does not exist in dbo.DeviceStatus.");
        _logger.LogInformation("Processed irrigation command {CommandId} for {DeviceId}; status={Status}",command.CommandId,command.DeviceId,status);
    }

    private string Required(string key)=>_configuration[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"Missing required Function App setting '{key}'.");
    private static string GuessContentType(string name)=>Path.GetExtension(name).ToLowerInvariant() switch { ".jpg" or ".jpeg"=>"image/jpeg", ".png"=>"image/png", ".tif" or ".tiff"=>"image/tiff", _=>"application/octet-stream" };
}

public sealed class IrrigationCommand
{
    public string CommandId { get; set; }=Guid.NewGuid().ToString("N");
    public string DeviceId { get; set; }=string.Empty;
    public string FieldId { get; set; }=string.Empty;
    public string Action { get; set; }="StartIrrigation";
    public int DurationMinutes { get; set; }=30;
    public string RequestedBy { get; set; }=string.Empty;
    public DateTime RequestedAt { get; set; }=DateTime.UtcNow;
}
