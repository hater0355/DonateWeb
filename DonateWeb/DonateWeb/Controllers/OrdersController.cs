using Microsoft.AspNetCore.Mvc;

namespace DonateWeb.Controllers
{
    public class OrdersController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
