﻿﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EBookDashboard.Interfaces;
using Microsoft.EntityFrameworkCore;
using EBookDashboard.Models;

namespace EBookDashboard.Controllers
{
    [Authorize(Roles = "Reader")] // Only Readers can access
    public class ReaderController : Controller
    {
        private readonly IPlansService _planService;
        private readonly ApplicationDbContext _context;

        public ReaderController(IPlansService planService, ApplicationDbContext context)
        {
            _planService = planService;
            _context = context;
        }

        public IActionResult Dashboard()
        {
            return View();  // will look for Views/Reader/Dashboard.cshtml
        }

        public async Task<IActionResult> Plans()
        {
            var plans = await _planService.GetActivePlansAsync();
            return View(plans);
        }

        public IActionResult Library()
        {
            return View();
        }

        public IActionResult Settings()
        {
            return View();
        }
    }
}
