using System;
using System.Collections.Generic;

namespace EBookDashboard.Models.ViewModels
{
    public class UserBooksViewModel
    {
        public int UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public List<UserBook> Books { get; set; } = new List<UserBook>();
    }

    public class UserBook
    {
        public int BookId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Genre { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public int TotalChapters { get; set; }
        public int TotalWords { get; set; }
        public List<UserChapter> Chapters { get; set; } = new List<UserChapter>();
        public string CoverImagePath { get; set; } = string.Empty;
    }

    public class UserChapter
    {
        public int ChapterId { get; set; }
        public int ChapterNumber { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public int WordCount { get; set; }
        public string PreviewContent { get; set; } = string.Empty;
    }
}
