using Microsoft.AspNetCore.Mvc;

namespace DonateWeb.Controllers
{
    public class EffectsController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
