using System.Collections;
using System.Data.Common;

namespace Azrng.DuckDB.Quack;

/// <summary>
/// DuckDB 参数集合，管理命令参数的添加、删除和查找操作。
/// </summary>
internal sealed class QuackParameterCollection : DbParameterCollection
{
    private readonly List<DbParameter> _items = new();

    /// <summary>
    /// 获取集合中的参数数量。
    /// </summary>
    public override int Count => _items.Count;
    /// <summary>
    /// 获取同步根对象，用于线程同步。
    /// </summary>
    public override object SyncRoot => this;

    /// <summary>
    /// 向集合中添加一个参数。
    /// </summary>
    /// <param name="value">要添加的参数对象。</param>
    /// <returns>新添加参数在集合中的索引。</returns>
    public override int Add(object value)
    {
        _items.Add(AsParameter(value));
        return _items.Count - 1;
    }

    /// <summary>
    /// 向集合中批量添加参数。
    /// </summary>
    /// <param name="values">要添加的参数对象数组。</param>
    public override void AddRange(Array values)
    {
        foreach (var value in values)
            Add(value!);
    }

    /// <summary>
    /// 清空集合中的所有参数。
    /// </summary>
    public override void Clear()
    {
        _items.Clear();
    }

    /// <summary>
    /// 判断集合中是否包含指定的参数对象。
    /// </summary>
    /// <param name="value">要查找的参数对象。</param>
    /// <returns>如果集合中包含该参数则返回 true，否则返回 false。</returns>
    public override bool Contains(object value)
    {
        return _items.Contains(AsParameter(value, throwOnMismatch: false));
    }

    /// <summary>
    /// 判断集合中是否包含指定名称的参数。
    /// </summary>
    /// <param name="value">要查找的参数名称。</param>
    /// <returns>如果集合中包含该名称的参数则返回 true，否则返回 false。</returns>
    public override bool Contains(string value)
    {
        return _items.Any(p => string.Equals(p.ParameterName, value, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 将集合中的参数复制到指定数组中。
    /// </summary>
    /// <param name="array">目标数组。</param>
    /// <param name="index">目标数组中的起始索引。</param>
    public override void CopyTo(Array array, int index)
    {
        _items.ToArray().CopyTo(array, index);
    }

    /// <summary>
    /// 获取集合的枚举器。
    /// </summary>
    /// <returns>用于遍历集合的枚举器。</returns>
    public override IEnumerator GetEnumerator()
    {
        return _items.GetEnumerator();
    }

    /// <summary>
    /// 获取指定参数对象在集合中的索引。
    /// </summary>
    /// <param name="value">要查找的参数对象。</param>
    /// <returns>参数在集合中的索引，如果未找到则返回 -1。</returns>
    public override int IndexOf(object value)
    {
        return value is DbParameter parameter ? _items.IndexOf(parameter) : -1;
    }

    /// <summary>
    /// 获取指定名称的参数在集合中的索引。
    /// </summary>
    /// <param name="parameterName">要查找的参数名称。</param>
    /// <returns>参数在集合中的索引，如果未找到则返回 -1。</returns>
    public override int IndexOf(string parameterName)
    {
        return _items.FindIndex(p => string.Equals(p.ParameterName, parameterName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 在集合中的指定位置插入参数。
    /// </summary>
    /// <param name="index">插入位置的索引。</param>
    /// <param name="value">要插入的参数对象。</param>
    public override void Insert(int index, object value)
    {
        _items.Insert(index, AsParameter(value));
    }

    /// <summary>
    /// 从集合中移除指定的参数对象。
    /// </summary>
    /// <param name="value">要移除的参数对象。</param>
    public override void Remove(object value)
    {
        if (value is DbParameter parameter)
            _items.Remove(parameter);
    }

    /// <summary>
    /// 移除集合中指定索引位置的参数。
    /// </summary>
    /// <param name="index">要移除参数的索引。</param>
    public override void RemoveAt(int index)
    {
        _items.RemoveAt(index);
    }

    /// <summary>
    /// 移除集合中指定名称的参数。
    /// </summary>
    /// <param name="parameterName">要移除参数的名称。</param>
    public override void RemoveAt(string parameterName)
    {
        var index = IndexOf(parameterName);
        if (index >= 0)
            RemoveAt(index);
    }

    /// <summary>
    /// 获取指定索引位置的参数。
    /// </summary>
    /// <param name="index">参数的索引。</param>
    /// <returns>指定索引位置的参数对象。</returns>
    protected override DbParameter GetParameter(int index)
    {
        return _items[index];
    }

    /// <summary>
    /// 获取指定名称的参数。
    /// </summary>
    /// <param name="parameterName">参数的名称。</param>
    /// <returns>指定名称的参数对象。</returns>
    protected override DbParameter GetParameter(string parameterName)
    {
        var index = IndexOf(parameterName);
        if (index < 0)
            throw new IndexOutOfRangeException(parameterName);

        return _items[index];
    }

    /// <summary>
    /// 设置指定索引位置的参数。
    /// </summary>
    /// <param name="index">要设置参数的索引。</param>
    /// <param name="value">要设置的参数对象。</param>
    protected override void SetParameter(int index, DbParameter value)
    {
        _items[index] = value;
    }

    /// <summary>
    /// 设置指定名称的参数，如果参数不存在则添加。
    /// </summary>
    /// <param name="parameterName">参数的名称。</param>
    /// <param name="value">要设置的参数对象。</param>
    protected override void SetParameter(string parameterName, DbParameter value)
    {
        var index = IndexOf(parameterName);
        if (index >= 0)
            _items[index] = value;
        else
            _items.Add(value);
    }

    private static DbParameter AsParameter(object value, bool throwOnMismatch = true)
    {
        if (value is DbParameter parameter)
            return parameter;

        if (!throwOnMismatch)
            return null!;

        throw new ArgumentException($"Value is not a {nameof(DbParameter)}.", nameof(value));
    }
}
