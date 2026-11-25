using System.Reflection;
using Npgsql;

namespace FiveHead.State.Api;

public static class MigrationRunner
{
    public static async Task ApplyAsync(NpgsqlDataSource dataSource, CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using (var ensure = new NpgsqlCommand(
            """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                id text PRIMARY KEY,
                applied_at timestamptz NOT NULL DEFAULT now()
            );
            """,
            connection))
        {
            await ensure.ExecuteNonQueryAsync(ct);
        }

        var assembly = typeof(MigrationRunner).Assembly;
        var scripts = assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (scripts.Length == 0)
        {
            throw new InvalidOperationException("No SQL migrations were embedded in the state API.");
        }

        foreach (var name in scripts)
        {
            var id = name.Split('.').Reverse().Skip(1).First();
            await using var exists = new NpgsqlCommand(
                "SELECT 1 FROM schema_migrations WHERE id = $1;",
                connection);
            exists.Parameters.AddWithValue(id);
            var applied = await exists.ExecuteScalarAsync(ct);
            if (applied is not null)
            {
                continue;
            }

            await using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Missing migration resource {name}.");
            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync(ct);

            await using var tx = await connection.BeginTransactionAsync(ct);
            await using (var apply = new NpgsqlCommand(sql, connection, tx))
            {
                await apply.ExecuteNonQueryAsync(ct);
            }

            await using (var record = new NpgsqlCommand(
                "INSERT INTO schema_migrations (id) VALUES ($1);",
                connection,
                tx))
            {
                record.Parameters.AddWithValue(id);
                await record.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
    }
}
