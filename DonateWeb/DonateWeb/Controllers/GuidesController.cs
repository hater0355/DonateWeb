using Microsoft.AspNetCore.Mvc;

namespace DonateWeb.Controllers
{
    public class GuidesController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
