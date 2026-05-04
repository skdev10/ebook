using EBookDashboard.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace EBookDashboard.Controllers
{
    public class CoverDesignController : Controller
    {
       // private readonly ILogger<HomeController> _logger;
        private readonly IWebHostEnvironment _env;
        private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

        public CoverDesignController(ILogger<HomeController> logger, IWebHostEnvironment env)
        {
            //_logger = logger;
            _env = env;
        }
         



        public IActionResult CoverDesignTemplate(string? category = null)
        {
            var bookCoversPath = Path.Combine(_env.ContentRootPath, "Images", "book_covers");
            var categories = new List<string>();
            var allImagePaths = new List<string>();

            if (Directory.Exists(bookCoversPath))
            {
                foreach (var dir in Directory.EnumerateDirectories(bookCoversPath))
                {
                    categories.Add(Path.GetFileName(dir));
                }
                categories = categories.OrderBy(c => c).ToList();

                foreach (var file in Directory.EnumerateFiles(bookCoversPath, "*.*", SearchOption.AllDirectories))
                {
                    if (ImageExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                    {
                        var relativePath = Path.GetRelativePath(bookCoversPath, file).Replace('\\', '/');
                        allImagePaths.Add(relativePath);
                    }
                }
            }

            var imagePaths = string.IsNullOrEmpty(category)
                ? allImagePaths
                : allImagePaths.Where(p => p.StartsWith(category + "/", StringComparison.OrdinalIgnoreCase)).ToList();

            var viewModel = new BookCoversViewModel
            {
                Categories = categories,
                SelectedCategory = category,
                ImagePaths = imagePaths
            };
            return View("CoverDesignTemplate", viewModel);
        }
            

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

    }
}
