using System.Buffers.Binary;
using System.Text;

namespace Azrng.DuckDB.Quack.Internal;

/// <summary>
/// DuckDB 二进制协议读取器，用于从字节序列中按序读取各类型字段数据。
/// </summary>
internal ref struct QuackBinaryReader
{
    private readonly ReadOnlyMemory<byte> _data;
    private int _position;

    /// <summary>
    /// 使用指定的字节数据初始化读取器。
    /// </summary>
    /// <param name="data">待读取的二进制数据。</param>
    public QuackBinaryReader(ReadOnlyMemory<byte> data)
    {
        _data = data;
        _position = 0;
    }

    /// <summary>
    /// 获取当前读取位置（字节偏移量）。
    /// </summary>
    public int Position => _position;
    /// <summary>
    /// 获取底层待读取数据。
    /// </summary>
    public ReadOnlyMemory<byte> Data => _data;
    /// <summary>
    /// 获取是否还有未读取的数据。
    /// </summary>
    public bool HasMore => _position < _data.Length;

    /// <summary>
    /// 读取一个无符号 16 位小端整数作为字段标识符。
    /// </summary>
    /// <returns>字段标识符。</returns>
    public ushort ReadFieldId()
    {
        if (_position + 2 > _data.Length)
            throw new QuackProtocolException("Unexpected end of data reading field ID");

        var value = BinaryPrimitives.ReadUInt16LittleEndian(_data.Span[_position..]);
        _position += 2;
        return value;
    }

    /// <summary>
    /// 读取一个无符号变长整数（VarInt 编码）。
    /// </summary>
    /// <returns>解码后的无符号 64 位整数。</returns>
    public ulong ReadVarUInt()
    {
        ulong result = 0;
        int shift = 0;
        while (_position < _data.Length)
        {
            byte b = _data.Span[_position++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return result;
            shift += 7;
        }
        throw new QuackProtocolException("Unexpected end of data reading varint");
    }

    /// <summary>
    /// 读取一个有符号变长整数（signed LEB128 编码）。
    /// </summary>
    /// <returns>解码后的有符号 64 位整数。</returns>
    public long ReadVarInt()
    {
        long result = 0;
        var shift = 0;
        byte value;

        do
        {
            if (_position >= _data.Length)
                throw new QuackProtocolException("Unexpected end of data reading varint");

            value = _data.Span[_position++];
            result |= (long)(value & 0x7F) << shift;
            shift += 7;
        } while ((value & 0x80) != 0);

        if (shift < 64 && (value & 0x40) != 0)
            result |= -1L << shift;

        return result;
    }

    /// <summary>
    /// 读取一个布尔值（非零字节为 true）。
    /// </summary>
    /// <returns>布尔值。</returns>
    public bool ReadBool()
    {
        if (_position >= _data.Length)
            throw new QuackProtocolException("Unexpected end of data reading bool");

        return _data.Span[_position++] != 0;
    }

    /// <summary>
    /// 读取一个 VarInt 长度前缀的 UTF-8 字符串。
    /// </summary>
    /// <returns>解码后的字符串。</returns>
    public string ReadString()
    {
        int length = (int)ReadVarUInt();
        if (_position + length > _data.Length)
            throw new QuackProtocolException("Unexpected end of data reading string");

        var value = Encoding.UTF8.GetString(_data.Span[_position..(_position + length)]);
        _position += length;
        return value;
    }

    /// <summary>
    /// 读取指定数量的字节。
    /// </summary>
    /// <param name="count">要读取的字节数。</param>
    /// <returns>包含读取字节的只读内存块。</returns>
    public ReadOnlyMemory<byte> ReadBytes(int count)
    {
        if (_position + count > _data.Length)
            throw new QuackProtocolException("Unexpected end of data reading bytes");

        var value = _data[_position..(_position + count)];
        _position += count;
        return value;
    }

    /// <summary>
    /// 跳过指定数量的字节。
    /// </summary>
    /// <param name="count">要跳过的字节数。</param>
    public void Skip(int count)
    {
        if (_position + count > _data.Length)
            throw new QuackProtocolException("Unexpected end of data skipping bytes");

        _position += count;
    }

    /// <summary>
    /// 将读取位置移动到指定偏移量。
    /// </summary>
    /// <param name="position">目标偏移量（从数据起始位置计算）。</param>
    public void SeekTo(int position)
    {
        if (position < 0 || position > _data.Length)
            throw new QuackProtocolException($"Invalid seek position: {position}");

        _position = position;
    }
}
