using PawsNClaws.Application.Features.Products.DTOs;

namespace PawsNClaws.Local.Models.Products;

public sealed class ProductListModel
{
    public IReadOnlyList<ProductDto> Products { get; set; }
        = Array.Empty<ProductDto>();
}
