using System.ComponentModel.DataAnnotations;

namespace SignalRChatMVC.Models
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Vui lòng nhập Email hoặc Tên đăng nhập.")]
        public string Email { get; set; } = string.Empty; // Hoặc Username

        [Required(ErrorMessage = "Vui lòng nhập Mật khẩu.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }
}
