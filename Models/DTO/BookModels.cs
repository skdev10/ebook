namespace EBookDashboard.Models.DTO
{
    public class BookModels
    {
        public class BookResult
        {
            public BookContent Book { get; set; } = new();
            public HighlightContent Highlights { get; set; } = new();
        }

        public class BookContent
        {
            public string ChapterTitle { get; set; } = string.Empty;
            public List<BookSection> Sections { get; set; } = new();
        }

        public class BookSection
        {
            public string Tag { get; set; } = string.Empty;   // h1, h2, p
            public string Title { get; set; } = string.Empty; // for h1 / h2
            public List<string> Sentences { get; set; } = new();
        }

        public class HighlightContent
        {
            public string ChapterName { get; set; } = string.Empty;
            public List<HighlightItem> Items { get; set; } = new();
        }

        public class HighlightItem
        {
            public string Heading { get; set; } = string.Empty;
            public List<string> Sentences { get; set; } = new();
        }
    }
}
