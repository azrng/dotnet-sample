using System.Text;
using Azrng.DuckDB.Quack.Internal;

namespace Azrng.DuckDB.Quack.Tests;

/// <summary>
/// Verifies that the binary protocol parser decodes known wire-format payloads and refuses
/// to silently misalign on unknown fields.
/// </summary>
public class QuackProtocolBridgeParsingTests
{
    private static byte[] Concat(params byte[][] chunks)
    {
        var result = new byte[chunks.Sum(c => c.Length)];
        var offset = 0;
        foreach (var chunk in chunks)
        {
            Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
            offset += chunk.Length;
        }
        return result;
    }

    private static byte[] U16(ushort v) => new[] { (byte)(v & 0xFF), (byte)(v >> 8) };
    private static byte[] VarUInt(ulong v)
    {
        var bytes = new List<byte>();
        while (v > 0x7F)
        {
            bytes.Add((byte)(v & 0x7F | 0x80));
            v >>= 7;
        }
        bytes.Add((byte)v);
        return bytes.ToArray();
    }
    private static byte[] Str(string s)
    {
        var body = Encoding.UTF8.GetBytes(s);
        return Concat(VarUInt((ulong)body.Length), body);
    }
    private static byte[] Terminator() => U16(0xFFFF);

    /// <summary>
    /// ReadConnectionResponse DecodesAllFields
    /// </summary>
    [Fact]
    public void ReadConnectionResponse_DecodesAllFields()
    {
        var payload = Concat(
            U16(1), Str("1.5.3"),
            U16(2), Str("win-x64"),
            U16(3), VarUInt(42),
            Terminator());

        var reader = new QuackBinaryReader(payload);
        var response = QuackProtocolBridge.ReadConnectionResponse(ref reader);

        Assert.Equal("1.5.3", response.ServerDuckDbVersion);
        Assert.Equal("win-x64", response.ServerPlatform);
        Assert.Equal("42", response.QuackVersion);
    }

    /// <summary>
    /// ReadErrorResponse DecodesMessage
    /// </summary>
    [Fact]
    public void ReadErrorResponse_DecodesMessage()
    {
        var payload = Concat(
            U16(1), Str("table not found"),
            Terminator());

        var reader = new QuackBinaryReader(payload);
        var response = QuackProtocolBridge.ReadErrorResponse(ref reader);

        Assert.Equal("table not found", response.Message);
    }

    /// <summary>
    /// ReadHeader UnknownField RaisesProtocolException
    /// </summary>
    [Fact]
    public void ReadHeader_UnknownField_RaisesProtocolException()
    {
        var payload = Concat(
            U16(1), VarUInt(2),     // type = ConnectionResponse
            U16(99),                // unknown field — parser must refuse to skip blindly
            U16(2), Str("conn-abc"),
            Terminator());

        Assert.Throws<QuackProtocolException>(() =>
        {
            var reader = new QuackBinaryReader(payload);
            QuackProtocolBridge.ReadHeader(ref reader);
        });
    }

    /// <summary>
    /// ReadHeader KnownFields DecodesSuccessfully
    /// </summary>
    [Fact]
    public void ReadHeader_KnownFields_DecodesSuccessfully()
    {
        var payload = Concat(
            U16(1), VarUInt(2),             // type = ConnectionResponse
            U16(2), Str("conn-abc"),        // connectionId
            U16(3), VarUInt(1),             // client_query_id (1 means clientQueryId=0 internally)
            Terminator());

        var reader = new QuackBinaryReader(payload);
        var header = QuackProtocolBridge.ReadHeader(ref reader);

        Assert.Equal(MessageType.ConnectionResponse, header.Type);
        Assert.Equal("conn-abc", header.ConnectionId);
        Assert.Equal(0UL, header.ClientQueryId);
    }
}
