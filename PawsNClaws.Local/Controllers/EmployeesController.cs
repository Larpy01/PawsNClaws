using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PawsNClaws.Application.Features.Employees.DTOs;
using PawsNClaws.Application.Features.Employees.Interfaces;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Local.Models.Employees;

namespace PawsNClaws.Local.Controllers;

[Authorize]
public sealed class EmployeesController : Controller
{
    private readonly IEmployeeManagementService _employees;
    private readonly UserManager<LocalApplicationUser> _users;

    public EmployeesController(
        IEmployeeManagementService employees,
        UserManager<LocalApplicationUser> users)
    {
        _employees = employees;
        _users = users;
    }


    [HttpGet]
    [Authorize(Policy = Permissions.UsersView)]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        var employees = await _employees.GetAllAsync(
            cancellationToken);

        return View(new EmployeeListModel
        {
            Employees = employees
        });
    }

    [HttpGet]
    [Authorize(Policy = Permissions.UsersCreate)]
    [Authorize(Roles = LocalRoles.SuperAdmin + "," + LocalRoles.Admin)]
    public IActionResult Create()
    {
        return View(new CreateEmployeeModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.UsersCreate)]
    [Authorize(Roles = LocalRoles.SuperAdmin + "," + LocalRoles.Admin)]
    public async Task<IActionResult> Create(
        CreateEmployeeModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(model);


        var actingUserId = _users.GetUserId(User);

        if (!Guid.TryParse(actingUserId, out var actorId))
        {
            return Challenge();
        }

        var request = new CreateEmployeeRequest(
            model.FirstName,
            model.LastName,
            model.Email,
            model.Password,
            model.Role,
            actorId);



        var result = await _employees.CreateAsync(
            request,
            cancellationToken);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return View(model);
        }

        TempData["SuccessMessage"] =
            "Employee account created successfully.";

        return RedirectToAction(nameof(Index));
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.UsersChangeStatus)]
    [Authorize(Roles =
        LocalRoles.SuperAdmin + "," + LocalRoles.Admin)]
    public async Task<IActionResult> SetStatus(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken)
    {
        var actingUserId = _users.GetUserId(User);

        if (!Guid.TryParse(actingUserId, out var actorId))
            return Challenge();

        var result = await _employees.SetActiveStatusAsync(
            id,
            actorId,
            isActive,
            cancellationToken);

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] =
                string.Join(" ", result.Errors);
        }
        else
        {
            TempData["SuccessMessage"] =
                isActive
                    ? "Employee activated successfully."
                    : "Employee deactivated successfully.";
        }

        return RedirectToAction(nameof(Index));
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.UsersAssignRoles)]
    [Authorize(Roles = LocalRoles.SuperAdmin + "," + LocalRoles.Admin)]
    public async Task<IActionResult> ChangeRole(
        Guid id,
        string newRole,
        CancellationToken cancellationToken)
    {
        var actingUserId = _users.GetUserId(User);

        if (!Guid.TryParse(actingUserId, out var actorId))
            return Challenge();

        var request = new ChangeEmployeeRoleRequest(
            id,
            actorId,
            newRole);

        var result = await _employees.ChangeRoleAsync(
            request,
            cancellationToken);

        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] =
                string.Join(" ", result.Errors);
        }
        else
        {
            TempData["SuccessMessage"] =
                "Employee role updated successfully.";
        }

        return RedirectToAction(nameof(Index));
    }


}
