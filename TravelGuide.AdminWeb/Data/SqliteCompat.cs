using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Microsoft.Data.Sqlite;

public sealed class SqliteConnection : IAsyncDisposable
{
    private readonly SqlConnection _inner;

    public SqliteConnection(string connectionString)
    {
        _inner = new SqlConnection(connectionString);
    }

    internal SqlConnection Inner => _inner;

    public Task OpenAsync() => _inner.OpenAsync();

    public SqliteCommand CreateCommand() => new(_inner.CreateCommand());

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}

public sealed class SqliteCommand : IAsyncDisposable
{
    private readonly SqlCommand _inner;

    internal SqliteCommand(SqlCommand inner)
    {
        _inner = inner;
        Parameters = new SqliteParameterCollection(_inner.Parameters);
    }

    public string CommandText
    {
        get => _inner.CommandText;
        set => _inner.CommandText = value;
    }

    public SqliteParameterCollection Parameters { get; }

    private void NormalizeSql()
    {
        var sql = _inner.CommandText ?? string.Empty;
        sql = Regex.Replace(sql, @"\$(\w+)", "@$1");
        sql = sql.Replace("SELECT last_insert_rowid();", "SELECT CAST(SCOPE_IDENTITY() AS INT);", StringComparison.OrdinalIgnoreCase);
        if (Regex.IsMatch(sql, @"\bLIMIT\s+1\b", RegexOptions.IgnoreCase))
        {
            sql = Regex.Replace(sql, @"\s+LIMIT\s+1\s*;?", "", RegexOptions.IgnoreCase);
            sql = Regex.Replace(sql, @"^\s*SELECT\s", "SELECT TOP 1 ", RegexOptions.IgnoreCase);
        }
        _inner.CommandText = sql;
    }

    public async Task<int> ExecuteNonQueryAsync()
    {
        NormalizeSql();
        return await _inner.ExecuteNonQueryAsync();
    }

    public async Task<object?> ExecuteScalarAsync()
    {
        NormalizeSql();
        return await _inner.ExecuteScalarAsync();
    }

    public async Task<SqliteDataReader> ExecuteReaderAsync()
    {
        NormalizeSql();
        return new SqliteDataReader(await _inner.ExecuteReaderAsync());
    }

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}

public sealed class SqliteParameterCollection
{
    private readonly SqlParameterCollection _inner;

    internal SqliteParameterCollection(SqlParameterCollection inner)
    {
        _inner = inner;
    }

    public void AddWithValue(string parameterName, object? value)
    {
        var normalized = parameterName.StartsWith("$", StringComparison.Ordinal)
            ? "@" + parameterName[1..]
            : parameterName;
        _inner.AddWithValue(normalized, value ?? DBNull.Value);
    }
}

public sealed class SqliteDataReader : IAsyncDisposable
{
    private readonly SqlDataReader _inner;

    internal SqliteDataReader(SqlDataReader inner)
    {
        _inner = inner;
    }

    public Task<bool> ReadAsync() => _inner.ReadAsync();

    public int GetInt32(int ordinal) => Convert.ToInt32(_inner.GetValue(ordinal));
    public long GetInt64(int ordinal) => Convert.ToInt64(_inner.GetValue(ordinal));
    public string GetString(int ordinal) => _inner.GetString(ordinal);
    public double GetDouble(int ordinal) => Convert.ToDouble(_inner.GetValue(ordinal));
    public DateTime GetDateTime(int ordinal) => Convert.ToDateTime(_inner.GetValue(ordinal));
    public bool IsDBNull(int ordinal) => _inner.IsDBNull(ordinal);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
