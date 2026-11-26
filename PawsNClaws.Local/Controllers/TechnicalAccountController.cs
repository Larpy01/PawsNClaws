using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Local.Models.Authentication;

namespace PawsNClaws.Local.Controllers;

[Route("system-admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TechnicalAccountController(TechnicalAuthenticationService authentication) : Controller
{
    internal const string Scheme = "PawsNClaws.Technical";

    [HttpGet("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login()
    {
        var result = await HttpContext.AuthenticateAsync(Scheme);
        return result.Succeeded
            ? RedirectToAction("Index", "TechnicalDashboard")
            : View(new TechnicalLoginModel());
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(TechnicalLoginModel model)
    {
        if (ModelState.IsValid)
        {
            var principal = await authentication.SignInAsync(
                model.Email, model.Password, model.Code, model.UseRecoveryCode);
            if (principal is not null)
            {
                await HttpContext.SignInAsync(Scheme, principal);
                return RedirectToAction("Index", "TechnicalDashboard");
            }

        }

        model.Code = model.Password = "";
        ModelState.Remove(nameof(model.Code));
        ModelState.Remove(nameof(model.Password));
        ModelState.AddModelError(string.Empty, "Unable to sign in. Check your credentials and code.");
        return View(model);
    }

    [HttpPost("logout")]
    [Authorize(AuthenticationSchemes = Scheme)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(Scheme);
        return RedirectToAction(nameof(Login));
    }
}
