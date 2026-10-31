namespace DevHub.Domain.Common;

/// <summary>Small embedded raster images; active formats and remote URLs are not accepted.</summary>
public static class AvatarImage
{
    public const int MaxBytes = 131072;
    public const int MaxLength = 180000;

    public static bool IsValid(string? value)
    {
        if (value is null) return true;
        if (value.Length > MaxLength) return false;
        var comma = value.IndexOf(',');
        if (comma < 0) return false;
        var header = value[..comma];
        if (header is not ("data:image/png;base64" or "data:image/jpeg;base64" or "data:image/webp;base64")) return false;
        byte[] bytes;
        try {bytes = Convert.FromBase64String(value[(comma + 1)..]);} catch (FormatException) {return false;}
        if (bytes.Length is < 12 or > MaxBytes) return false;
        return header switch
        {
            "data:image/png;base64" => bytes.AsSpan().StartsWith(new byte[] {137,80,78,71,13,10,26,10}),
            "data:image/jpeg;base64" => bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255,
            _ => bytes.AsSpan(0,4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8,4).SequenceEqual("WEBP"u8),
        };
    }
}
