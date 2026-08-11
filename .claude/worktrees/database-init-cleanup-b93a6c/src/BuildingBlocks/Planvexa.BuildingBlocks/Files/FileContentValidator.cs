namespace Planvexa.BuildingBlocks.Files;

using Planvexa.BuildingBlocks.Exceptions;

/// <summary>
/// Magic-byte content sniffing for uploaded files: every upload path in this codebase
/// (task/chat/whiteboard attachments, form uploads, clip media) calls <see cref="ValidateAsync"/> before
/// persisting, so a file's actual bytes are checked against what its declared Content-Type/filename claim,
/// instead of trusting client-supplied metadata outright.
///
/// Only a fixed, well-known set of binary formats can be verified this way — text formats (CSV, plain
/// text, JSON) have no reliable magic number, so a declared type this class does not recognise is let
/// through unchecked rather than rejected (that would be false positives, not security). ZIP-based Office
/// formats (docx/xlsx/pptx) share the same outer ZIP signature, so they are checked as one family, not
/// individually.
///
/// The source stream is typically a forward-only HTTP request body (cannot be rewound), so this reads a
/// small prefix into memory and returns a replacement <see cref="Stream"/> that replays the buffered
/// prefix before continuing to read the original stream — callers must use the RETURNED stream for the
/// remainder of the upload (storage save, malware scan), not the one they passed in.
/// </summary>
public static class FileContentValidator
{
    private const int SniffLength = 32;

    public static async Task<Stream> ValidateAsync(Stream content, string? fileName, string? contentType, CancellationToken cancellationToken = default)
    {
        var buffer = new byte[SniffLength];
        var read = await ReadFullyAsync(content, buffer, cancellationToken);
        var prefix = buffer.AsMemory(0, read).Span;

        var family = ClassifyDeclared(contentType, fileName);
        if (family is not null && !MatchesFamily(family, prefix))
        {
            throw new ValidationAppException(
                $"The uploaded file's content does not match its declared type ({contentType ?? "unknown"}).");
        }

        return new PrefixedStream(buffer, read, content);
    }

    private static async Task<int> ReadFullyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), ct);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    /// <summary>Maps a declared Content-Type (preferred) or file extension to a checkable signature family,
    /// or null when the declared type is not one this class can verify (e.g. text/*).</summary>
    private static string? ClassifyDeclared(string? contentType, string? fileName)
    {
        var type = contentType?.Trim().ToLowerInvariant();
        var extension = string.IsNullOrWhiteSpace(fileName) ? null : Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();

        return (type, extension) switch
        {
            ({ } t, _) when t is "image/png" => "png",
            ({ } t, _) when t is "image/jpeg" or "image/jpg" => "jpeg",
            ({ } t, _) when t is "image/gif" => "gif",
            ({ } t, _) when t is "image/webp" => "webp",
            ({ } t, _) when t is "image/bmp" => "bmp",
            ({ } t, _) when t is "application/pdf" => "pdf",
            ({ } t, _) when t is "application/zip" or "application/x-zip-compressed"
                or "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                or "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                or "application/vnd.openxmlformats-officedocument.presentationml.presentation" => "zip",
            ({ } t, _) when t is "video/mp4" or "video/quicktime" or "audio/mp4" or "audio/x-m4a" => "isobmff",
            ({ } t, _) when t is "video/webm" or "audio/webm" or "video/x-matroska" => "ebml",
            ({ } t, _) when t is "audio/mpeg" or "audio/mp3" => "mp3",
            ({ } t, _) when t is "audio/wav" or "audio/x-wav" or "audio/wave" => "wav",
            ({ } t, _) when t is "audio/ogg" or "video/ogg" or "application/ogg" => "ogg",
            (null or "" or "application/octet-stream", { } ext) => ext switch
            {
                "png" => "png",
                "jpg" or "jpeg" => "jpeg",
                "gif" => "gif",
                "webp" => "webp",
                "bmp" => "bmp",
                "pdf" => "pdf",
                "zip" or "docx" or "xlsx" or "pptx" => "zip",
                "mp4" or "mov" or "m4a" => "isobmff",
                "webm" or "mkv" => "ebml",
                "mp3" => "mp3",
                "wav" => "wav",
                "ogg" or "oga" or "ogv" => "ogg",
                _ => null,
            },
            _ => null,
        };
    }

    private static bool MatchesFamily(string family, ReadOnlySpan<byte> prefix) => family switch
    {
        "png" => StartsWith(prefix, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
        "jpeg" => StartsWith(prefix, [0xFF, 0xD8, 0xFF]),
        "gif" => StartsWith(prefix, "GIF8"u8),
        "bmp" => StartsWith(prefix, [0x42, 0x4D]),
        "pdf" => StartsWith(prefix, "%PDF"u8),
        "webp" => prefix.Length >= 12 && StartsWith(prefix, "RIFF"u8) && prefix.Slice(8, 4).SequenceEqual("WEBP"u8),
        "wav" => prefix.Length >= 12 && StartsWith(prefix, "RIFF"u8) && prefix.Slice(8, 4).SequenceEqual("WAVE"u8),
        // PK\x03\x04 (normal), PK\x05\x06 (empty archive), PK\x07\x08 (spanned) — covers zip and every
        // Office Open XML format built on it.
        "zip" => StartsWith(prefix, [0x50, 0x4B]) && prefix.Length >= 4 && prefix[2] is 0x03 or 0x05 or 0x07,
        // ISO base media file format (mp4/mov/m4a): a 4-byte size then "ftyp" at offset 4.
        "isobmff" => prefix.Length >= 8 && prefix.Slice(4, 4).SequenceEqual("ftyp"u8),
        "ebml" => StartsWith(prefix, [0x1A, 0x45, 0xDF, 0xA3]),
        "mp3" => StartsWith(prefix, "ID3"u8) || (prefix.Length >= 2 && prefix[0] == 0xFF && (prefix[1] & 0xE0) == 0xE0),
        "ogg" => StartsWith(prefix, "OggS"u8),
        _ => true,
    };

    private static bool StartsWith(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
        => data.Length >= signature.Length && data[..signature.Length].SequenceEqual(signature);
}

/// <summary>A forward-only stream that replays a buffered prefix before continuing to read an inner
/// stream — lets <see cref="FileContentValidator"/> peek the first bytes of a non-seekable upload stream
/// without losing them for the caller that saves the full content afterwards.</summary>
internal sealed class PrefixedStream(byte[] prefix, int prefixLength, Stream inner) : Stream
{
    private int _prefixPosition;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_prefixPosition < prefixLength)
        {
            var remaining = prefixLength - _prefixPosition;
            var toCopy = Math.Min(remaining, buffer.Length);
            prefix.AsSpan(_prefixPosition, toCopy).CopyTo(buffer);
            _prefixPosition += toCopy;
            return toCopy;
        }

        return inner.Read(buffer);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_prefixPosition < prefixLength)
        {
            var remaining = prefixLength - _prefixPosition;
            var toCopy = Math.Min(remaining, buffer.Length);
            prefix.AsMemory(_prefixPosition, toCopy).CopyTo(buffer);
            _prefixPosition += toCopy;
            return toCopy;
        }

        return await inner.ReadAsync(buffer, cancellationToken);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync();
        await base.DisposeAsync();
    }
}
