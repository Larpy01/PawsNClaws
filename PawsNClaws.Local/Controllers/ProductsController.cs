using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PawsNClaws.Application.Features.Products.DTOs;
using PawsNClaws.Application.Features.Products.Interfaces;
using PawsNClaws.Infastructure.Identity.Local;
using PawsNClaws.Local.Models.Products;

namespace PawsNClaws.Local.Controllers;

[Authorize]
public sealed class ProductsController : Controller
{
    private readonly IProductService _products;

    public ProductsController(IProductService products)
    {
        _products = products;
    }

    [HttpGet]
    [Authorize(Policy = Permissions.ProductsView)]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        var products = await _products.GetAllAsync(
            cancellationToken);

        return View(new ProductListModel
        {
            Products = products
        });
    }

    [HttpGet]
    [Authorize(Policy = Permissions.ProductsCreate)]
    public IActionResult Create()
    {
        return View(new ProductFormModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.ProductsCreate)]
    public async Task<IActionResult> Create(
        ProductFormModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(model);

        var request = new CreateProductRequest(
            model.SKU,
            model.Name,
            model.Price);

        var result = await _products.CreateAsync(
            request, cancellationToken);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error);

            return View(model);
        }

        TempData["SuccessMessage"] =
            "Product created successfully.";

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = Permissions.ProductsUpdate)]
    public async Task<IActionResult> Edit(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
            return NotFound();

        var product = await _products.GetByIdAsync(
            id, cancellationToken);

        if (product is null)
            return NotFound();

        return View(new ProductFormModel
        {
            Id = product.Id,
            SKU = product.SKU,
            Name = product.Name,
            Price = product.Price,
            IsActive = product.IsActive
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Permissions.ProductsUpdate)]
    public async Task<IActionResult> Edit(
        Guid id,
        ProductFormModel model,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty || id != model.Id)
            return BadRequest();

        if (!ModelState.IsValid)
            return View(model);

        var request = new UpdateProductRequest(
            id,
            model.SKU,
            model.Name,
            model.Price,
            model.IsActive);

        var result = await _products.UpdateAsync(
            request, cancellationToken);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error);

            return View(model);
        }

        TempData["SuccessMessage"] =
            "Product updated successfully.";

        return RedirectToAction(nameof(Index));
    }
}
