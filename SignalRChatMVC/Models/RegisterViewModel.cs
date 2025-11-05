using System.ComponentModel.DataAnnotations;

namespace SignalRChatMVC.Models
{
    public class RegisterViewModel
    {
        // Tên hiển thị (DisplayName)
        [Required(ErrorMessage = "Tên hiển thị không được để trống.")]
        [StringLength(50, MinimumLength = 3, ErrorMessage = "Tên hiển thị phải dài từ 3 đến 50 ký tự.")]
        public string DisplayName { get; set; } = string.Empty;

        // Email
        [Required(ErrorMessage = "Địa chỉ Email không được để trống.")]
        [EmailAddress(ErrorMessage = "Địa chỉ Email không hợp lệ.")]
        [StringLength(100, ErrorMessage = "Email không được vượt quá 100 ký tự.")]
        public string Email { get; set; } = string.Empty;

        // Mật khẩu (Password)
        [Required(ErrorMessage = "Mật khẩu không được để trống.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu phải dài ít nhất 6 ký tự.")]
        [DataType(DataType.Password)] // Giúp các helper tag của ASP.NET nhận diện đây là trường mật khẩu
        [Display(Name = "Mật khẩu (Tối thiểu 6 ký tự, phải có chữ hoa, chữ thường, số, và ký tự đặc biệt)")]
        public string Password { get; set; } = string.Empty;

        // Xác nhận Mật khẩu (ConfirmPassword)
        [Required(ErrorMessage = "Vui lòng xác nhận mật khẩu.")]
        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "Mật khẩu và xác nhận mật khẩu không khớp.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}