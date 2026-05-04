using EBookDashboard.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EBookDashboard.Controllers
{
    [Authorize]
    public class ChatController : Controller
    {
        private readonly IChatService _chatService;

        public ChatController(IChatService chatService)
        {
            _chatService = chatService;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> Ask([FromBody] AskRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request?.Prompt))
                return Json(new { success = false, response = "Please enter a question." });

            var response = await _chatService.GetResponseAsync(request.Prompt.Trim(), cancellationToken);
            return Json(new { success = true, response });
        }
    }

    public class AskRequest
    {
        public string? Prompt { get; set; }
    }
}