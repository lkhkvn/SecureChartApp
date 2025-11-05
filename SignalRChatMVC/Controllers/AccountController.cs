using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SignalRChatMVC.Data;
using SignalRChatMVC.Models;
using System.Threading.Tasks;

namespace SignalRChatMVC.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;

        public AccountController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
        }

        // GET: /Account/Register
        [HttpGet]
        public IActionResult Register() => View();

        // POST: /Account/Register
        [HttpPost]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (ModelState.IsValid)
            {
                // tao doi tuong nguoi dung moi
                var user = new ApplicationUser { UserName = model.DisplayName, Email = model.Email, DisplayName = model.DisplayName };
                var result = await _userManager.CreateAsync(user, model.Password);
               // sau khi dang ky thanh cong 
                if (result.Succeeded)
                {
                    await _signInManager.SignInAsync(user, isPersistent: false);
                    return RedirectToAction("Index", "Home");
                }
                foreach (var error in result.Errors)
                    ModelState.AddModelError("", error.Description);
            }
            return View(model);
        }

        // GET: /Account/Login
        [HttpGet]
        public IActionResult Login() => View();

        // POST: /Account/Login
        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (ModelState.IsValid)
            {
                var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password,  isPersistent: false,lockoutOnFailure: false);
                if (result.Succeeded)
                    return RedirectToAction("Index", "Home");

                else if (result.IsLockedOut)
                {
                    ModelState.AddModelError(string.Empty, "Tài khoản của bạn đã bị khóa tạm thời do nhập sai quá nhiều lần.");
                }
                else if (result.IsNotAllowed)
                {
                    ModelState.AddModelError(string.Empty, "Bạn chưa được phép đăng nhập. Vui lòng xác nhận Email hoặc liên hệ Quản trị viên.");

                }
                // Trường hợp 3: Tài khoản không được phép đăng nhập (ví dụ: email chưa được xác nhận)
                else if (result.IsNotAllowed)
                {
                    ModelState.AddModelError(string.Empty, "Bạn chưa được phép đăng nhập. Vui lòng xác nhận Email hoặc liên hệ Quản trị viên.");
                }
                else
                {
                    // Giữ thông báo lỗi chung và không chỉ rõ lỗi là do sai tài khoản hay mật khẩu
                    // Điều này là phương pháp bảo mật tiêu chuẩn để ngăn chặn việc dò tìm username.
                    ModelState.AddModelError(string.Empty, "Tài khoản hoặc mật khẩu không hợp lệ. Vui lòng kiểm tra lại.");
                }
                ModelState.AddModelError("", "Sai thông tin đăng nhập");
            }
            return View(model);
        }

        [HttpGet]
        
        
       
        

        // GET: /Account/Logout
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Login");
        }
    }
}
