namespace EBookDashboard.Models.DTO
{
    public class UpdateUserRequest
    {
        public int UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string UserEmail { get; set; } = string.Empty;
        public int? RoleId { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
