using Microsoft.AspNetCore.Authorization;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SignalRChatMVC.Models;

namespace SignalRChatMVC.Controllers
{
    [Authorize] // yeu cau dang nhap de vao chat
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        // TRang chinh sau khi dang nhap 
        public IActionResult Index()
        {
            // lay ten nguoi dung identity
            
            return View();
        }
        // Dang xuat kho he thong 
        

        public IActionResult Privacy()
        {
            return View();
        }

        // ? Thêm action Chat
        public IActionResult Chat()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
