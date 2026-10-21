using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MickMouse.Web.ViewModels;

namespace MickMouse.Web.Controllers;

public sealed class HomeController : Controller
{
    [IgnoreAntiforgeryToken]
    public IActionResult Error()
    {
        Response.StatusCode = 500;
        return View("~/Views/Shared/Error.cshtml", new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
            Message = "The request could not be completed. Check your connection or try again shortly."
        });
    }

    [IgnoreAntiforgeryToken]
    public IActionResult Status(int code)
    {
        Response.StatusCode = code;
        return View("~/Views/Shared/Error.cshtml", new ErrorViewModel
        {
            StatusCode = code,
            Title = code == 404 ? "This page is off the trail." : code == 429 ? "A short pause, please." : "The request could not be completed.",
            Message = code == 404 ? "The episode or page may have moved, or it is not published yet." :
                code == 429 ? "Too many requests arrived at once. Try again in a minute." : "Return to browsing and try again."
        });
    }
}
