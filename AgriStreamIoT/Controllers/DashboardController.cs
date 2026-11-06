using AgriStreamIoT.Models;
using AgriStreamIoT.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriStreamIoT.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly IFunctionClientService _functions;
        private readonly IAzureStorageService _storage;
        private readonly IUserService _users;

        public DashboardController(IFunctionClientService functions, IAzureStorageService storage, IUserService users)
        {
            _functions = functions;
            _storage = storage;
            _users = users;
        }

        public async Task<IActionResult> Index()
        {
            // Requirement: MVC calls the HTTP GET Function to retrieve latest telemetry
            var telemetry = (await _functions.GetTelemetryFromFunctionAsync()).ToList();
            var audits = (await _storage.GetRecentImageAuditsAsync()).ToList();
            var devices = (await _users.GetDeviceStatusesAsync()).ToList();

            var vm = new DashboardViewModel
            {
                LatestTelemetry = telemetry,
                RecentUploads = audits,
                Devices = devices,
                CurrentUser = User.Identity?.Name ?? "",
                CurrentRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? ""
            };
            return View(vm);
        }

        /// <summary>
        /// Helper endpoint used by load-testing scripts.
        /// </summary>
        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> Health()
        {
            return Json(new { status = "healthy", time = DateTime.UtcNow });
        }
    }
}
