using System.ComponentModel.DataAnnotations;

namespace SignalRChatMVC.Models
{
    public class ChatGroup
    {
        public int Id { get; set; }
        
        [Required]
        [StringLength(50)]
        public string Name { get; set; } = string.Empty;
        
        [StringLength(200)]
        public string Description { get; set; } = string.Empty;
        
        public string? Avatar { get; set; }
        
        [Required]
        public string CreatedBy { get; set; } = string.Empty;
        
        public DateTime CreatedAt { get; set; }
        
        public List<string> Members { get; set; } = new List<string>();

        public List<string> Admins { get; set; } = new List<string>();
    }
}