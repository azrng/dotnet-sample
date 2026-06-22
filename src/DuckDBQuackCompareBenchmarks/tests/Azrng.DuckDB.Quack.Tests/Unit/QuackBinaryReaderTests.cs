using Azrng.DuckDB.Quack.Internal;

namespace Azrng.DuckDB.Quack.Tests;

public sealed class QuackBinaryReaderTests
{
    public static TheoryData<byte[], long> SignedLeb128Cases => new()
    {
        { [0x00], 0L },
        { [0x01], 1L },
        { [0x7F], -1L },
        { [0x7E], -2L },
        { [0x40], -64L },
        { [0xC0, 0x00], 64L },
        { [0xBF, 0x7F], -65L },
        { [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00], long.MaxValue },
        { [0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x7F], long.MinValue }
    };

    [Theory]
    [MemberData(nameof(SignedLeb128Cases))]
    public void ReadVarInt_DecodesSignedLeb128(byte[] payload, long expected)
    {
        var reader = new QuackBinaryReader(payload);

        var value = reader.ReadVarInt();

        Assert.Equal(expected, value);
        Assert.Equal(payload.Length, reader.Position);
    }
}
