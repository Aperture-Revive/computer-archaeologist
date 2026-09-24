using ComputerArchaeologist.Core.Analysis;
using ComputerArchaeologist.Core.Discovery;
using ComputerArchaeologist.Core.Models;
using ComputerArchaeologist.Core.Options;
using ComputerArchaeologist.Core.Utilities;

namespace ComputerArchaeologist.Core.Analysis;

public enum PreviewKind
{
    None = 0,
    Text,
    Image,
    Metadata,
}

/// <summary>Lightweight, safe preview of a file. Never parses a whole document.</summary>
public sealed record FilePreview
{
    public PreviewKind Kind { get; init; } = PreviewKind.Metadata;

    public string? Text { get; init; }

    /// <summary>Absolute path used by the UI to render an image thumbnail. Never uploaded anywhere.</summary>
    public string? ImagePath { get; init; }

    public string? Note { get; init; }
}

/// <summary>
/// Produces the in-app preview used by the discovery detail page (specification section 33).
/// Text files expose a bounded prefix, images are handed to the UI as a decoded-downsampled
/// thumbnail, Office/PDF files fall back to metadata, and unknown types show metadata only.
/// </summary>
public interface IFilePreviewService
{
    Task<FilePreview> GetPreviewAsync(FileMetadata file, CancellationToken cancellationToken = default);
}

public sealed class FilePreviewService : IFilePreviewService
{
    private const int MaxPreviewBytes = 12 * 1024;

    private readonly ArchaeologyOptions _options;

    public FilePreviewService(ArchaeologyOptions options) => _options = options;

    public async Task<FilePreview> GetPreviewAsync(FileMetadata file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        var family = FileTypeCatalog.FamilyOf(file.Extension);

        if (family == FileFamily.Image && FileTypeCatalog.IsImage(file.Extension))
        {
            var description = FileAnalysisService.DescribeImage(file.FullPath);
            return new FilePreview
            {
                Kind = PreviewKind.Image,
                ImagePath = file.FullPath,
                Note = description,
            };
        }

        if (FileTypeCatalog.IsTextReadable(file.Extension) || family is FileFamily.Unknown or FileFamily.Code)
        {
            var text = await ReadPrefixAsync(file.FullPath, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return new FilePreview
                {
                    Kind = PreviewKind.Text,
                    Text = text,
                    Note = file.SizeBytes > MaxPreviewBytes
                        ? $"Showing the first {FormatHelpers.FileSize(MaxPreviewBytes)} of {FormatHelpers.FileSize(file.SizeBytes)}."
                        : null,
                };
            }
        }

        return new FilePreview
        {
            Kind = PreviewKind.Metadata,
            Note = _options.PrivacyMode == PrivacyMode.SmartContent
                ? "No text preview is available for this file type."
                : "Preview is limited to metadata because content analysis is disabled.",
        };
    }

    private static async Task<string?> ReadPrefixAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                useAsync: true);

            var buffer = new byte[MaxPreviewBytes];
            var total = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (total <= 0)
            {
                return null;
            }

            var text = FileAnalysisService.DecodeText(buffer.AsSpan(0, total));
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }
}
