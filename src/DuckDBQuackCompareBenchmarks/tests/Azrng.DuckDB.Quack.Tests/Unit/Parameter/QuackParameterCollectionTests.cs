using System.Data.Common;

namespace Azrng.DuckDB.Quack.Tests;

/// <summary>
/// QuackParameterCollection 的单元测试，验证参数集合的各项操作。
/// </summary>
public class QuackParameterCollectionTests
{
    /// <summary>
    /// 验证 Add 方法能够正确添加参数并返回索引。
    /// </summary>
    [Fact]
    public void Add_AddsParameter()
    {
        var collection = new QuackParameterCollection();
        var param = CreateParam("@id", 1);

        var index = collection.Add(param);

        Assert.Equal(0, index);
        Assert.Equal(1, collection.Count);
    }

    /// <summary>
    /// 验证 AddRange 方法能够批量添加多个参数。
    /// </summary>
    [Fact]
    public void AddRange_AddsMultipleParameters()
    {
        var collection = new QuackParameterCollection();
        var parameters = new[] { CreateParam("@a", 1), CreateParam("@b", 2) };

        collection.AddRange(parameters);

        Assert.Equal(2, collection.Count);
    }

    /// <summary>
    /// 验证 Clear 方法能够移除集合中的所有参数。
    /// </summary>
    [Fact]
    public void Clear_RemovesAllParameters()
    {
        var collection = new QuackParameterCollection();
        collection.Add(CreateParam("@a", 1));
        collection.Add(CreateParam("@b", 2));

        collection.Clear();

        Assert.Equal(0, collection.Count);
    }

    /// <summary>
    /// 验证 Contains 方法通过对象引用判断参数是否存在。
    /// </summary>
    [Fact]
    public void Contains_ByIdentity_ReturnsTrue()
    {
        var collection = new QuackParameterCollection();
        var param = CreateParam("@id", 1);
        collection.Add(param);

        Assert.True(collection.Contains(param));
    }

    /// <summary>
    /// 验证 Contains 方法通过名称查找参数，且支持大小写不敏感匹配。
    /// </summary>
    [Fact]
    public void Contains_ByName_ReturnsTrue()
    {
        var collection = new QuackParameterCollection();
        collection.Add(CreateParam("@id", 1));

        Assert.True(collection.Contains("@id"));
        Assert.True(collection.Contains("@ID"));
        Assert.False(collection.Contains("@other"));
    }

    /// <summary>
    /// 验证 IndexOf 方法通过对象引用返回参数在集合中的索引。
    /// </summary>
    [Fact]
    public void IndexOf_ByIdentity_ReturnsIndex()
    {
        var collection = new QuackParameterCollection();
        var param1 = CreateParam("@a", 1);
        var param2 = CreateParam("@b", 2);
        collection.Add(param1);
        collection.Add(param2);

        Assert.Equal(0, collection.IndexOf(param1));
        Assert.Equal(1, collection.IndexOf(param2));
    }

    /// <summary>
    /// 验证 IndexOf 方法通过名称查找参数索引，支持大小写不敏感匹配，未找到返回 -1。
    /// </summary>
    [Fact]
    public void IndexOf_ByName_ReturnsIndex()
    {
        var collection = new QuackParameterCollection();
        collection.Add(CreateParam("@a", 1));
        collection.Add(CreateParam("@b", 2));

        Assert.Equal(0, collection.IndexOf("@a"));
        Assert.Equal(1, collection.IndexOf("@B"));
        Assert.Equal(-1, collection.IndexOf("@c"));
    }

    /// <summary>
    /// 验证 Insert 方法能够在指定索引位置插入参数。
    /// </summary>
    [Fact]
    public void Insert_InsertsAtIndex()
    {
        var collection = new QuackParameterCollection();
        collection.Add(CreateParam("@a", 1));
        collection.Add(CreateParam("@c", 3));

        collection.Insert(1, CreateParam("@b", 2));

        Assert.Equal(3, collection.Count);
        Assert.Equal("@b", ((DbParameter)collection[1]).ParameterName);
    }

    /// <summary>
    /// 验证 Remove 方法通过对象引用移除指定参数。
    /// </summary>
    [Fact]
    public void Remove_RemovesParameter()
    {
        var collection = new QuackParameterCollection();
        var param = CreateParam("@id", 1);
        collection.Add(param);

        collection.Remove(param);

        Assert.Equal(0, collection.Count);
    }

    /// <summary>
    /// 验证 RemoveAt 方法通过索引移除参数。
    /// </summary>
    [Fact]
    public void RemoveAt_ByIndex_RemovesParameter()
    {
        var collection = new QuackParameterCollection();
        collection.Add(CreateParam("@a", 1));
        collection.Add(CreateParam("@b", 2));

        collection.RemoveAt(0);

        Assert.Equal(1, collection.Count);
        Assert.Equal("@b", ((DbParameter)collection[0]).ParameterName);
    }

    /// <summary>
    /// 验证 RemoveAt 方法通过名称移除参数。
    /// </summary>
    [Fact]
    public void RemoveAt_ByName_RemovesParameter()
    {
        var collection = new QuackParameterCollection();
        collection.Add(CreateParam("@a", 1));
        collection.Add(CreateParam("@b", 2));

        collection.RemoveAt("@a");

        Assert.Equal(1, collection.Count);
        Assert.Equal("@b", ((DbParameter)collection[0]).ParameterName);
    }

    /// <summary>
    /// 验证 RemoveAt 方法在名称不存在时不做任何操作。
    /// </summary>
    [Fact]
    public void RemoveAt_ByName_NotFound_DoesNothing()
    {
        var collection = new QuackParameterCollection();
        collection.Add(CreateParam("@a", 1));

        collection.RemoveAt("@nonexistent");

        Assert.Equal(1, collection.Count);
    }

    /// <summary>
    /// 验证通过索引器按索引获取参数。
    /// </summary>
    [Fact]
    public void GetParameter_ByIndex_ReturnsParameter()
    {
        var collection = new QuackParameterCollection();
        var param = CreateParam("@id", 42);
        collection.Add(param);

        var result = collection[0];
        Assert.Same(param, result);
    }

    /// <summary>
    /// 验证通过索引器按名称获取参数。
    /// </summary>
    [Fact]
    public void GetParameter_ByName_ReturnsParameter()
    {
        var collection = new QuackParameterCollection();
        var param = CreateParam("@id", 42);
        collection.Add(param);

        var result = collection["@id"];
        Assert.Same(param, result);
    }

    /// <summary>
    /// 验证通过名称获取参数时支持大小写不敏感匹配。
    /// </summary>
    [Fact]
    public void GetParameter_ByName_CaseInsensitive()
    {
        var collection = new QuackParameterCollection();
        var param = CreateParam("@MyParam", 42);
        collection.Add(param);

        var result = collection["@myparam"];
        Assert.Same(param, result);
    }

    /// <summary>
    /// 验证通过名称获取不存在的参数时抛出 IndexOutOfRangeException。
    /// </summary>
    [Fact]
    public void GetParameter_ByName_NotFound_Throws()
    {
        var collection = new QuackParameterCollection();
        collection.Add(CreateParam("@a", 1));

        Assert.Throws<IndexOutOfRangeException>(() => collection["@nonexistent"]);
    }

    /// <summary>
    /// 验证通过索引器按索引替换参数。
    /// </summary>
    [Fact]
    public void SetParameter_ByIndex_ReplacesParameter()
    {
        var collection = new QuackParameterCollection();
        var param1 = CreateParam("@a", 1);
        var param2 = CreateParam("@a", 2);
        collection.Add(param1);

        collection[0] = param2;

        Assert.Same(param2, collection[0]);
    }

    /// <summary>
    /// 验证通过索引器按名称替换已存在的参数。
    /// </summary>
    [Fact]
    public void SetParameter_ByName_ReplacesParameter()
    {
        var collection = new QuackParameterCollection();
        var param1 = CreateParam("@a", 1);
        var param2 = CreateParam("@a", 2);
        collection.Add(param1);

        collection["@a"] = param2;

        Assert.Same(param2, collection[0]);
    }

    /// <summary>
    /// 验证通过索引器设置不存在的名称时自动添加新参数。
    /// </summary>
    [Fact]
    public void SetParameter_ByName_NotFound_AddsParameter()
    {
        var collection = new QuackParameterCollection();
        var param = CreateParam("@new", 42);

        collection["@new"] = param;

        Assert.Equal(1, collection.Count);
        Assert.Same(param, collection[0]);
    }

    /// <summary>
    /// 验证 GetEnumerator 能够遍历集合中的所有参数。
    /// </summary>
    [Fact]
    public void GetEnumerator_IteratesAllParameters()
    {
        var collection = new QuackParameterCollection();
        collection.Add(CreateParam("@a", 1));
        collection.Add(CreateParam("@b", 2));

        var count = 0;
        foreach (var item in collection)
            count++;

        Assert.Equal(2, count);
    }

    /// <summary>
    /// 验证 CopyTo 方法能够将参数集合复制到目标数组。
    /// </summary>
    [Fact]
    public void CopyTo_CopiesToArray()
    {
        var collection = new QuackParameterCollection();
        var param1 = CreateParam("@a", 1);
        var param2 = CreateParam("@b", 2);
        collection.Add(param1);
        collection.Add(param2);

        var array = new DbParameter[2];
        collection.CopyTo(array, 0);

        Assert.Same(param1, array[0]);
        Assert.Same(param2, array[1]);
    }

    /// <summary>
    /// 验证 SyncRoot 属性返回集合自身实例。
    /// </summary>
    [Fact]
    public void SyncRoot_ReturnsSelf()
    {
        var collection = new QuackParameterCollection();

        Assert.Same(collection, collection.SyncRoot);
    }

    private static DbParameter CreateParam(string name, object? value)
    {
        return new QuackParameter { ParameterName = name, Value = value };
    }
}
