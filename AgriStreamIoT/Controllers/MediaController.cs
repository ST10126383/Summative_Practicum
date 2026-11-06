using AgriStreamIoT.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriStreamIoT.Controllers
{
    [Authorize]
    public class MediaController : Controller
    {
        private readonly IAzureStorageService _storage;
        private readonly ILogger<MediaController> _logger;

        public MediaController(IAzureStorageService storage, ILogger<MediaController> logger)
        {
            _storage = storage;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Upload() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(50_000_000)] // 50 MB
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                ModelState.AddModelError("", "Please select a drone image or media file.");
                return View();
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".tif", ".tiff" };
            var allowedTypes = new[] { "image/jpeg", "image/png", "image/tiff" };
            if (!allowedExtensions.Contains(extension) || !allowedTypes.Contains(file.ContentType.ToLowerInvariant()))
            {
                ModelState.AddModelError("", "Only image files (JPEG, PNG, TIFF) are accepted.");
                return View();
            }

            try
            {
                await using var stream = file.OpenReadStream();
                var url = await _storage.UploadDroneImageAsync(stream, file.FileName, file.ContentType, User.Identity?.Name ?? "unknown");
                TempData["Success"] = $"Image uploaded successfully. Blob URL: {url}. The Blob-triggered Function will process the audit entry.";
                return RedirectToAction("Index", "Dashboard");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Upload failed");
                ModelState.AddModelError("", $"Upload failed: {ex.Message}");
                return View();
            }
        }
    }
}
