using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PawsNClaws.Application.Features.Authentication.DTOs;
using PawsNClaws.Application.Features.Authentication.Interfaces;
using PawsNClaws.Local.Models.Authentication;

namespace PawsNClaws.Local.Controllers;

public sealed class AccountController : Controller
{
    private readonly IEmployeeAuthenticationService _auth;

    public AccountController(
        IEmployeeAuthenticationService auth)
    {
        _auth = auth;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Dashboard");

        return View(new LoginViewModel());
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(
        LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var result = await _auth.LoginAsync(
            model.Email,
            model.Password,
            model.RememberMe);

        if (result.Status == EmployeeLoginStatus.Success)
            return RedirectToAction("Index", "Dashboard");

        ModelState.AddModelError(
            string.Empty,
            result.Status == EmployeeLoginStatus.LockedOut
                ? "Account temporarily locked. Try again later."
                : "Unable to sign in. Check your credentials.");

        return View(model);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _auth.LogoutAsync();

        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        return View();
    }

}
