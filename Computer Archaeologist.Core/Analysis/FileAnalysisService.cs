using System.Security.Cryptography;
using ComputerArchaeologist.Core.Analysis;
using ComputerArchaeologist.Core.Discovery;
using ComputerArchaeologist.Core.Models;
using ComputerArchaeologist.Core.Options;
using Microsoft.Extensions.Logging;

namespace ComputerArchaeologist.Core.Analysis;

/// <summary>
/// Turns a raw <see cref="FileMetadata"/> into everything the scoring and AI stages need:
/// folder structure, a bounded content excerpt (only when the privacy mode allows it), a structural
/// summary for images and archives, and - for the final candidates only - a SHA-256 fingerprint.
/// </summary>
public interface IFileAnalysisService
{
    /// <summary>Reads the real directory listing for every candidate folder, bounded and cached.</summary>
    Task<IReadOnlyDictionary<string, DirectoryClusterInfo>> BuildDirectoryClustersAsync(
        IReadOnlyCollection<string> directories,
        CancellationToken cancellationToken = default);

    Task<FileContext> BuildContextAsync(
        FileMetadata file,
        DirectoryClusterInfo cluster,
        CancellationToken cancellationToken = default);

    Task<string?> ComputeSha256Async(string fullPath, CancellationToken cancellationToken = default);
}

public sealed class FileAnalysisService : IFileAnalysisService
{
    private const int MaxSiblingsInspected = 250;

    /// <summary>Header parsing is cheap, but a pathological file is still skipped.</summary>
    private const long MaxStructuralAnalysisBytes = 512L * 1024 * 1024;

    /// <summary>Reading a ZIP central directory is bounded so a huge archive cannot exhaust memory.</summary>
    private const long MaxArchiveAnalysisBytes = 2L * 1024 * 1024 * 1024;

    private readonly ArchaeologyOptions _options;
    private readonly ILogger<FileAnalysisService>? _logger;

    public FileAnalysisService(ArchaeologyOptions options, ILogger<FileAnalysisService>? logger = null)
    {
        _options = options ?? new ArchaeologyOptions();
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, DirectoryClusterInfo>> BuildDirectoryClustersAsync(
        IReadOnlyCollection<string> directories,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(directories);

        var result = new Dictionary<string, DirectoryClusterInfo>(StringComparer.OrdinalIgnoreCase);
        var distinct = directories
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(NormalizeDirectory)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (distinct.Length == 0)
        {
            return result;
        }

        var parallelism = Math.Clamp(_options.SafeFileReadConcurrency, 1, 16);
        var gate = new SemaphoreSlim(parallelism, parallelism);

        var tasks = distinct.Select(async directory =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var info = await Task.Run(() => DescribeDirectory(directory), cancellationToken).ConfigureAwait(false);
                return (directory, info);
            }
            finally
            {
                gate.Release();
            }
        });

        foreach (var task in tasks)
        {
            var (directory, info) = await task.ConfigureAwait(false);
            result[directory] = info;
        }

        return result;
    }

    private DirectoryClusterInfo DescribeDirectory(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return DirectoryClusterInfo.Empty;
            }

            var fileNames = new List<string>(64);
            var siblingCount = 0;
            var sourceCount = 0;
            var assetCount = 0;
            var hasReadme = false;
            var hasManifest = false;

            foreach (var path in Directory.EnumerateFiles(directory))
            {
                siblingCount++;
                if (fileNames.Count < MaxSiblingsInspected)
                {
                    fileNames.Add(Path.GetFileName(path));
                }

                var name = Path.GetFileName(path);
                var extension = Path.GetExtension(name);

                if (FileTypeCatalog.IsSourceCode(extension))
                {
                    sourceCount++;
                }
                else if (FileTypeCatalog.IsImage(extension) || FileTypeCatalog.IsArchive(extension))
                {
                    assetCount++;
                }

                if (name.StartsWith("readme", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("license", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("notes.txt", StringComparison.OrdinalIgnoreCase))
                {
                    hasReadme = true;
                }

                if (extension is ".sln" or ".csproj" or ".vcxproj" or ".uproject" or ".unity"
                    or ".godot" or ".love" or ".gmx" or ".yyp" or ".pbxproj" or ".gradle"
                    || name.Equals("package.json", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("cargo.toml", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("makefile", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("cmakelists.txt", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("go.mod", StringComparison.OrdinalIgnoreCase))
                {
                    hasManifest = true;
                }
            }

            var hasAssetFolder = false;
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(directory))
                {
                    var name = Path.GetFileName(sub).ToLowerInvariant();
                    if (name is "assets" or "art" or "sprites" or "textures" or "resources" or "content" or "media" or "res" or "data")
                    {
                        hasAssetFolder = true;
                        break;
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // Directory listing of sub folders is best effort.
            }

            var looksLikeProject =
                (hasManifest && (sourceCount >= 1 || hasReadme)) ||
                (sourceCount >= 3 && (hasReadme || hasAssetFolder)) ||
                (sourceCount >= 6) ||
                (sourceCount >= 2 && hasAssetFolder && hasReadme);

            return new DirectoryClusterInfo
            {
                SiblingCount = siblingCount,
                LooksLikeProject = looksLikeProject,
                HasReadme = hasReadme,
                HasManifest = hasManifest,
                SourceFileCount = sourceCount,
                AssetFileCount = assetCount,
                FileNames = fileNames,
            };
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PathTooLongException)
        {
            return DirectoryClusterInfo.Empty;
        }
    }

    public async Task<FileContext> BuildContextAsync(
        FileMetadata file,
        DirectoryClusterInfo cluster,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        cluster ??= DirectoryClusterInfo.Empty;

        string? excerpt = null;
        var truncated = false;
        var bytesRead = 0;
        string? language = null;
        string? structural = null;

        var family = FileTypeCatalog.FamilyOf(file.Extension);

        if (family == FileFamily.Image && _options.AnalyzeImages && file.SizeBytes <= MaxStructuralAnalysisBytes)
        {
            structural = await Task.Run(() => DescribeImage(file.FullPath), cancellationToken).ConfigureAwait(false);
        }
        else if (family == FileFamily.Archive && _options.AnalyzeArchives && file.SizeBytes <= MaxArchiveAnalysisBytes)
        {
            structural = await Task.Run(() => DescribeArchive(file.FullPath), cancellationToken).ConfigureAwait(false);
        }
        else if (family == FileFamily.Code)
        {
            if (_options.AnalyzeCode)
            {
                language = DetectLanguage(file.Extension);
            }
        }

        if (_options.PrivacyMode == PrivacyMode.SmartContent && ShouldReadContent(file, family))
        {
            var (text, wasTruncated, read) = await ReadExcerptAsync(file, cancellationToken).ConfigureAwait(false);
            excerpt = text;
            truncated = wasTruncated;
            bytesRead = read;
        }

        var nearby = cluster.FileNames
            .Where(n => !string.Equals(n, file.FileName, StringComparison.OrdinalIgnoreCase))
            .Take(40)
            .ToArray();

        return new FileContext
        {
            DirectoryPath = file.DirectoryPath,
            NearbyFiles = nearby,
            SiblingCount = cluster.SiblingCount,
            LooksLikeProject = cluster.LooksLikeProject,
            ContentExcerpt = excerpt,
            ContentTruncated = truncated,
            ContentBytesRead = bytesRead,
            DetectedLanguage = language,
            StructuralSummary = structural,
        };
    }

    private bool ShouldReadContent(FileMetadata file, FileFamily family)
    {
        if (file.SizeBytes <= 0 || file.SizeBytes > 64L * 1024 * 1024)
        {
            return false;
        }

        return family switch
        {
            FileFamily.Code => _options.AnalyzeCode,
            FileFamily.Image or FileFamily.Audio or FileFamily.Video => false,
            FileFamily.Archive => false,
            _ => FileTypeCatalog.IsTextReadable(file.Extension) || family == FileFamily.Unknown,
        };
    }

    private async Task<(string? Text, bool Truncated, int BytesRead)> ReadExcerptAsync(
        FileMetadata file,
        CancellationToken cancellationToken)
    {
        var limit = _options.SafeMaxContentBytes;

        try
        {
            await using var stream = new FileStream(
                file.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 8192,
                useAsync: true);

            var buffer = new byte[Math.Min(limit, 64 * 1024)];
            var total = 0;
            while (total < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total += read;
            }

            if (total == 0)
            {
                return (null, false, 0);
            }

            var truncated = stream.Position < stream.Length;
            var text = DecodeText(buffer.AsSpan(0, total));
            return (text, truncated, total);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger?.LogDebug(ex, "Could not read an excerpt from {Path}", file.FullPath);
            return (null, false, 0);
        }
    }

    /// <summary>Decodes a byte prefix, stripping NUL padding so binary files degrade to readable text.</summary>
    internal static string DecodeText(ReadOnlySpan<byte> bytes)
    {
        var nul = bytes.IndexOf((byte)0);
        if (nul >= 0 && nul < 8)
        {
            return string.Empty; // Looks like a UTF-16 or binary blob: not worth sending.
        }

        var slice = bytes;
        if (nul > 0)
        {
            slice = bytes[..nul];
        }

        var text = System.Text.Encoding.UTF8.GetString(slice);
        var controlRatio = text.Count(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')) / (double)Math.Max(1, text.Length);
        if (controlRatio > 0.05)
        {
            return string.Empty;
        }

        return text.Length > 24_000 ? text[..24_000] : text;
    }

    public async Task<string?> ComputeSha256Async(string fullPath, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                useAsync: true);

            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexString(hash);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private static string DetectLanguage(string extension) => FileMetadata.NormalizeExtension(extension).TrimStart('.') switch
    {
        "cs" => "C#",
        "vb" => "Visual Basic",
        "fs" => "F#",
        "cpp" or "cc" or "cxx" or "h" or "hpp" => "C++",
        "c" => "C",
        "py" or "pyw" => "Python",
        "js" or "mjs" or "cjs" => "JavaScript",
        "ts" or "tsx" => "TypeScript",
        "java" => "Java",
        "kt" => "Kotlin",
        "lua" => "Lua",
        "rb" => "Ruby",
        "php" => "PHP",
        "go" => "Go",
        "rs" => "Rust",
        "swift" => "Swift",
        "dart" => "Dart",
        "pas" => "Pascal",
        "bas" => "BASIC",
        "asm" or "s" => "Assembly",
        "sh" or "bash" => "Shell",
        "ps1" or "psm1" => "PowerShell",
        "sql" => "SQL",
        "gd" => "GDScript",
        "html" or "htm" => "HTML",
        "css" or "scss" => "CSS",
        "json" => "JSON",
        "xml" => "XML",
        "yaml" or "yml" => "YAML",
        "md" or "markdown" => "Markdown",
        _ => extension.TrimStart('.').ToUpperInvariant(),
    };

    // ------------------------------------------------------------- image headers

    /// <summary>
    /// Reads dimensions straight from the file header. No imaging dependency is used, which keeps
    /// this safe for untrusted files and free of extra packages.
    /// </summary>
    internal static string? DescribeImage(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> header = stackalloc byte[32];
            var read = stream.Read(header);
            if (read < 10)
            {
                return null;
            }

            // PNG
            if (read >= 24 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
            {
                var width = ReadBigEndianInt32(header[16..20]);
                var height = ReadBigEndianInt32(header[20..24]);
                return $"PNG {width}x{height}";
            }

            // GIF
            if (header[0] == 'G' && header[1] == 'I' && header[2] == 'F')
            {
                var width = header[6] | (header[7] << 8);
                var height = header[8] | (header[9] << 8);
                return $"GIF {width}x{height}";
            }

            // BMP
            if (header[0] == 'B' && header[1] == 'M' && read >= 26)
            {
                var width = BitConverter.ToInt32(header[18..22]);
                var height = Math.Abs(BitConverter.ToInt32(header[22..26]));
                return $"BMP {width}x{height}";
            }

            // JPEG: walk the marker chain looking for a start-of-frame segment.
            if (header[0] == 0xFF && header[1] == 0xD8)
            {
                stream.Position = 2;
                var buffer = new byte[4];
                while (stream.Position < stream.Length - 4)
                {
                    if (stream.ReadByte() != 0xFF)
                    {
                        continue;
                    }

                    var marker = stream.ReadByte();
                    if (marker is 0xD8 or 0x01 or >= 0xD0 and <= 0xD7)
                    {
                        continue;
                    }

                    if (stream.Read(buffer, 0, 2) != 2)
                    {
                        break;
                    }

                    var length = (buffer[0] << 8) | buffer[1];
                    if (length < 2)
                    {
                        break;
                    }

                    if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
                    {
                        // Segment payload: length(2) precision(1) height(2) width(2) ...
                        var sof = new byte[5];
                        if (stream.Read(sof, 0, sof.Length) != sof.Length)
                        {
                            break;
                        }

                        var height = (sof[1] << 8) | sof[2];
                        var width = (sof[3] << 8) | sof[4];
                        return $"JPEG {width}x{height}";
                    }

                    stream.Position += length - 2;
                }

                return "JPEG";
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private static int ReadBigEndianInt32(ReadOnlySpan<byte> bytes) =>
        (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];

    /// <summary>Lists the top level of a ZIP archive. Other formats are described but not parsed.</summary>
    internal static string? DescribeArchive(string path)
    {
        var extension = FileMetadata.NormalizeExtension(Path.GetExtension(path));
        if (extension != ".zip")
        {
            return $"{extension.TrimStart('.').ToUpperInvariant()} archive";
        }

        try
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(path);
            var names = archive.Entries.Take(15).Select(e => e.FullName).ToArray();
            var total = archive.Entries.Count;
            return $"ZIP with {total} entries: {string.Join(", ", names)}";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        {
            return "ZIP archive (contents not readable)";
        }
    }

    private static string NormalizeDirectory(string directory) =>
        directory.Replace('/', '\\').TrimEnd('\\');
}
