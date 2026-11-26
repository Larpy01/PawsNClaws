using System.Security.Cryptography;
using PawsNClaws.Application.Features.Inventory;
using SkiaSharp;

namespace PawsNClaws.Infastructure.Features.Inventory;

public sealed class InventoryEvidenceStore(string root)
{
    private const int MaxBytes = 5 * 1024 * 1024;
    private readonly string _root = Path.GetFullPath(root);

    public async Task<EvidenceFile> SaveAsync(Stream source, CancellationToken cancellationToken)
    {
        using var data = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (data.Length + count > MaxBytes)
                throw new InventoryRuleException("Each photo must be 5 MiB or smaller.");
            data.Write(buffer, 0, count);
        }

        data.Position = 0;
        using var codec = SKCodec.Create(new SKManagedStream(data, false));
        var contentType = codec?.EncodedFormat switch
        {
            SKEncodedImageFormat.Jpeg => "image/jpeg",
            SKEncodedImageFormat.Png => "image/png",
            _ => throw new InventoryRuleException("The photo is not a valid JPEG or PNG image.")
        };
        if (codec!.Info.Width <= 0 || codec.Info.Height <= 0 ||
            (long)codec.Info.Width * codec.Info.Height > 20_000_000)
            throw new InventoryRuleException("Each photo must be 20 megapixels or smaller.");
        using var image = new SKBitmap(codec.Info);
        if (codec.GetPixels(image.Info, image.GetPixels()) != SKCodecResult.Success)
            throw new InventoryRuleException("The photo is incomplete or invalid.");

        Directory.CreateDirectory(_root);
        var key = Guid.NewGuid().ToString("N");
        var path = Path.Combine(_root, key);
        try
        {
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            data.Position = 0;
            await data.CopyToAsync(file, cancellationToken);
            await file.FlushAsync(cancellationToken);
        }
        catch
        {
            File.Delete(path);
            throw;
        }

        return new EvidenceFile(key, contentType, data.Length,
            Convert.ToHexString(SHA256.HashData(data.GetBuffer().AsSpan(0, (int)data.Length))));
    }

    public Stream? Open(string key)
    {
        if (!Guid.TryParseExact(key, "N", out _))
            return null;
        var path = Path.Combine(_root, key);
        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    internal void DeleteUncommitted(string key) => File.Delete(Path.Combine(_root, key));
}
