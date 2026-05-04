namespace EBookDashboard.Models.DTO
{
    
        public class ApiResponse<T>
        {
            public bool Success { get; set; }
            public string? Message { get; set; }= string.Empty;
        public T Data { get; set; }= default!;
    }

        public class ApiResponse
        {
            public bool Success { get; set; }
            public string? Message { get; set; }= string.Empty;
    }
    
}
