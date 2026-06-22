using System.Data;

namespace Azrng.DuckDB.Quack.Tests;

/// <summary>
/// Schema metadata (<see cref="DbDataReader.GetSchemaTable"/>) and <see cref="CommandBehavior"/>
/// contracts — both were previously unverified end-to-end and the behaviour flag was silently
/// dropped by the command before this coverage existed.
/// </summary>
[Trait("Category", "Integration")]
[Collection(IntegrationTestCollection.Name)]
public sealed class SchemaAndBehaviorIntegrationTests
{
    private readonly TestOptions _options;

    public SchemaAndBehaviorIntegrationTests(TestOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// GetSchemaTable must surface per-column metadata: name, ordinal, data type, and the
    /// provider type name, so schema-bound consumers can introspect a result without reading rows.
    /// </summary>
    [Fact]
    public async Task GetSchemaTable_ReportsColumnMetadata()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 42 AS the_answer, 'hello' AS greeting, CAST(1.5 AS DOUBLE) AS ratio";

        await using var reader = await command.ExecuteReaderAsync();
        var schema = reader.GetSchemaTable()!;

        Assert.NotNull(schema);
        Assert.Equal(3, schema.Rows.Count);

        Assert.Equal("the_answer", schema.Rows[0]["ColumnName"]);
        Assert.Equal(0, schema.Rows[0]["ColumnOrdinal"]);
        Assert.Equal("greeting", schema.Rows[1]["ColumnName"]);
        Assert.Equal(1, schema.Rows[1]["ColumnOrdinal"]);
        Assert.Equal("ratio", schema.Rows[2]["ColumnName"]);
        Assert.Equal(2, schema.Rows[2]["ColumnOrdinal"]);

        // DataType must be the CLR type the reader yields (long for DuckDB INTEGER).
        Assert.Equal(typeof(long), schema.Rows[0]["DataType"]);
        Assert.Equal(typeof(string), schema.Rows[1]["DataType"]);
        Assert.Equal(typeof(double), schema.Rows[2]["DataType"]);
    }

    /// <summary>
    /// DECIMAL precision/scale from the wire type_info must propagate into the schema table so
    /// tooling can reconstruct exact column definitions.
    /// </summary>
    [Fact]
    public async Task GetSchemaTable_NumericPrecisionScale_ForDecimal()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CAST(1234.56 AS DECIMAL(10,2)) AS amount";

        await using var reader = await command.ExecuteReaderAsync();
        var schema = reader.GetSchemaTable()!;

        Assert.Equal((byte)10, schema.Rows[0]["NumericPrecision"]);
        Assert.Equal((byte)2, schema.Rows[0]["NumericScale"]);
    }

    /// <summary>
    /// Fixed-width types must report their byte width via ColumnSize; variable-length types fall
    /// back to -1 (unknown).
    /// </summary>
    [Fact]
    public async Task GetSchemaTable_ColumnSize_ForFixedWidthTypes()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CAST(1 AS INTEGER) AS i, CAST(1 AS BIGINT) AS b, CAST('00000000-0000-0000-0000-000000000000' AS UUID) AS u, 'free' AS s";

        await using var reader = await command.ExecuteReaderAsync();
        var schema = reader.GetSchemaTable()!;

        Assert.Equal(4, schema.Rows[0]["ColumnSize"]);   // INTEGER
        Assert.Equal(8, schema.Rows[1]["ColumnSize"]);   // BIGINT
        Assert.Equal(16, schema.Rows[2]["ColumnSize"]);  // UUID
        Assert.Equal(-1, schema.Rows[3]["ColumnSize"]);  // VARCHAR — unbounded
    }

    /// <summary>
    /// CommandBehavior.SchemaOnly must expose column metadata but yield no data rows — the ADO.NET
    /// contract for schema-only enumeration.
    /// </summary>
    [Fact]
    public async Task CommandBehavior_SchemaOnly_ReportsSchemaWithoutRows()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT i, i * 2 AS doubled FROM range(0, 1000) t(i)";

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SchemaOnly);

        // Schema is still available up front.
        Assert.Equal(2, reader.FieldCount);
        Assert.Equal("doubled", reader.GetName(1));

        // But no rows are surfaced, regardless of how large the underlying result set is.
        Assert.False(await reader.ReadAsync());
    }

    /// <summary>
    /// CommandBehavior.SingleRow must yield exactly one row then signal EOF, even when the query
    /// matches many.
    /// </summary>
    [Fact]
    public async Task CommandBehavior_SingleRow_YieldsExactlyOneRow()
    {
        await using var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT i FROM range(0, 5000) t(i) ORDER BY i";

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow);

        Assert.True(await reader.ReadAsync());
        Assert.Equal(0L, reader.GetInt64(0));

        // Only one row is exposed, even though the server streamed thousands.
        Assert.False(await reader.ReadAsync());
    }

    /// <summary>
    /// CommandBehavior.CloseConnection binds the connection lifetime to the reader's: disposing the
    /// reader must tear the connection down without the caller closing it explicitly.
    /// </summary>
    [Fact]
    public async Task CommandBehavior_CloseConnection_ClosesConnectionWhenReaderDisposed()
    {
        var connection = new QuackConnection(_options.ConnectionString);
        await connection.OpenAsync();
        Assert.Equal(ConnectionState.Open, connection.State);

        await using (connection)
        {
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT 1";
                await using (var reader = await command.ExecuteReaderAsync(CommandBehavior.CloseConnection))
                {
                    Assert.True(await reader.ReadAsync());
                } // reader disposed here
            }

            // The reader carried the connection with it.
            Assert.Equal(ConnectionState.Closed, connection.State);
        }
    }
}
