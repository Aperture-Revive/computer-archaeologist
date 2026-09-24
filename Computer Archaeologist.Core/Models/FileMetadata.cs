using System.Text.Json.Serialization;

namespace ComputerArchaeologist.Core.Models;

/// <summary>
/// Immutable description of a single file that the archaeology pipeline is considering.
/// Everything the rest of the application knows about a file starts here; everything else
/// (interestingness, AI analysis, reports) is derived from it.
/// </summary>
public sealed class FileMetadata
{
    public required string FullPath { get; init; }

    public string FileName { get; init; } = string.Empty;

    /// <summary>Lower-case extension including the leading dot, for example <c>.cs</c>. Empty when the file has none.</summary>
    public string Extension { get; init; } = string.Empty;

    public string DirectoryPath { get; init; } = string.Empty;

    public long SizeBytes { get; init; }

    public DateTimeOffset? CreatedUtc { get; init; }

    public DateTimeOffset? ModifiedUtc { get; init; }

    public DateTimeOffset? AccessedUtc { get; init; }

    public FileAttributes Attributes { get; init; }

    public bool IsHidden => Attributes.HasFlag(FileAttributes.Hidden);

    public bool IsSystem => Attributes.HasFlag(FileAttributes.System);

    public bool IsReadOnly => Attributes.HasFlag(FileAttributes.ReadOnly);

    /// <summary>Set only for candidates that reach the deep-analysis stage; hashing every file would be far too expensive.</summary>
    public string? Sha256 { get; init; }

    /// <summary>Where this record came from, for example <c>Local scan</c>.</summary>
    public string Source { get; init; } = "Unknown";

    [JsonIgnore]
    public string DisplayExtension => string.IsNullOrEmpty(Extension) ? "(none)" : Extension;

    public static FileMetadata FromFileInfo(FileInfo info, string source)
    {
        ArgumentNullException.ThrowIfNull(info);
        return FromFileSystemInfo(info, source);
    }

    /// <summary>
    /// Builds the record from an entry produced by a directory listing. On Windows the listing already
    /// carries the size, timestamps and attributes, so this costs no extra file system round trip -
    /// which matters when millions of entries are being inspected.
    /// </summary>
    public static FileMetadata FromFileSystemInfo(FileSystemInfo info, string source)
    {
        ArgumentNullException.ThrowIfNull(info);
        return new FileMetadata
        {
            FullPath = info.FullName,
            FileName = info.Name,
            Extension = NormalizeExtension(info.Extension),
            DirectoryPath = info is FileInfo located ? located.DirectoryName ?? string.Empty : info.FullName[..Math.Max(0, info.FullName.Length - info.Name.Length)].TrimEnd(Path.DirectorySeparatorChar),
            SizeBytes = info is FileInfo file ? SafeLength(file) : 0,
            CreatedUtc = SafeTime(() => info.CreationTimeUtc),
            ModifiedUtc = SafeTime(() => info.LastWriteTimeUtc),
            AccessedUtc = SafeTime(() => info.LastAccessTimeUtc),
            Attributes = SafeAttributes(info),
            Source = source,
        };
    }

    public static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var value = extension.Trim();
        if (!value.StartsWith('.'))
        {
            value = "." + value;
        }

        return value.ToLowerInvariant();
    }

    private static long SafeLength(FileInfo info)
    {
        try
        {
            return info.Length;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static DateTimeOffset? SafeTime(Func<DateTimeOffset> read)
    {
        try
        {
            var value = read();
            return value.Year <= 1601 ? null : value.ToUniversalTime();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static FileAttributes SafeAttributes(FileSystemInfo info)
    {
        try
        {
            return info.Attributes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return FileAttributes.Normal;
        }
    }

    public override string ToString() => FullPath;
}
