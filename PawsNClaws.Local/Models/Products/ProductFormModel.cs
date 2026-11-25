using System.ComponentModel.DataAnnotations;

namespace PawsNClaws.Local.Models.Products;

public sealed class ProductFormModel
{
    public Guid Id { get; set; }

    [Required]
    [StringLength(50)]
    [Display(Name = "SKU")]
    public string SKU { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    [Display(Name = "Product Name")]
    public string Name { get; set; } = string.Empty;

    [Range(typeof(decimal), "0", "9999999999999999.99")]
    [Display(Name = "Selling Price (PHP)")]
    public decimal Price { get; set; }

    public bool IsActive { get; set; } = true;
}
