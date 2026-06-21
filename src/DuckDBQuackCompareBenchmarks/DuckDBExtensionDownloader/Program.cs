using DuckDB.NET.Data;

var extensions = new[] { "httpfs", "quack" };

Console.WriteLine("DuckDB 扩展预下载工具");
Console.WriteLine("====================");
Console.WriteLine();

using var conn = new DuckDBConnection("Data Source=:memory:");
conn.Open();

foreach (var ext in extensions)
{
    Console.Write($"正在下载 {ext}...");
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = $"INSTALL {ext};";
        cmd.ExecuteNonQuery();
    }
    Console.WriteLine(" 完成");
}

Console.WriteLine();
Console.WriteLine("所有扩展已下载到本地缓存。");
