namespace FluidBar.Island;

/// <summary>Presentation-only file facts loaded by the host, never by the render loop.</summary>
public sealed record FileVisualMetadata
{
    public string TypeLabel { get; init; } = "FILE";
    public FileVisualKind Kind { get; init; }
    public string Directory { get; init; } = string.Empty;
    public string? SizeText { get; init; }
    public byte[]? ThumbnailPng { get; init; }
    public string? ImageKey { get; init; }
    public bool Exists { get; init; }

    /// <summary>Path-only fallback, safe to construct before asynchronous file access finishes.</summary>
    public static FileVisualMetadata FromPath(string path)
    {
        var extension = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        return new()
        {
            TypeLabel = extension.Length is > 0 and <= 8 ? extension : "FILE",
            Directory = Path.GetDirectoryName(path) ?? string.Empty,
            Kind = extension switch
            {
                "PNG" or "JPG" or "JPEG" or "WEBP" or "GIF" or "BMP" => FileVisualKind.Image,
                "ZIP" or "7Z" or "RAR" or "TAR" or "GZ" => FileVisualKind.Archive,
                "MP3" or "WAV" or "FLAC" or "M4A" or "OGG" => FileVisualKind.Audio,
                "MP4" or "MOV" or "MKV" or "WEBM" or "AVI" => FileVisualKind.Video,
                "CS" or "JS" or "TS" or "JSON" or "PY" or "HTML" or "CSS" or "MD" => FileVisualKind.Code,
                "PDF" => FileVisualKind.Pdf,
                _ => FileVisualKind.Document,
            },
        };
    }
}

public enum FileVisualKind { Document, Image, Pdf, Archive, Audio, Video, Code, Folder }
