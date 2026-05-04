﻿﻿﻿﻿﻿﻿﻿﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Controllers
{
    [Authorize(Roles = "Author")] // Only Authors can access
    public class AuthorController : Controller
    {
        private readonly IUserService _userService;
        private readonly IBookService _bookService;
        private readonly ApplicationDbContext _context;

        public AuthorController(IUserService userService, IBookService bookService, ApplicationDbContext context)
        {
            _userService = userService;
            _bookService = bookService;
            _context = context;
        }

        public async Task<IActionResult> Dashboard()
        {
            // Get the current user's information
            var userEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
            var user = await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.UserEmail == userEmail);
            
            if (user == null)
            {
                return RedirectToAction("UserLogin", "Account");
            }

            // Get author information - using UserId to find the author
            var author = await _context.Authors
                .FirstOrDefaultAsync(a => a.AuthorCode == user.UserId.ToString());
            
            // If no author record exists, we'll create basic dashboard info from user data
            if (author == null)
            {
                // Use user data for dashboard if no specific author record
                ViewBag.AuthorName = user.FullName;
                ViewBag.PublishedBooks = 0;
                ViewBag.DraftBooks = 0;
                ViewBag.TotalBooks = 0;
            }
            else
            {
                // Get author's books
                var books = await _context.Books
                    .Where(b => b.AuthorId == author.AuthorId)
                    .ToListAsync();

                // Calculate statistics
                var publishedBooks = books.Count(b => b.Status == "Published");
                var draftBooks = books.Count(b => b.Status == "Draft");
                var totalBooks = books.Count;

                // Pass data to view
                ViewBag.AuthorName = author.FullName ?? user.FullName;
                ViewBag.PublishedBooks = publishedBooks;
                ViewBag.DraftBooks = draftBooks;
                ViewBag.TotalBooks = totalBooks;
            }
            //return RedirectToAction("Author", "Dashboard");

            return View(); // Views/Author/Dashboard.cshtml
        }

        // GET: /Author/Books
        public IActionResult Books()
        {
            //// Get the current user's author information
            //var user = await _context.Users
            //    .FirstOrDefaultAsync(u => u.UserEmail == User.Identity.Name);
            
            //if (user == null)
            //{
            //    return RedirectToAction("UserLogin", "Account");
            //}

            //var author = await _context.Authors
            //    .FirstOrDefaultAsync(a => a.AuthorCode == user.UserId.ToString());
            
            //if (author == null)
            //{
            //    return RedirectToAction("UserLogin", "Account");
            //}

            //// Get author's books
            //var books = await _context.Books
            //    .Include(b => b.Chapters)
            //    .Where(b => b.AuthorId == author.AuthorId)
            //    .ToListAsync();

            return View();
        }
        public IActionResult WriteNew()
        {
            return View();
        }

        // GET: /Author/Analytics
        public IActionResult Analytics()
        {
            return View();
        }

        // GET: /Author/Settings
        public IActionResult Settings()
        {
            return View();
        }

        // GET: /Author/Editor
        public IActionResult Editor()
        {
            return View();
        }

        // GET: /Author/Publishing
        public IActionResult Publishing()
        {
            return View();
        }

        // GET: /Author/Royalties
        public IActionResult Royalties()
        {
            return View();
        }

        // GET: /Author/Marketing
        public IActionResult Marketing()
        {
            return View();
        }

        // GET: /Author/Orders
        public IActionResult Orders()
        {
            return View();
        }

        // GET: /Author/Copyright
        public IActionResult Copyright()
        {
            return View();
        }

        [HttpPost]
        public IActionResult SaveDocument([FromBody] DocumentModel document)
        {
            // Add your document saving logic here
            return Json(new { success = true, message = "Document saved successfully" });
        }

        [HttpPost]
        public IActionResult AISuggestion([FromBody] AISuggestionRequest request)
        {
            // Add your AI suggestion logic here
            return Json(new { suggestion = "AI suggestion would go here" });
        }
    }

    public class DocumentModel
    {
        public string? Title { get; set; } = string.Empty;
        public string? Content { get; set; } = string.Empty;
        public string? DocumentId { get; set; } = string.Empty;
    }

    public class AISuggestionRequest
    {
        public string? Text { get; set; } = string.Empty;
        public string? SuggestionType { get; set; } = string.Empty;
    }
}


