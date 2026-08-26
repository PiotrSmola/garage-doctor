using System.Diagnostics;
using GarageDoctor.Web.Models;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace GarageDoctor.Web.Controllers;

[Route("error")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ErrorController : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        Response.StatusCode = StatusCodes.Status500InternalServerError;

        return View(Build(StatusCodes.Status500InternalServerError));
    }

    [HttpGet("{statusCode:int}")]
    public IActionResult Status(int statusCode)
    {
        var resolved = statusCode is >= 400 and <= 599
            ? statusCode
            : StatusCodes.Status404NotFound;

        Response.StatusCode = resolved;

        var model = Build(resolved);

        return resolved == StatusCodes.Status404NotFound
            ? View("NotFound", model)
            : View("Index", model);
    }

    private ErrorViewModel Build(int statusCode) => new()
    {
        StatusCode = statusCode,
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
        RequestedPath = HttpContext.Features.Get<IStatusCodeReExecuteFeature>()?.OriginalPath
            ?? HttpContext.Features.Get<IExceptionHandlerPathFeature>()?.Path
    };
}
