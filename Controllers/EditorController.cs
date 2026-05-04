using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EBookDashboard.Controllers
{
          [Authorize(Roles = "Editor")] // Only Editors can access
        public class EditorController : Controller
        {
            public IActionResult Dashboard()
            {
                return View();    // Views/Editor/Dashboard.cshtml
        }
        }
  
}
