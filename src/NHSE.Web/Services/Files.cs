using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace NHSE.Web.Services;

/// <summary>Reading uploaded files and handing files to the browser, for NHSE's export and import formats.</summary>
public static class Files
{
    /// <summary>Reads an uploaded file up to <paramref name="max"/> bytes; null when it is bigger.</summary>
    public static async Task<byte[]?> Read(IBrowserFile file, long max)
    {
        if (file.Size > max)
            return null;
        await using var stream = file.OpenReadStream(max);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    public static async Task Save(SaveSession session, string name, byte[] data)
    {
        var js = await session.Js();
        await js.InvokeVoidAsync("download", name, data, "application/octet-stream");
    }

    /// <summary>A file name without characters that Windows or phones refuse.</summary>
    public static string Safe(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']))
            name = name.Replace(c, '_');
        return name.Trim();
    }
}
