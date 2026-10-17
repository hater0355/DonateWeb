using Microsoft.AspNetCore.Mvc;

namespace DonateWeb.Controllers
{
    public class CouponsController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
