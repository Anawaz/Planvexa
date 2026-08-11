namespace Planvexa.UnitTests.Files;

using Planvexa.BuildingBlocks.Exceptions;
using Planvexa.BuildingBlocks.Files;
using Shouldly;
using Xunit;

public sealed class FileContentValidatorTests
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];
    private static readonly byte[] PdfBytes = "%PDF-1.7 rest of file..."u8.ToArray();
    private static readonly byte[] TextBytes = "just some plain text content"u8.ToArray();

    [Fact]
    public async Task Matching_magic_bytes_pass_and_full_content_is_preserved()
    {
        using var source = new MemoryStream(PngBytes);
        var validated = await FileContentValidator.ValidateAsync(source, "photo.png", "image/png");

        using var buffer = new MemoryStream();
        await validated.CopyToAsync(buffer);
        buffer.ToArray().ShouldBe(PngBytes);
    }

    [Fact]
    public async Task Mismatched_declared_type_is_rejected()
    {
        // PDF bytes claiming to be a PNG.
        using var source = new MemoryStream(PdfBytes);
        await Should.ThrowAsync<ValidationAppException>(
            () => FileContentValidator.ValidateAsync(source, "fake.png", "image/png"));
    }

    [Fact]
    public async Task Extension_is_used_when_content_type_is_generic()
    {
        using var source = new MemoryStream(TextBytes);
        await Should.ThrowAsync<ValidationAppException>(
            () => FileContentValidator.ValidateAsync(source, "fake.pdf", "application/octet-stream"));
    }

    [Fact]
    public async Task Unrecognised_declared_type_is_not_second_guessed()
    {
        // text/plain has no reliable magic number, so arbitrary bytes are accepted.
        using var source = new MemoryStream(TextBytes);
        var validated = await FileContentValidator.ValidateAsync(source, "notes.txt", "text/plain");

        using var buffer = new MemoryStream();
        await validated.CopyToAsync(buffer);
        buffer.ToArray().ShouldBe(TextBytes);
    }

    [Fact]
    public async Task Pdf_content_matching_pdf_declared_type_passes()
    {
        using var source = new MemoryStream(PdfBytes);
        var validated = await FileContentValidator.ValidateAsync(source, "doc.pdf", "application/pdf");

        using var buffer = new MemoryStream();
        await validated.CopyToAsync(buffer);
        buffer.ToArray().ShouldBe(PdfBytes);
    }

    [Fact]
    public async Task Zip_family_covers_office_open_xml_content_types()
    {
        byte[] zipBytes = [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00];
        using var source = new MemoryStream(zipBytes);
        var validated = await FileContentValidator.ValidateAsync(
            source, "report.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        using var buffer = new MemoryStream();
        await validated.CopyToAsync(buffer);
        buffer.ToArray().ShouldBe(zipBytes);
    }
}
