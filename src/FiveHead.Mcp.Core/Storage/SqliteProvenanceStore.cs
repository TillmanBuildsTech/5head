using FiveHead.Mcp.Core.Provenance;
using FiveHead.Mcp.Core.Serialization;
using Microsoft.Data.Sqlite;

namespace FiveHead.Mcp.Core.Storage;

public sealed class SqliteProvenanceStore(
    string databasePath,
    TimeProvider? timeProvider = null,
    TimeSpan? busyTimeout = null) : IProvenanceStore
{
    private readonly string _databasePath = Path.GetFullPath(databasePath);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan _busyTimeout = busyTimeout ?? TimeSpan.FromSeconds(5);

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = await OpenConnectionAsync(ct);
        await ExecuteNonQueryAsync(connection, "PRAGMA journal_mode=WAL;", null, ct);
        await ExecuteNonQueryAsync(connection,
            @"
            CREATE TABLE IF NOT EXISTS provenance (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                target TEXT NOT NULL,
                mode TEXT NOT NULL,
                source_kind TEXT NOT NULL,
                content TEXT NOT NULL,
                fact_keys TEXT NULL,
                thread_id TEXT NULL,
                task_id TEXT NULL,
                created_at TEXT NOT NULL,
                confirmed_at TEXT NULL,
                retracted_at TEXT NULL,
                retracts_id INTEGER NULL
            );

            CREATE INDEX IF NOT EXISTS idx_provenance_target ON provenance(target);
            CREATE INDEX IF NOT EXISTS idx_provenance_retracts_id ON provenance(retracts_id);",
            null,
            ct);
    }

    public async Task<long> RecordAsync(ProvenanceRow row, CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        return await InsertRowAsync(connection, row, null, ct);
    }

    public async Task<long> RecordAndRetractAsync(
        ProvenanceRow row,
        IReadOnlyList<FactRetraction> retractions,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await ExecuteNonQueryAsync(connection, "BEGIN IMMEDIATE;", null, ct);
        try
        {
            var id = await InsertRowAsync(connection, row, null, ct);
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            foreach (var retraction in retractions)
            {
                await RetractRowAsync(connection, retraction.Id, retraction.Reason, now, null, ct);
            }

            await ExecuteNonQueryAsync(connection, "COMMIT;", null, ct);
            return id;
        }
        catch
        {
            await RollbackAsync(connection);
            throw;
        }
    }

    public async Task ConfirmAsync(long id, CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await ExecuteNonQueryAsync(connection, "BEGIN IMMEDIATE;", null, ct);
        try
        {
            var row = await QuerySingleOrDefaultAsync(
                connection,
                SelectRowSql + " WHERE id = @Id;",
                new { Id = id },
                null,
                ct);

            if (row is null)
            {
                throw new InvalidOperationException($"Fact {id} does not exist.");
            }

            if (row.ConfirmedAt is null)
            {
                var now = _timeProvider.GetUtcNow().UtcDateTime;
                var updated = await ExecuteNonQueryAsync(
                    connection,
                    "UPDATE provenance SET confirmed_at = @Now WHERE id = @Id AND confirmed_at IS NULL;",
                    new { Id = id, Now = now },
                    ct);
                if (updated != 1 && row.ConfirmedAt is null)
                {
                    throw new InvalidOperationException($"Fact {id} could not be confirmed.");
                }
            }

            await ExecuteNonQueryAsync(connection, "COMMIT;", null, ct);
        }
        catch
        {
            await RollbackAsync(connection);
            throw;
        }
    }

    public async Task RetractAsync(long id, string reason, CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        await ExecuteNonQueryAsync(connection, "BEGIN IMMEDIATE;", null, ct);
        try
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            await RetractRowAsync(connection, id, reason, now, null, ct);
            await ExecuteNonQueryAsync(connection, "COMMIT;", null, ct);
        }
        catch
        {
            await RollbackAsync(connection);
            throw;
        }
    }

    public async Task<ProvenanceRow?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        return await QuerySingleOrDefaultAsync(
            connection,
            SelectRowSql + " WHERE id = @Id;",
            new { Id = id },
            null,
            ct);
    }

    public async Task<IReadOnlyList<ProvenanceRow>> GetHistoryAsync(string subject, string predicate, CancellationToken ct = default)
    {
        var rows = await ListAsync(null, ct);
        var matchingIds = rows
            .Where(row => RowContainsFact(row, subject, predicate))
            .Select(row => row.Id)
            .ToHashSet();

        return rows
            .Where(row => matchingIds.Contains(row.Id) || (row.RetractsId is long retractsId && matchingIds.Contains(retractsId)))
            .OrderBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .ToArray();
    }

    public async Task<IReadOnlyList<ProvenanceRow>> ListAsync(string? targetFile = null, CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        await using var connection = await OpenConnectionAsync(ct);
        var rows = await QueryAsync(
            connection,
            @"
            SELECT id, target, mode, source_kind AS SourceKind, content, fact_keys AS FactKeys,
                   thread_id AS ThreadId, task_id AS TaskId, created_at AS CreatedAt,
                   confirmed_at AS ConfirmedAt, retracted_at AS RetractedAt, retracts_id AS RetractsId
            FROM provenance
            WHERE @Target IS NULL OR target = @Target
            ORDER BY created_at, id;",
            new { Target = targetFile },
            null,
            ct);

        return rows;
    }

    private const string SelectRowSql =
        "SELECT id, target, mode, source_kind AS SourceKind, content, fact_keys AS FactKeys, thread_id AS ThreadId, task_id AS TaskId, created_at AS CreatedAt, confirmed_at AS ConfirmedAt, retracted_at AS RetractedAt, retracts_id AS RetractsId FROM provenance";

    private async Task<long> InsertRowAsync(
        SqliteConnection connection,
        ProvenanceRow row,
        SqliteTransaction? transaction,
        CancellationToken ct)
    {
        return await ExecuteScalarAsync<long>(connection,
            @"
            INSERT INTO provenance (
                target, mode, source_kind, content, fact_keys, thread_id, task_id,
                created_at, confirmed_at, retracted_at, retracts_id)
            VALUES (
                @Target, @Mode, @SourceKind, @Content, @FactKeys, @ThreadId, @TaskId,
                @CreatedAt, @ConfirmedAt, @RetractedAt, @RetractsId);

            SELECT last_insert_rowid();",
            new
            {
                row.Target,
                row.Mode,
                row.SourceKind,
                row.Content,
                row.FactKeys,
                row.ThreadId,
                row.TaskId,
                CreatedAt = row.CreatedAt,
                ConfirmedAt = row.ConfirmedAt,
                RetractedAt = row.RetractedAt,
                RetractsId = row.RetractsId
            },
            ct,
            transaction);
    }

    private async Task RetractRowAsync(
        SqliteConnection connection,
        long id,
        string reason,
        DateTime now,
        SqliteTransaction? transaction,
        CancellationToken ct)
    {
        var row = await QuerySingleOrDefaultAsync(
            connection,
            SelectRowSql + " WHERE id = @Id;",
            new { Id = id },
            transaction,
            ct);

        if (row is null)
        {
            throw new InvalidOperationException($"Fact {id} does not exist.");
        }

        if (row.RetractedAt is not null)
        {
            throw new InvalidOperationException($"Fact {id} is already retracted.");
        }

        var updated = await ExecuteNonQueryAsync(
            connection,
            "UPDATE provenance SET retracted_at = @Now WHERE id = @Id AND retracted_at IS NULL;",
            new { Id = id, Now = now },
            ct,
            transaction);
        if (updated != 1)
        {
            throw new InvalidOperationException($"Fact {id} is already retracted.");
        }

        await ExecuteNonQueryAsync(
            connection,
            @"
            INSERT INTO provenance (
                target, mode, source_kind, content, fact_keys, thread_id, task_id,
                created_at, confirmed_at, retracted_at, retracts_id)
            VALUES (
                @Target, @Mode, @SourceKind, @Content, NULL, NULL, NULL,
                @CreatedAt, NULL, NULL, @RetractsId);",
            new
            {
                Target = row.Target,
                Mode = "retract",
                SourceKind = "system",
                Content = reason,
                CreatedAt = now,
                RetractsId = id
            },
            ct,
            transaction);
    }

    private static async Task RollbackAsync(SqliteConnection connection)
    {
        try
        {
            await ExecuteNonQueryAsync(connection, "ROLLBACK;", null, CancellationToken.None);
        }
        catch (SqliteException)
        {
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = Math.Max(1, (int)Math.Ceiling(_busyTimeout.TotalSeconds)),
            Pooling = true
        };
        var connection = new SqliteConnection(builder.ConnectionString);
        await connection.OpenAsync(ct);
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = $"PRAGMA busy_timeout={Math.Max(0, (int)_busyTimeout.TotalMilliseconds)};";
        await pragma.ExecuteNonQueryAsync(ct);
        return connection;
    }

    private static bool RowContainsFact(ProvenanceRow row, string subject, string predicate)
    {
        if (string.IsNullOrWhiteSpace(row.FactKeys))
        {
            return false;
        }

        var facts = System.Text.Json.JsonSerializer.Deserialize(row.FactKeys, CoreJsonSerializerContext.Default.FactKeyArray) ?? [];
        return facts.Any(fact =>
            string.Equals(fact.Subject, subject, StringComparison.OrdinalIgnoreCase)
            && string.Equals(fact.Predicate, predicate, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<int> ExecuteNonQueryAsync(
        SqliteConnection connection,
        string sql,
        object? parameters,
        CancellationToken ct,
        SqliteTransaction? transaction = null)
    {
        await using var command = CreateCommand(connection, sql, parameters, transaction);
        return await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<T> ExecuteScalarAsync<T>(
        SqliteConnection connection,
        string sql,
        object? parameters,
        CancellationToken ct,
        SqliteTransaction? transaction = null)
    {
        await using var command = CreateCommand(connection, sql, parameters, transaction);
        var result = await command.ExecuteScalarAsync(ct);
        return result is T typed
            ? typed
            : (T)Convert.ChangeType(result!, typeof(T));
    }

    private static async Task<ProvenanceRow?> QuerySingleOrDefaultAsync(
        SqliteConnection connection,
        string sql,
        object? parameters,
        SqliteTransaction? transaction,
        CancellationToken ct)
    {
        var rows = await QueryAsync(connection, sql, parameters, transaction, ct);
        return rows.Count switch
        {
            0 => null,
            1 => rows[0],
            _ => throw new InvalidOperationException("Expected a single row.")
        };
    }

    private static async Task<IReadOnlyList<ProvenanceRow>> QueryAsync(
        SqliteConnection connection,
        string sql,
        object? parameters,
        SqliteTransaction? transaction,
        CancellationToken ct)
    {
        await using var command = CreateCommand(connection, sql, parameters, transaction);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var rows = new List<ProvenanceRow>();
        while (await reader.ReadAsync(ct))
        {
            rows.Add(MapRow(reader));
        }

        return rows;
    }

    private static SqliteCommand CreateCommand(
        SqliteConnection connection,
        string sql,
        object? parameters,
        SqliteTransaction? transaction)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;

        if (parameters is not null)
        {
            foreach (var property in parameters.GetType().GetProperties())
            {
                var value = property.GetValue(parameters) ?? DBNull.Value;
                command.Parameters.AddWithValue($"@{property.Name}", value);
            }
        }

        return command;
    }

    private static ProvenanceRow MapRow(SqliteDataReader reader)
    {
        return new ProvenanceRow
        {
            Id = reader.GetInt64(0),
            Target = reader.GetString(1),
            Mode = reader.GetString(2),
            SourceKind = reader.GetString(3),
            Content = reader.GetString(4),
            FactKeys = reader.IsDBNull(5) ? null : reader.GetString(5),
            ThreadId = reader.IsDBNull(6) ? null : reader.GetString(6),
            TaskId = reader.IsDBNull(7) ? null : reader.GetString(7),
            CreatedAt = reader.GetDateTime(8),
            ConfirmedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
            RetractedAt = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
            RetractsId = reader.IsDBNull(11) ? null : reader.GetInt64(11)
        };
    }
}
