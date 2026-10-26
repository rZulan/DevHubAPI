using System.Text;

namespace DevHub.Infrastructure.BugReports;

public static class BugFileValidation
{
    public const int MaxFiles = 5, MaxFileBytes = 10 * 1024 * 1024, MaxTotalBytes = 25 * 1024 * 1024;
    public static string SafeName(string value) => new string(value.Replace('\\', '/').Split('/').Last().Where(c => !char.IsControl(c)).ToArray()).Trim();
    public static string? ContentType(string fileName, byte[] content)
    {
        if (content.Length is 0 or > MaxFileBytes || fileName.Length is 0 or > 200) return null;
        var ext = Path.GetExtension(fileName).ToLowerInvariant(); var bytes = content.AsSpan();
        bool Starts(byte[] prefix) => content.AsSpan().StartsWith(prefix);
        switch (ext)
        {
            case ".png": return Starts([137, 80, 78, 71, 13, 10, 26, 10]) ? "image/png" : null;
            case ".jpg": case ".jpeg": return Starts([255, 216, 255]) ? "image/jpeg" : null;
            case ".gif": return bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8) ? "image/gif" : null;
            case ".webp": return bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8) ? "image/webp" : null;
            case ".pdf": return bytes.StartsWith("%PDF-"u8) ? "application/pdf" : null;
            case ".zip": return Starts([80, 75, 3, 4]) || Starts([80, 75, 5, 6]) || Starts([80, 75, 7, 8]) ? "application/zip" : null;
            case ".mp4": return bytes.Length >= 12 && bytes.Slice(4, 4).SequenceEqual("ftyp"u8) ? "video/mp4" : null;
            case ".webm": return Starts([26, 69, 223, 163]) ? "video/webm" : null;
            case ".txt": case ".log": case ".csv": case ".json":
                try
                {
                    var text = new UTF8Encoding(false, true).GetString(content);
                    return text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t' or '\f')) ? null : "text/plain";
                }
                catch (DecoderFallbackException) {return null;}
            default: return null;
        }
    }
}
