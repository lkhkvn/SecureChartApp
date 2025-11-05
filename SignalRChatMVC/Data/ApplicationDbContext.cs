using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SignalRChatMVC.Models;

namespace SignalRChatMVC.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }
        public DbSet<ChatGroup> ChatGroups { get; set; }
        public DbSet<ChatMessage> ChatMessages { get; set; }
    }

    public class ApplicationUser : IdentityUser
    {
        // Bạn có thể thêm thuộc tính mở rộng như tên hiển thị, avatar...
        public string? DisplayName { get; set; }
    }
}
