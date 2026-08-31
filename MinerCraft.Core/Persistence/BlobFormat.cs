using System.Buffers.Binary;
using System.IO.Compression;

namespace MinerCraft.Core.Persistence;

/// <summary>
/// A stored blob could not be read, and was refused rather than guessed at.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every path that throws this is one where repairing would be possible and wrong.</b> A
/// truncated chunk could be padded with air; a material ID past the table could be clamped; a
/// blob under the wrong key could be accepted anyway. Each of those turns a loud, one-off
/// failure into terrain that is quietly wrong somewhere the player will find months later — and
/// M6's whole failure model is that a persistence defect destroys work nobody can recreate.
/// A world that half-loads is worse than one that will not open.
/// </para>
/// </remarks>
public sealed class BlobFormatException(string message) : Exception(message);

/// <summary>
/// The frame every stored blob shares, and the compression that sits inside it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Version and both lengths live outside the compressed region</b>, so a reader can refuse an
/// unknown version without first inflating a payload it has already decided it cannot
/// interpret — and so a truncated blob is caught by arithmetic rather than by a decompressor
/// error that could mean anything.
/// </para>
/// <para>
/// <b>Two lengths, and the stored one is there because a truncation test caught its absence.</b>
/// The frame originally declared only the uncompressed size, on the assumption that a short
/// compressed payload would fail to inflate. It does not: cut the last five bytes of a zlib
/// stream — the four-byte Adler-32 and the byte carrying the end-of-stream marker — and the
/// inflater has already emitted every byte it was going to, so it reports a clean end and every
/// check passes. Removing <em>fewer</em> bytes is caught, because the trailer is then present and
/// wrong. **The two mechanisms are complementary and both are kept:** the stored length catches
/// truncation, which a checksum reached by a decompressor that never gets there cannot, and the
/// Adler-32 catches corruption in the middle of an intact blob, which a length cannot.
/// </para>
/// <para>
/// <b>Each blob type carries its own version, not one shared number.</b> They evolve
/// independently: a change to how voxels pack has nothing to say about a column of sky heights,
/// and one shared version would invalidate every stored column to land it.
/// </para>
/// <para>
/// <b>Everything multi-byte is written explicitly little-endian, including the bulk arrays.</b>
/// Reinterpreting a <c>ulong[]</c> as bytes would be one memcpy against a few thousand
/// <see cref="BinaryPrimitives"/> calls, and the difference is invisible beside compression — while
/// a save file whose meaning depends on the architecture that wrote it is exactly the kind of
/// latent trap that surfaces once, years later, on somebody else's machine.
/// </para>
/// </remarks>
internal static class BlobFormat
{
    /// <summary>Payload is zlib-compressed. Clear means it is stored verbatim.</summary>
    internal const byte FlagCompressed = 0x01;

    /// <summary>Bits nothing has defined yet. Set means a writer we do not understand.</summary>
    /// <remarks>
    /// Checked on read for the same reason the version byte is. A flag added later is a format
    /// change that the version byte would normally catch — but only if the version is also
    /// bumped, and refusing unknown flags means forgetting to bump it fails loudly rather than
    /// producing a payload read under the wrong rules.
    /// </remarks>
    internal const byte FlagsKnown = FlagCompressed;

    /// <summary>
    /// Compresses <paramref name="payload"/>, or returns null if that made it no smaller.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both arms are real and the small one is common.</b> A uniform chunk's payload is five
    /// bytes — a whole chunk of one material, which is most of the world — and compressing five
    /// bytes reliably produces more than five. Storing verbatim when compression does not pay is
    /// what keeps the cheapest chunks cheap.
    /// </para>
    /// <para>
    /// <b>zlib rather than raw deflate, for six bytes, and a truncation test is what said so.</b>
    /// Raw deflate carries no integrity check at all: a blob cut after the last byte the
    /// decompressor actually needed inflates to exactly the declared length and then reports a
    /// clean end of stream, so <em>every</em> check here passes and the caller is handed a chunk
    /// assembled from a damaged file. zlib appends an Adler-32 over the uncompressed bytes, which
    /// turns that silent success into an <see cref="InvalidDataException"/> — and quietly covers
    /// bit-rot in the middle of a blob too, which nothing else here was ever going to catch.
    /// </para>
    /// </remarks>
    internal static byte[]? TryCompress(ReadOnlySpan<byte> payload)
    {
        using var output = new MemoryStream(Math.Max(64, payload.Length / 4));

        using (var compress = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            compress.Write(payload);
        }

        return output.Length < payload.Length ? output.ToArray() : null;
    }

    /// <summary>
    /// Inflates <paramref name="compressed"/> into <paramref name="destination"/>, which must
    /// end up exactly full.
    /// </summary>
    /// <remarks>
    /// <b>Both "too short" and "too long" are refusals, and the second one needs asking for.</b>
    /// A decompressor that fills the buffer has not said the stream ended there; reading one byte
    /// past is what distinguishes a payload that matches its declared length from one that merely
    /// starts the same way.
    /// </remarks>
    internal static void Decompress(ReadOnlySpan<byte> compressed, Span<byte> destination, string what)
    {
        using var input = new MemoryStream(compressed.ToArray(), writable: false);
        using var inflate = new ZLibStream(input, CompressionMode.Decompress);

        int filled = 0;
        while (filled < destination.Length)
        {
            int read;
            try
            {
                read = inflate.Read(destination[filled..]);
            }
            catch (InvalidDataException error)
            {
                throw new BlobFormatException(
                    $"{what}: compressed payload is corrupt ({error.Message}).");
            }

            if (read == 0)
            {
                throw new BlobFormatException(
                    $"{what}: compressed payload inflated to {filled} bytes, "
                    + $"but the header declares {destination.Length}.");
            }

            filled += read;
        }

        int trailing;
        try
        {
            trailing = inflate.ReadByte();
        }
        catch (InvalidDataException error)
        {
            throw new BlobFormatException($"{what}: compressed payload is corrupt ({error.Message}).");
        }

        if (trailing != -1)
        {
            throw new BlobFormatException(
                $"{what}: compressed payload inflates to more than the {destination.Length} bytes "
                + "the header declares.");
        }
    }

    /// <summary>Reads the shared frame and hands back the payload, inflating it if needed.</summary>
    /// <param name="blob">The whole stored blob.</param>
    /// <param name="expectedVersion">The only version this reader understands.</param>
    /// <param name="headerBytes">Size of the uncompressed header, payload excluded.</param>
    /// <param name="what">Names the blob in any refusal — the position it was stored under.</param>
    /// <param name="owner">Receives the rented buffer to return once the payload is consumed.</param>
    internal static ReadOnlySpan<byte> ReadPayload(
        ReadOnlySpan<byte> blob,
        byte expectedVersion,
        int headerBytes,
        string what,
        out byte[] owner) =>
        ReadPayload(blob, expectedVersion, expectedVersion, headerBytes, what, out owner, out _);

    /// <summary>
    /// The same, for a reader that understands a <em>range</em> of versions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Only for a change that is strictly additive, and the distinction is the whole of the
    /// policy.</b> A version that appended a section can be read by supplying documented defaults
    /// for what is missing — a world written before the inventory existed comes back carrying
    /// nothing, which is exactly what it was. A version that <em>changed</em> the meaning of a
    /// field cannot: that is the silent world-reshaping the explicit binary layout exists to
    /// prevent, and it must be refused.
    /// </para>
    /// <para>
    /// So this is not "be lenient". It is: name the older versions you can still interpret, and
    /// hand the reader the version it got so the defaults are chosen deliberately rather than by
    /// a field simply being absent.
    /// </para>
    /// </remarks>
    /// <param name="oldestVersion">The oldest layout this reader can still interpret.</param>
    /// <param name="newestVersion">The version this build writes.</param>
    /// <param name="version">Which version the blob actually turned out to be.</param>
    internal static ReadOnlySpan<byte> ReadPayload(
        ReadOnlySpan<byte> blob,
        byte oldestVersion,
        byte newestVersion,
        int headerBytes,
        string what,
        out byte[] owner,
        out byte version)
    {
        version = 0;
        if (blob.Length < headerBytes)
        {
            owner = [];
            throw new BlobFormatException(
                $"{what}: {blob.Length} bytes is shorter than the {headerBytes}-byte header.");
        }

        version = blob[0];
        if (version < oldestVersion || version > newestVersion)
        {
            owner = [];

            string reads = oldestVersion == newestVersion
                ? $"version {newestVersion}"
                : $"versions {oldestVersion} to {newestVersion}";

            throw new BlobFormatException(
                $"{what}: format version {version}, but this build reads {reads}. "
                + "Refusing rather than guessing at the layout.");
        }

        byte flags = blob[1];
        if ((flags & ~FlagsKnown) != 0)
        {
            owner = [];
            throw new BlobFormatException(
                $"{what}: flags 0x{flags:X2} set bits this build does not know about.");
        }

        int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(blob[2..]);
        if (payloadLength < 0)
        {
            owner = [];
            throw new BlobFormatException($"{what}: declares a negative payload length ({payloadLength}).");
        }

        int storedLength = BinaryPrimitives.ReadInt32LittleEndian(blob[6..]);
        if (storedLength < 0)
        {
            owner = [];
            throw new BlobFormatException($"{what}: declares a negative stored length ({storedLength}).");
        }

        ReadOnlySpan<byte> stored = blob[headerBytes..];

        // The check that actually catches truncation, and it runs before any decompression —
        // see the class remarks for why the inflater cannot be relied on to notice.
        if (stored.Length != storedLength)
        {
            owner = [];
            throw new BlobFormatException(
                $"{what}: header declares {storedLength} stored bytes but {stored.Length} follow. "
                + "The blob is truncated or has been appended to.");
        }

        if ((flags & FlagCompressed) == 0)
        {
            if (stored.Length != payloadLength)
            {
                owner = [];
                throw new BlobFormatException(
                    $"{what}: stored uncompressed, but the declared payload length is {payloadLength} "
                    + $"against {stored.Length} bytes.");
            }

            owner = [];
            return stored;
        }

        owner = new byte[payloadLength];
        Decompress(stored, owner, what);
        return owner;
    }

    /// <summary>Writes the version, flags and two lengths that open every blob.</summary>
    /// <param name="payloadLength">Size of the payload once decompressed.</param>
    /// <param name="storedLength">Bytes that actually follow the header. See the class remarks.</param>
    internal static void WriteFrame(
        Span<byte> header, byte version, bool compressed, int payloadLength, int storedLength)
    {
        header[0] = version;
        header[1] = compressed ? FlagCompressed : (byte)0;
        BinaryPrimitives.WriteInt32LittleEndian(header[2..], payloadLength);
        BinaryPrimitives.WriteInt32LittleEndian(header[6..], storedLength);
    }

    /// <summary>Bytes before any type's own key fields: version, flags, payload length, stored length.</summary>
    internal const int FrameBytes = 10;
}
