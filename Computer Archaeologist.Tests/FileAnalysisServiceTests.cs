using ComputerArchaeologist.Core.Analysis;
using Xunit;

namespace ComputerArchaeologist.Tests;

/// <summary>Content decoding and header parsing must never throw on hostile input.</summary>
public sealed class FileAnalysisServiceTests
{
    [Fact]
    public void Plain_utf8_text_is_decoded()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("hello archaeology");
        Assert.Equal("hello archaeology", FileAnalysisService.DecodeText(bytes));
    }

    [Fact]
    public void Binary_content_degrades_to_an_empty_excerpt()
    {
        var bytes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        Assert.Equal(string.Empty, FileAnalysisService.DecodeText(bytes));
    }

    [Fact]
    public void Control_character_soup_is_rejected()
    {
        var bytes = new byte[256];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(i % 32);
        }

        Assert.Equal(string.Empty, FileAnalysisService.DecodeText(bytes));
    }

    [Fact]
    public void Empty_input_is_handled()
    {
        Assert.Equal(string.Empty, FileAnalysisService.DecodeText(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Png_dimensions_are_read_from_the_header()
    {
        using var fixture = new TempFixture();
        var path = Path.Combine(fixture.Root, "image.png");

        // 8 byte PNG signature, 4 byte length, "IHDR", then width and height as big endian uint32.
        var bytes = new byte[32];
        byte[] signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        signature.CopyTo(bytes, 0);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        bytes[16] = 0; bytes[17] = 0; bytes[18] = 0x02; bytes[19] = 0x80; // 640
        bytes[20] = 0; bytes[21] = 0; bytes[22] = 0x01; bytes[23] = 0xE0; // 480
        File.WriteAllBytes(path, bytes);

        var description = FileAnalysisService.DescribeImage(path);
        Assert.NotNull(description);
        Assert.Contains("PNG", description, StringComparison.Ordinal);
        Assert.Contains("640x480", description, StringComparison.Ordinal);
    }

    [Fact]
    public void Gif_dimensions_are_read_from_the_header()
    {
        using var fixture = new TempFixture();
        var path = Path.Combine(fixture.Root, "image.gif");

        var bytes = new byte[32];
        "GIF89a"u8.CopyTo(bytes);
        bytes[6] = 0x40; bytes[7] = 0x01; // 320 little endian
        bytes[8] = 0x20; bytes[9] = 0x01; // 288 little endian
        File.WriteAllBytes(path, bytes);

        var description = FileAnalysisService.DescribeImage(path);
        Assert.NotNull(description);
        Assert.Contains("320x288", description, StringComparison.Ordinal);
    }

    [Fact]
    public void Unreadable_image_returns_null_instead_of_throwing()
    {
        using var fixture = new TempFixture();
        var path = Path.Combine(fixture.Root, "not-an-image.png");
        File.WriteAllText(path, "definitely not a png");

        Assert.Null(FileAnalysisService.DescribeImage(path));
    }

    [Fact]
    public void Missing_image_returns_null_instead_of_throwing()
    {
        using var fixture = new TempFixture();
        Assert.Null(FileAnalysisService.DescribeImage(Path.Combine(fixture.Root, "nope.png")));
    }

    [Fact]
    public void Zip_archive_listing_is_described()
    {
        using var fixture = new TempFixture();
        var path = Path.Combine(fixture.Root, "bundle.zip");

        using (var archive = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("readme.txt").Open());
            writer.Write("hello");
        }

        var description = FileAnalysisService.DescribeArchive(path);
        Assert.NotNull(description);
        Assert.Contains("readme.txt", description, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_zip_archives_are_described_generically()
    {
        using var fixture = new TempFixture();
        var path = Path.Combine(fixture.Root, "bundle.7z");
        File.WriteAllText(path, "not really 7z");

        var description = FileAnalysisService.DescribeArchive(path);
        Assert.Equal("7Z archive", description);
    }

    [Fact]
    public async Task Sha256_of_a_known_payload_is_stable()
    {
        using var fixture = new TempFixture();
        var path = fixture.WriteFile("payload.txt", "abc");

        var service = new FileAnalysisService(new Core.Options.ArchaeologyOptions());
        var hash = await service.ComputeSha256Async(path);

        Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", hash);
    }

    [Fact]
    public async Task Hashing_a_missing_file_returns_null()
    {
        using var fixture = new TempFixture();
        var service = new FileAnalysisService(new Core.Options.ArchaeologyOptions());

        Assert.Null(await service.ComputeSha256Async(Path.Combine(fixture.Root, "missing.bin")));
    }
}
