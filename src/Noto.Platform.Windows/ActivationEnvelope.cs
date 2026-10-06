using System.Buffers.Binary;
using System.Text;

namespace Noto.Platform.Windows;

/// <summary>What a second launch asks the running Noto to do.</summary>
public enum ActivationKind : byte
{
    /// <summary>Show the workspace. The only kind version 1 accepts; its payload is empty.</summary>
    Activate = 1,

    /// <summary>Reserved for a future <c>noto://</c> link. Rejected in version 1.</summary>
    Uri = 2,

    /// <summary>Reserved for future command-line verbs. Rejected in version 1.</summary>
    Cli = 3,
}

/// <summary>The running Noto's one-byte answer.</summary>
public enum ActivationReply : byte
{
    /// <summary>Taken: the running Noto will act on it. The second launch exits.</summary>
    Accepted = 0,

    /// <summary>The running Noto is closing; the second launch may take over once it has gone.</summary>
    ShuttingDown = 1,

    /// <summary>Malformed, too large, or a version or kind this Noto does not handle.</summary>
    Rejected = 2,
}

/// <summary>Why a message was not a valid request.</summary>
public enum EnvelopeError
{
    /// <summary>Valid.</summary>
    None,

    /// <summary>Shorter than the 8-byte header.</summary>
    Truncated,

    /// <summary>Longer than <see cref="ActivationEnvelope.MaxSize"/>.</summary>
    TooLarge,

    /// <summary>Does not start with <c>NOTO</c>.</summary>
    BadMagic,

    /// <summary>A version other than <see cref="ActivationEnvelope.Version"/>.</summary>
    UnsupportedVersion,

    /// <summary>A kind version 1 does not handle, reserved ones included.</summary>
    UnknownKind,

    /// <summary>The length field does not match the bytes that follow it.</summary>
    BadLength,

    /// <summary>The payload is not valid UTF-8, or is not what the kind requires.</summary>
    BadPayload,
}

/// <summary>One request, decoded.</summary>
/// <param name="Kind">What is asked.</param>
/// <param name="Payload">The kind's text; empty for <see cref="ActivationKind.Activate"/>.</param>
public readonly record struct ActivationRequest(ActivationKind Kind, string Payload)
{
    /// <summary>"Show the workspace."</summary>
    public static ActivationRequest Activate { get; } = new(ActivationKind.Activate, string.Empty);
}

/// <summary>
/// The activation protocol, version 1 (A17, ADR-013): one request and one
/// reply per pipe connection.
/// </summary>
/// <remarks>
/// <para>
/// <b>Request:</b> <c>"NOTO"</c> (4 bytes), version (1 byte, <c>1</c>), kind
/// (1 byte), payload length (<c>uint16</c>, little-endian), then the UTF-8
/// payload. At most <see cref="MaxSize"/> bytes in all.
/// <b>Reply:</b> one byte, an <see cref="ActivationReply"/>.
/// </para>
/// <para>
/// Strict by design: a message is valid only if every field is exactly
/// right. There is no negotiation — a newer client is rejected and says so.
/// </para>
/// </remarks>
public static class ActivationEnvelope
{
    /// <summary>The only protocol version this Noto speaks.</summary>
    public const byte Version = 1;

    /// <summary>The largest request, header included, in bytes.</summary>
    public const int MaxSize = 8 * 1024;

    /// <summary>Magic, version, kind and length.</summary>
    public const int HeaderSize = 8;

    private static ReadOnlySpan<byte> Magic => "NOTO"u8;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Encodes a request.</summary>
    /// <exception cref="ArgumentException">It would not be a valid version 1 request.</exception>
    public static byte[] Encode(ActivationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Payload);

        byte[] payload = StrictUtf8.GetBytes(request.Payload);
        byte[] message = Frame(Version, (byte)request.Kind, payload);

        if (Decode(message, out _) is var error and not EnvelopeError.None)
        {
            throw new ArgumentException($"Not a valid version {Version} request: {error}.", nameof(request));
        }

        return message;
    }

    /// <summary>
    /// Builds a message with any header values, valid or not — the encoder's
    /// own building block, and how tests and the validation harness make
    /// malformed ones.
    /// </summary>
    internal static byte[] Frame(byte version, byte kind, ReadOnlySpan<byte> payload, int? declaredLength = null)
    {
        byte[] message = new byte[HeaderSize + payload.Length];

        Magic.CopyTo(message);
        message[4] = version;
        message[5] = kind;
        BinaryPrimitives.WriteUInt16LittleEndian(message.AsSpan(6), (ushort)(declaredLength ?? payload.Length));
        payload.CopyTo(message.AsSpan(HeaderSize));

        return message;
    }

    /// <summary>Decodes a request, accepting only what version 1 defines.</summary>
    /// <returns><see cref="EnvelopeError.None"/> when <paramref name="request"/> is valid.</returns>
    public static EnvelopeError Decode(ReadOnlySpan<byte> message, out ActivationRequest request)
    {
        request = default;

        if (message.Length > MaxSize)
        {
            return EnvelopeError.TooLarge;
        }

        if (message.Length < HeaderSize)
        {
            return EnvelopeError.Truncated;
        }

        if (!message[..4].SequenceEqual(Magic))
        {
            return EnvelopeError.BadMagic;
        }

        if (message[4] != Version)
        {
            return EnvelopeError.UnsupportedVersion;
        }

        if (message[5] != (byte)ActivationKind.Activate)
        {
            return EnvelopeError.UnknownKind;
        }

        int length = BinaryPrimitives.ReadUInt16LittleEndian(message[6..]);

        if (length != message.Length - HeaderSize)
        {
            return EnvelopeError.BadLength;
        }

        // Activate carries nothing; any payload is malformed.
        if (length != 0)
        {
            return EnvelopeError.BadPayload;
        }

        request = ActivationRequest.Activate;
        return EnvelopeError.None;
    }

    /// <summary>Reads a reply byte; anything undefined is <see langword="null"/>.</summary>
    public static ActivationReply? DecodeReply(int value) =>
        value is >= 0 and <= (int)ActivationReply.Rejected ? (ActivationReply)value : null;
}
