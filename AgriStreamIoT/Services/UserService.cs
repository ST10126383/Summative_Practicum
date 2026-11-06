using AgriStreamIoT.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace AgriStreamIoT.Services;

public interface IUserService
{
    Task InitializeDatabaseAsync();
    Task<UserAccount?> AuthenticateAsync(string username, string password);
    Task<bool> RegisterAsync(RegisterViewModel model);
    Task<IEnumerable<UserAccount>> GetAllUsersAsync();
    Task UpdateDeviceStatusAsync(string deviceId, string status, string commandId);
    Task<IEnumerable<DeviceStatus>> GetDeviceStatusesAsync();
}

public sealed class UserService : IUserService
{
    private readonly string _connectionString;
    private readonly IConfiguration _configuration;
    private readonly ILogger<UserService> _logger;
    private readonly PasswordHasher<UserAccount> _hasher = new();
    private static readonly List<UserAccount> DemoUsers = new();
    private static readonly List<DeviceStatus> DemoDevices = new();
    private static readonly object DemoLock = new();

    public UserService(IConfiguration configuration, ILogger<UserService> logger)
    {
        _configuration = configuration;
        _connectionString = configuration.GetConnectionString("AzureSql") ?? string.Empty;
        _logger = logger;
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            lock (DemoLock)
            {
                if (DemoUsers.Count == 0)
                {
                    AddDemoUser("admin", "Admin@123", "Admin", "Demo Administrator");
                    AddDemoUser("farmer1", "Farmer@123", "Farmer", "Demo Farmer");
                    DemoDevices.Add(new() { Id=1, DeviceId="IRR-001", FieldId="Field-A" });
                    DemoDevices.Add(new() { Id=2, DeviceId="IRR-002", FieldId="Field-B" });
                }
            }
        }
    }

    private void AddDemoUser(string username, string password, string role, string fullName)
    {
        var user = new UserAccount { Username=username, Role=role, FullName=fullName };
        user.PasswordHash = _hasher.HashPassword(user, password);
        DemoUsers.Add(user);
    }

    public async Task InitializeDatabaseAsync()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            _logger.LogWarning("AzureSql connection string is not configured. Demo in-memory mode is active; no cloud database is connected.");
            return;
        }

        const string sql = @"
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.Users(
  Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
  Username NVARCHAR(50) NOT NULL,
  PasswordHash NVARCHAR(256) NOT NULL,
  Role NVARCHAR(20) NOT NULL,
  FullName NVARCHAR(100) NOT NULL,
  CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT SYSUTCDATETIME(),
  IsActive BIT NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT 1,
  CONSTRAINT UQ_Users_Username UNIQUE(Username),
  CONSTRAINT CK_Users_Role CHECK(Role IN ('Farmer','Agronomist','Admin'))
 );
END;
IF OBJECT_ID(N'dbo.DeviceStatus', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.DeviceStatus(
  Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DeviceStatus PRIMARY KEY,
  DeviceId NVARCHAR(50) NOT NULL,
  FieldId NVARCHAR(50) NOT NULL,
  Status NVARCHAR(30) NOT NULL CONSTRAINT DF_DeviceStatus_Status DEFAULT 'Idle',
  LastUpdated DATETIME2 NOT NULL CONSTRAINT DF_DeviceStatus_LastUpdated DEFAULT SYSUTCDATETIME(),
  LastCommandId NVARCHAR(50) NULL,
  CONSTRAINT UQ_DeviceStatus_DeviceId UNIQUE(DeviceId)
 );
END;
IF NOT EXISTS(SELECT 1 FROM dbo.DeviceStatus WHERE DeviceId='IRR-001')
 INSERT dbo.DeviceStatus(DeviceId,FieldId,Status) VALUES('IRR-001','Field-A','Idle');
IF NOT EXISTS(SELECT 1 FROM dbo.DeviceStatus WHERE DeviceId='IRR-002')
 INSERT dbo.DeviceStatus(DeviceId,FieldId,Status) VALUES('IRR-002','Field-B','Idle');";
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();

        // Optional one-time bootstrap account. Supply via User Secrets locally or App Service settings.
        var bootstrapUser = _configuration["BootstrapAdmin:Username"];
        var bootstrapPassword = _configuration["BootstrapAdmin:Password"];
        if (!string.IsNullOrWhiteSpace(bootstrapUser) && !string.IsNullOrWhiteSpace(bootstrapPassword))
        {
            var existing = await GetUserByUsernameAsync(bootstrapUser);
            if (existing is null)
            {
                var model = new RegisterViewModel { Username=bootstrapUser, Password=bootstrapPassword, ConfirmPassword=bootstrapPassword, FullName="System Administrator", Role="Admin" };
                await RegisterAsync(model);
                _logger.LogInformation("Configured bootstrap administrator account created.");
            }
        }
        _logger.LogInformation("Azure SQL schema is ready.");
    }

    private async Task<UserAccount?> GetUserByUsernameAsync(string username)
    {
        const string sql = "SELECT TOP(1) Id,Username,PasswordHash,Role,FullName,CreatedAt,IsActive FROM dbo.Users WHERE Username=@u";
        await using var c = new SqlConnection(_connectionString); await c.OpenAsync();
        await using var cmd = new SqlCommand(sql,c); cmd.Parameters.Add("@u",SqlDbType.NVarChar,50).Value=username;
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? MapUser(r, true) : null;
    }

    public async Task<UserAccount?> AuthenticateAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            lock(DemoLock)
            {
                var demo=DemoUsers.FirstOrDefault(x=>x.Username.Equals(username,StringComparison.OrdinalIgnoreCase)&&x.IsActive);
                return demo is not null && _hasher.VerifyHashedPassword(demo,demo.PasswordHash,password)!=PasswordVerificationResult.Failed ? demo : null;
            }
        }
        var user=await GetUserByUsernameAsync(username);
        if(user is null || !user.IsActive) return null;
        var result=_hasher.VerifyHashedPassword(user,user.PasswordHash,password);
        var legacyMatch = result == PasswordVerificationResult.Failed &&
            CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(user.PasswordHash),
                Encoding.UTF8.GetBytes(LegacyHashPassword(password)));
        if(result==PasswordVerificationResult.Failed && !legacyMatch) return null;
        if(result==PasswordVerificationResult.SuccessRehashNeeded || legacyMatch)
        {
            // Upgrade legacy SHA-256 hashes (from the starter ZIP) after a successful login.
            user.PasswordHash=_hasher.HashPassword(user,password);
            await using var c=new SqlConnection(_connectionString); await c.OpenAsync();
            await using var cmd=new SqlCommand("UPDATE dbo.Users SET PasswordHash=@p WHERE Id=@id",c);
            cmd.Parameters.Add("@p",SqlDbType.NVarChar,256).Value=user.PasswordHash;
            cmd.Parameters.Add("@id",SqlDbType.Int).Value=user.Id; await cmd.ExecuteNonQueryAsync();
        }
        return user;
    }

    public async Task<bool> RegisterAsync(RegisterViewModel model)
    {
        var role=model.Role;
        if(role is not ("Farmer" or "Agronomist" or "Admin")) throw new ArgumentException("Unsupported role.");
        var user=new UserAccount { Username=model.Username.Trim(), Role=role, FullName=model.FullName.Trim() };
        user.PasswordHash=_hasher.HashPassword(user,model.Password);
        if(string.IsNullOrWhiteSpace(_connectionString))
        {
            lock(DemoLock)
            {
                if(DemoUsers.Any(x=>x.Username.Equals(user.Username,StringComparison.OrdinalIgnoreCase))) return false;
                user.Id=DemoUsers.Count+1; DemoUsers.Add(user); return true;
            }
        }
        const string sql="INSERT dbo.Users(Username,PasswordHash,Role,FullName) VALUES(@u,@p,@r,@n)";
        try
        {
            await using var c=new SqlConnection(_connectionString); await c.OpenAsync();
            await using var cmd=new SqlCommand(sql,c);
            cmd.Parameters.Add("@u",SqlDbType.NVarChar,50).Value=user.Username;
            cmd.Parameters.Add("@p",SqlDbType.NVarChar,256).Value=user.PasswordHash;
            cmd.Parameters.Add("@r",SqlDbType.NVarChar,20).Value=user.Role;
            cmd.Parameters.Add("@n",SqlDbType.NVarChar,100).Value=user.FullName;
            await cmd.ExecuteNonQueryAsync(); return true;
        }
        catch(SqlException ex) when(ex.Number is 2601 or 2627){return false;}
    }

    public async Task<IEnumerable<UserAccount>> GetAllUsersAsync()
    {
        if(string.IsNullOrWhiteSpace(_connectionString))
        {
            lock(DemoLock) return DemoUsers.Select(u => new UserAccount { Id=u.Id, Username=u.Username, Role=u.Role, FullName=u.FullName, CreatedAt=u.CreatedAt, IsActive=u.IsActive }).ToList();
        }
        var items=new List<UserAccount>();
        await using var c=new SqlConnection(_connectionString); await c.OpenAsync();
        await using var cmd=new SqlCommand("SELECT Id,Username,PasswordHash,Role,FullName,CreatedAt,IsActive FROM dbo.Users ORDER BY Username",c);
        await using var r=await cmd.ExecuteReaderAsync(); while(await r.ReadAsync()) items.Add(MapUser(r,false)); return items;
    }

    public async Task UpdateDeviceStatusAsync(string deviceId,string status,string commandId)
    {
        if(string.IsNullOrWhiteSpace(_connectionString))
        { lock(DemoLock){var d=DemoDevices.FirstOrDefault(x=>x.DeviceId==deviceId);if(d!=null){d.Status=status;d.LastUpdated=DateTime.UtcNow;d.LastCommandId=commandId;}} return; }
        const string sql="UPDATE dbo.DeviceStatus SET Status=@s,LastUpdated=SYSUTCDATETIME(),LastCommandId=@c WHERE DeviceId=@d";
        await using var c=new SqlConnection(_connectionString);await c.OpenAsync();await using var cmd=new SqlCommand(sql,c);
        cmd.Parameters.Add("@s",SqlDbType.NVarChar,30).Value=status;cmd.Parameters.Add("@c",SqlDbType.NVarChar,50).Value=commandId;cmd.Parameters.Add("@d",SqlDbType.NVarChar,50).Value=deviceId;
        if(await cmd.ExecuteNonQueryAsync()==0) throw new InvalidOperationException($"Device '{deviceId}' does not exist.");
    }

    public async Task<IEnumerable<DeviceStatus>> GetDeviceStatusesAsync()
    {
        if(string.IsNullOrWhiteSpace(_connectionString)){lock(DemoLock)return DemoDevices.ToList();}
        var items=new List<DeviceStatus>();await using var c=new SqlConnection(_connectionString);await c.OpenAsync();
        await using var cmd=new SqlCommand("SELECT Id,DeviceId,FieldId,Status,LastUpdated,LastCommandId FROM dbo.DeviceStatus ORDER BY DeviceId",c);
        await using var r=await cmd.ExecuteReaderAsync();while(await r.ReadAsync())items.Add(new DeviceStatus{Id=r.GetInt32(0),DeviceId=r.GetString(1),FieldId=r.GetString(2),Status=r.GetString(3),LastUpdated=r.GetDateTime(4),LastCommandId=r.IsDBNull(5)?string.Empty:r.GetString(5)});return items;
    }

    private static string LegacyHashPassword(string password)
    {
        using var sha = SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(password + "AgriStreamSalt2026")));
    }

    private static UserAccount MapUser(SqlDataReader r,bool includeHash)=>new(){Id=r.GetInt32(0),Username=r.GetString(1),PasswordHash=includeHash?r.GetString(2):string.Empty,Role=r.GetString(3),FullName=r.IsDBNull(4)?string.Empty:r.GetString(4),CreatedAt=r.GetDateTime(5),IsActive=r.GetBoolean(6)};
}
