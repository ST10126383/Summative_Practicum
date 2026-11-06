using AgriStreamIoT.Models;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace AgriStreamIoT.Services;

public interface IFunctionClientService
{
    Task<IEnumerable<TelemetryEntity>> GetTelemetryFromFunctionAsync();
    Task<bool> TriggerIrrigationViaFunctionAsync(IrrigationCommand command);
}

public sealed class FunctionClientService(HttpClient http, IConfiguration config, ILogger<FunctionClientService> logger, IAzureStorageService storage) : IFunctionClientService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    public async Task<IEnumerable<TelemetryEntity>> GetTelemetryFromFunctionAsync()
    {
        var url=config["AzureFunctions:GetTelemetryUrl"];
        if(string.IsNullOrWhiteSpace(url)) return await storage.GetLatestTelemetryAsync();
        using var request=new HttpRequestMessage(HttpMethod.Get,url);
        AddFunctionKey(request);
        using var response=await http.SendAsync(request);
        if(!response.IsSuccessStatusCode){logger.LogError("GetTelemetry Function returned {StatusCode}",response.StatusCode);response.EnsureSuccessStatusCode();}
        return await response.Content.ReadFromJsonAsync<List<TelemetryEntity>>(JsonOptions) ?? [];
    }

    public async Task<bool> TriggerIrrigationViaFunctionAsync(IrrigationCommand command)
    {
        var url=config["AzureFunctions:PostIrrigationUrl"];
        if(string.IsNullOrWhiteSpace(url)){await storage.SendIrrigationCommandAsync(command);return true;}
        using var request=new HttpRequestMessage(HttpMethod.Post,url){Content=new StringContent(JsonSerializer.Serialize(command,JsonOptions),Encoding.UTF8,"application/json")};
        AddFunctionKey(request);
        using var response=await http.SendAsync(request);
        if(!response.IsSuccessStatusCode){logger.LogError("PostIrrigation Function returned {StatusCode}",response.StatusCode);return false;}
        return true;
    }

    private void AddFunctionKey(HttpRequestMessage request)
    {
        var key=config["AzureFunctions:FunctionKey"];
        if(!string.IsNullOrWhiteSpace(key)) request.Headers.Add("x-functions-key",key);
    }
}
