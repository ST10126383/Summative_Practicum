using AgriStreamIoT.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddSingleton<IAzureStorageService, AzureStorageService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddHttpClient<IFunctionClientService, FunctionClientService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// A configured Azure SQL connection must work: do not silently hide cloud failures.
using (var scope = app.Services.CreateScope())
{
    var users = scope.ServiceProvider.GetRequiredService<IUserService>();
    var storage = scope.ServiceProvider.GetRequiredService<IAzureStorageService>();
    await users.InitializeDatabaseAsync();
    await storage.EnsureContainersAndTablesAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.Run();
