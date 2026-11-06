using AgriStreamIoT.Models;
using AgriStreamIoT.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriStreamIoT.Controllers
{
    [Authorize(Roles = "Farmer,Agronomist,Admin")]
    public class IrrigationController : Controller
    {
        private readonly IFunctionClientService _functions;
        private readonly IUserService _users;

        public IrrigationController(IFunctionClientService functions, IUserService users)
        {
            _functions = functions;
            _users = users;
        }

        [HttpGet]
        public async Task<IActionResult> Trigger()
        {
            ViewBag.Devices = await _users.GetDeviceStatusesAsync();
            return View(new IrrigationCommand());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Trigger(IrrigationCommand model)
        {
            if (string.IsNullOrWhiteSpace(model.DeviceId))
                ModelState.AddModelError(nameof(model.DeviceId), "Device is required.");
            if (model.Action is not ("StartIrrigation" or "StopIrrigation"))
                ModelState.AddModelError(nameof(model.Action), "Select a valid irrigation action.");
            if (model.DurationMinutes is < 1 or > 1440)
                ModelState.AddModelError(nameof(model.DurationMinutes), "Duration must be between 1 and 1440 minutes.");

            var devices = await _users.GetDeviceStatusesAsync();
            if (!devices.Any(d => d.DeviceId == model.DeviceId))
                ModelState.AddModelError(nameof(model.DeviceId), "The selected device does not exist.");
            if (!ModelState.IsValid)
            {
                ViewBag.Devices = devices;
                return View(model);
            }

            model.RequestedBy = User.Identity?.Name ?? "unknown";
            model.RequestedAt = DateTime.UtcNow;
            model.CommandId = Guid.NewGuid().ToString();

            // Requirement: MVC calls the HTTP POST Function which places the message on the Queue.
            // The Queue-triggered Function then updates DeviceStatus in Azure SQL.
            var ok = await _functions.TriggerIrrigationViaFunctionAsync(model);

            if (ok)
            {
                TempData["Success"] = $"Irrigation command {model.CommandId} queued for device {model.DeviceId}. Device status will change after the Queue-triggered Function processes it.";
            }
            else
            {
                TempData["Error"] = "The irrigation Function did not accept the command. Device status was not changed. Check Function App logs.";
            }

            return RedirectToAction("Index", "Dashboard");
        }
    }
}
