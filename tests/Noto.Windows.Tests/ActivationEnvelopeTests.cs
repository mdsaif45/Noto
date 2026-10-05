using Noto.Platform.Windows;
using Xunit;

namespace Noto.Windows.Tests;

/// <summary>
/// The activation protocol, version 1 (A17, ADR-013): exactly one valid
/// request, and every way of getting it wrong rejected.
/// </summary>
public sealed class ActivationEnvelopeTests
{
    private static readonly byte[] ValidActivate = [(byte)'N', (byte)'O', (byte)'T', (byte)'O', 1, 1, 0, 0];

    [Fact]
    public void Activate_encodes_to_the_documented_eight_bytes()
    {
        Assert.Equal(ValidActivate, ActivationEnvelope.Encode(ActivationRequest.Activate));
    }

    [Fact]
    public void Activate_round_trips()
    {
        byte[] message = ActivationEnvelope.Encode(ActivationRequest.Activate);

        Assert.Equal(EnvelopeError.None, ActivationEnvelope.Decode(message, out ActivationRequest decoded));
        Assert.Equal(ActivationRequest.Activate, decoded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(7)]
    public void A_message_shorter_than_the_header_is_truncated(int length)
    {
        Assert.Equal(EnvelopeError.Truncated, ActivationEnvelope.Decode(ValidActivate.AsSpan(0, length), out _));
    }

    [Theory]
    [InlineData("NOTE")]
    [InlineData("noto")]
    [InlineData("\0OTO")]
    public void A_wrong_magic_is_rejected(string magic)
    {
        byte[] message = [.. ValidActivate];
        System.Text.Encoding.ASCII.GetBytes(magic).CopyTo(message, 0);

        Assert.Equal(EnvelopeError.BadMagic, ActivationEnvelope.Decode(message, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(255)]
    public void Any_version_but_1_is_rejected(byte version)
    {
        Assert.Equal(
            EnvelopeError.UnsupportedVersion,
            ActivationEnvelope.Decode(ActivationEnvelope.Frame(version, 1, []), out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)] // Uri: reserved, not implemented in version 1
    [InlineData(3)] // Cli: reserved, not implemented in version 1
    [InlineData(4)]
    [InlineData(255)]
    public void Any_kind_but_activate_is_rejected(byte kind)
    {
        Assert.Equal(
            EnvelopeError.UnknownKind,
            ActivationEnvelope.Decode(ActivationEnvelope.Frame(ActivationEnvelope.Version, kind, []), out _));
    }

    [Fact]
    public void Reserved_kinds_carrying_a_payload_are_rejected_as_unknown()
    {
        byte[] uri = ActivationEnvelope.Frame(ActivationEnvelope.Version, (byte)ActivationKind.Uri, "noto://note/1"u8);

        Assert.Equal(EnvelopeError.UnknownKind, ActivationEnvelope.Decode(uri, out _));
    }

    [Fact]
    public void Activate_with_a_payload_is_rejected()
    {
        byte[] message = ActivationEnvelope.Frame(ActivationEnvelope.Version, (byte)ActivationKind.Activate, "x"u8);

        Assert.Equal(EnvelopeError.BadPayload, ActivationEnvelope.Decode(message, out _));
    }

    [Theory]
    [InlineData(1)]   // claims a byte that is not there
    [InlineData(100)]
    [InlineData(65535)]
    public void A_length_longer_than_the_bytes_that_follow_is_rejected(int declared)
    {
        byte[] message = ActivationEnvelope.Frame(ActivationEnvelope.Version, 1, [], declared);

        Assert.Equal(EnvelopeError.BadLength, ActivationEnvelope.Decode(message, out _));
    }

    [Fact]
    public void Bytes_beyond_the_declared_length_are_rejected()
    {
        byte[] message = ActivationEnvelope.Frame(ActivationEnvelope.Version, 1, "xyz"u8, declaredLength: 0);

        Assert.Equal(EnvelopeError.BadLength, ActivationEnvelope.Decode(message, out _));
    }

    [Fact]
    public void A_message_over_8_KiB_is_rejected_before_anything_else_is_read()
    {
        byte[] message = new byte[ActivationEnvelope.MaxSize + 1];
        ValidActivate.CopyTo(message, 0);

        Assert.Equal(EnvelopeError.TooLarge, ActivationEnvelope.Decode(message, out _));
    }

    [Fact]
    public void A_message_of_exactly_8_KiB_is_not_too_large_but_still_malformed_for_activate()
    {
        byte[] payload = new byte[ActivationEnvelope.MaxSize - ActivationEnvelope.HeaderSize];
        byte[] message = ActivationEnvelope.Frame(ActivationEnvelope.Version, 1, payload);

        Assert.Equal(ActivationEnvelope.MaxSize, message.Length);
        Assert.Equal(EnvelopeError.BadPayload, ActivationEnvelope.Decode(message, out _));
    }

    [Fact]
    public void A_rejected_decode_never_returns_a_request()
    {
        Assert.NotEqual(EnvelopeError.None, ActivationEnvelope.Decode(ActivationEnvelope.Frame(2, 1, []), out ActivationRequest request));
        Assert.Equal(default, request);
    }

    [Fact]
    public void Encoding_refuses_what_version_1_would_reject()
    {
        Assert.Throws<ArgumentException>(() => ActivationEnvelope.Encode(new ActivationRequest(ActivationKind.Uri, "noto://x")));
        Assert.Throws<ArgumentException>(() => ActivationEnvelope.Encode(new ActivationRequest(ActivationKind.Activate, "x")));
    }

    // ---------------------------------------------------------------- reply

    [Theory]
    [InlineData(0, ActivationReply.Accepted)]
    [InlineData(1, ActivationReply.ShuttingDown)]
    [InlineData(2, ActivationReply.Rejected)]
    public void Replies_are_one_documented_byte(int value, ActivationReply reply)
    {
        Assert.Equal(value, (int)reply);
        Assert.Equal(reply, ActivationEnvelope.DecodeReply(value));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(255)]
    [InlineData(-1)]
    public void An_undefined_reply_is_not_read_as_any_answer(int value)
    {
        Assert.Null(ActivationEnvelope.DecodeReply(value));
    }
}
