using System.Text.Json;
using Npgsql;

namespace FiveHead.State.Api;

public sealed class MatterRepository(NpgsqlDataSource dataSource)
{
    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);
    private const string MatterColumns = """
        id, title, owner, status, next_step, metadata::text, vector_ref, version,
        claimed_by, claim_expires_at, created_at, updated_at
        """;

    public async Task<bool> PingAsync(CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand("SELECT 1;", connection);
        var result = await command.ExecuteScalarAsync(ct);
        return result is not null;
    }

    public async Task<IReadOnlyList<Matter>> ListAsync(string? status, string? owner, int limit, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(
            $"""
            SELECT {MatterColumns}
            FROM matters
            WHERE ($1::text IS NULL OR status = $1)
              AND ($2::text IS NULL OR owner = $2)
            ORDER BY updated_at DESC, id
            LIMIT $3;
            """,
            connection);
        command.Parameters.AddWithValue(string.IsNullOrWhiteSpace(status) ? DBNull.Value : status.Trim());
        command.Parameters.AddWithValue(string.IsNullOrWhiteSpace(owner) ? DBNull.Value : owner.Trim());
        command.Parameters.AddWithValue(limit);
        return await ReadMattersAsync(command, ct);
    }

    public async Task<Matter?> GetAsync(Guid id, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(
            $"SELECT {MatterColumns} FROM matters WHERE id = $1;",
            connection);
        command.Parameters.AddWithValue(id);
        var rows = await ReadMattersAsync(command, ct);
        return rows.Count == 0 ? null : rows[0];
    }

    public async Task<IReadOnlyList<MatterEvent>> ListEventsAsync(Guid matterId, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT id, matter_id, agent, kind, summary, payload::text, created_at
            FROM events
            WHERE matter_id = $1
            ORDER BY created_at, id;
            """,
            connection);
        command.Parameters.AddWithValue(matterId);
        var events = new List<MatterEvent>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            events.Add(ReadEvent(reader));
        }

        return events;
    }

    public async Task<Matter> CreateAsync(
        Guid id,
        string title,
        string owner,
        string status,
        string? nextStep,
        string metadataJson,
        string? vectorRef,
        CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(
            $"""
            INSERT INTO matters (id, title, owner, status, next_step, metadata, vector_ref)
            VALUES ($1, $2, $3, $4, $5, $6::jsonb, $7)
            RETURNING {MatterColumns};
            """,
            connection);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(title);
        command.Parameters.AddWithValue(owner);
        command.Parameters.AddWithValue(status);
        command.Parameters.AddWithValue((object?)nextStep ?? DBNull.Value);
        command.Parameters.AddWithValue(metadataJson);
        command.Parameters.AddWithValue((object?)vectorRef ?? DBNull.Value);
        var rows = await ReadMattersAsync(command, ct);
        return rows[0];
    }

    public async Task<MutationResult> UpdateAsync(Guid id, MatterPatch patch, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var current = await LockMatterAsync(connection, tx, id, ct);
        if (current is null)
        {
            await tx.CommitAsync(ct);
            return MutationResult.NotFound($"Matter {id} does not exist.");
        }

        if (current.Version != patch.Version)
        {
            await tx.CommitAsync(ct);
            return MutationResult.Conflict(
                current,
                $"Version conflict on matter {id}. Expected {patch.Version}, current is {current.Version}. Re-read and retry.");
        }

        var nextStatus = patch.HasStatus ? patch.Status ?? current.Status : current.Status;
        var nextOwner = patch.HasOwner ? patch.Owner ?? current.Owner : current.Owner;
        var statusChanged = !string.Equals(nextStatus, current.Status, StringComparison.Ordinal);
        var ownerChanged = !string.Equals(nextOwner, current.Owner, StringComparison.Ordinal);
        if ((statusChanged || ownerChanged)
            && !string.Equals(patch.Agent, current.Owner, StringComparison.Ordinal))
        {
            await tx.CommitAsync(ct);
            return MutationResult.Forbidden(
                current,
                $"Only the owner ({current.Owner}) may change status or owner.");
        }

        await using var command = new NpgsqlCommand(
            $"""
            UPDATE matters
            SET title = $3,
                owner = $4,
                status = $5,
                next_step = $6,
                metadata = $7::jsonb,
                vector_ref = $8,
                version = version + 1,
                updated_at = now()
            WHERE id = $1 AND version = $2
            RETURNING {MatterColumns};
            """,
            connection,
            tx);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(patch.Version);
        command.Parameters.AddWithValue(patch.HasTitle ? patch.Title ?? current.Title : current.Title);
        command.Parameters.AddWithValue(nextOwner);
        command.Parameters.AddWithValue(nextStatus);
        command.Parameters.AddWithValue((object?)(patch.HasNextStep ? patch.NextStep : current.NextStep) ?? DBNull.Value);
        command.Parameters.AddWithValue(patch.HasMetadata ? patch.MetadataJson ?? "{}" : current.MetadataJson);
        command.Parameters.AddWithValue((object?)(patch.HasVectorRef ? patch.VectorRef : current.VectorRef) ?? DBNull.Value);
        var updated = await ReadMattersAsync(command, ct);
        await tx.CommitAsync(ct);
        if (updated.Count == 0)
        {
            var latest = await GetAsync(id, ct);
            return latest is null
                ? MutationResult.NotFound($"Matter {id} does not exist.")
                : MutationResult.Conflict(latest, $"Version conflict on matter {id}. Re-read and retry.");
        }

        return MutationResult.Ok(updated[0]);
    }

    public async Task<MatterEvent?> AppendEventAsync(
        Guid matterId,
        string agent,
        string kind,
        string summary,
        string payloadJson,
        CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO events (matter_id, agent, kind, summary, payload)
            SELECT $1, $2, $3, $4, $5::jsonb
            WHERE EXISTS (SELECT 1 FROM matters WHERE id = $1)
            RETURNING id, matter_id, agent, kind, summary, payload::text, created_at;
            """,
            connection);
        command.Parameters.AddWithValue(matterId);
        command.Parameters.AddWithValue(agent);
        command.Parameters.AddWithValue(kind);
        command.Parameters.AddWithValue(summary);
        command.Parameters.AddWithValue(payloadJson);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return ReadEvent(reader);
    }

    public async Task<Matter?> ClaimNextAsync(string agent, int ttlSeconds, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(
            $"""
            WITH candidate AS (
                SELECT id
                FROM matters
                WHERE status <> 'done'
                  AND status <> 'cancelled'
                  AND (claimed_by IS NULL OR claim_expires_at IS NULL OR claim_expires_at <= now())
                ORDER BY created_at, id
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            UPDATE matters AS m
            SET claimed_by = $1,
                claim_expires_at = now() + ($2 * interval '1 second'),
                version = m.version + 1,
                updated_at = now()
            FROM candidate
            WHERE m.id = candidate.id
            RETURNING m.id, m.title, m.owner, m.status, m.next_step, m.metadata::text, m.vector_ref, m.version,
                      m.claimed_by, m.claim_expires_at, m.created_at, m.updated_at;
            """,
            connection);
        command.Parameters.AddWithValue(agent);
        command.Parameters.AddWithValue(ttlSeconds);
        var rows = await ReadMattersAsync(command, ct);
        return rows.Count == 0 ? null : rows[0];
    }

    public async Task<MutationResult> ReleaseAsync(Guid id, string agent, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var current = await LockMatterAsync(connection, tx, id, ct);
        if (current is null)
        {
            await tx.CommitAsync(ct);
            return MutationResult.NotFound($"Matter {id} does not exist.");
        }

        var expired = current.ClaimExpiresAt is null || current.ClaimExpiresAt <= DateTimeOffset.UtcNow;
        var heldByAgent = string.Equals(current.ClaimedBy, agent, StringComparison.Ordinal);
        if (current.ClaimedBy is null || expired)
        {
            if (current.ClaimedBy is null)
            {
                await tx.CommitAsync(ct);
                return MutationResult.Ok(current);
            }
        }
        else if (!heldByAgent)
        {
            await tx.CommitAsync(ct);
            return MutationResult.Conflict(current, $"Matter {id} is claimed by {current.ClaimedBy}.", "claim_conflict");
        }

        await using var command = new NpgsqlCommand(
            $"""
            UPDATE matters
            SET claimed_by = NULL,
                claim_expires_at = NULL,
                version = version + 1,
                updated_at = now()
            WHERE id = $1
            RETURNING {MatterColumns};
            """,
            connection,
            tx);
        command.Parameters.AddWithValue(id);
        var rows = await ReadMattersAsync(command, ct);
        await tx.CommitAsync(ct);
        return MutationResult.Ok(rows[0]);
    }

    public async Task<MatterOverview> GetOverviewAsync(CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var byStatus = new List<StatusCount>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT status, COUNT(*)::int
            FROM matters
            GROUP BY status
            ORDER BY status;
            """,
            connection))
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                byStatus.Add(new StatusCount(reader.GetString(0), reader.GetInt32(1)));
            }
        }

        await using var claimsCommand = new NpgsqlCommand(
            $"""
            SELECT {MatterColumns}
            FROM matters
            WHERE claimed_by IS NOT NULL
            ORDER BY CASE
                         WHEN claim_expires_at IS NOT NULL AND claim_expires_at > now() THEN 0
                         ELSE 1
                     END,
                     claim_expires_at NULLS LAST,
                     title,
                     id;
            """,
            connection);
        var claimed = await ReadMattersAsync(claimsCommand, ct);
        var now = DateTimeOffset.UtcNow;
        var claims = claimed.Select(matter => new ClaimSnapshot(
            matter.Id,
            matter.Title,
            matter.Owner,
            matter.Status,
            matter.ClaimedBy ?? "",
            matter.ClaimExpiresAt,
            matter.ClaimExpiresAt is not null && matter.ClaimExpiresAt > now,
            matter.Version)).ToArray();

        return new MatterOverview(
            byStatus.Sum(row => row.Count),
            byStatus,
            claims.Count(row => row.Active),
            claims.Count(row => !row.Active),
            claims);
    }

    public async Task<MutationResult> AdminUpdateAsync(
        Guid id,
        long version,
        bool hasStatus,
        string? status,
        bool hasOwner,
        string? owner,
        bool hasNextStep,
        string? nextStep,
        CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var current = await LockMatterAsync(connection, tx, id, ct);
        if (current is null)
        {
            await tx.CommitAsync(ct);
            return MutationResult.NotFound($"Matter {id} does not exist.");
        }

        if (current.Version != version)
        {
            await tx.CommitAsync(ct);
            return MutationResult.Conflict(
                current,
                $"Version conflict on matter {id}. Expected {version}, current is {current.Version}. Re-read and retry.");
        }

        var nextStatus = hasStatus ? status ?? current.Status : current.Status;
        var nextOwner = hasOwner ? owner ?? current.Owner : current.Owner;
        var nextNext = hasNextStep ? nextStep : current.NextStep;
        var changed = !string.Equals(nextStatus, current.Status, StringComparison.Ordinal)
            || !string.Equals(nextOwner, current.Owner, StringComparison.Ordinal)
            || !string.Equals(nextNext, current.NextStep, StringComparison.Ordinal);
        if (!changed)
        {
            await tx.CommitAsync(ct);
            return MutationResult.Ok(current);
        }

        await using var command = new NpgsqlCommand(
            $"""
            UPDATE matters
            SET owner = $3,
                status = $4,
                next_step = $5,
                version = version + 1,
                updated_at = now()
            WHERE id = $1 AND version = $2
            RETURNING {MatterColumns};
            """,
            connection,
            tx);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(version);
        command.Parameters.AddWithValue(nextOwner);
        command.Parameters.AddWithValue(nextStatus);
        command.Parameters.AddWithValue((object?)nextNext ?? DBNull.Value);
        var updated = await ReadMattersAsync(command, ct);
        if (updated.Count == 0)
        {
            await tx.CommitAsync(ct);
            var latest = await GetAsync(id, ct);
            return latest is null
                ? MutationResult.NotFound($"Matter {id} does not exist.")
                : MutationResult.Conflict(latest, $"Version conflict on matter {id}. Re-read and retry.");
        }

        var matter = updated[0];
        await InsertEventAsync(
            connection,
            tx,
            id,
            AdminActions.Actor,
            AdminActions.UpdateKind,
            DescribeAdminUpdate(current, matter),
            JsonSerializer.Serialize(new
            {
                before = new { status = current.Status, owner = current.Owner, nextStep = current.NextStep },
                after = new { status = matter.Status, owner = matter.Owner, nextStep = matter.NextStep }
            }, PayloadJson),
            ct);
        await tx.CommitAsync(ct);
        return MutationResult.Ok(matter);
    }

    public async Task<MutationResult> ForceReleaseAsync(Guid id, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var current = await LockMatterAsync(connection, tx, id, ct);
        if (current is null)
        {
            await tx.CommitAsync(ct);
            return MutationResult.NotFound($"Matter {id} does not exist.");
        }

        if (current.ClaimedBy is null)
        {
            await tx.CommitAsync(ct);
            return MutationResult.Conflict(current, $"Matter {id} is not claimed.", "not_claimed");
        }

        var expired = current.ClaimExpiresAt is null || current.ClaimExpiresAt <= DateTimeOffset.UtcNow;
        var heldBy = current.ClaimedBy;
        await using var command = new NpgsqlCommand(
            $"""
            UPDATE matters
            SET claimed_by = NULL,
                claim_expires_at = NULL,
                version = version + 1,
                updated_at = now()
            WHERE id = $1
            RETURNING {MatterColumns};
            """,
            connection,
            tx);
        command.Parameters.AddWithValue(id);
        var updated = await ReadMattersAsync(command, ct);
        await InsertEventAsync(
            connection,
            tx,
            id,
            AdminActions.Actor,
            AdminActions.ReleaseKind,
            $"Cleared claim held by {heldBy}.",
            JsonSerializer.Serialize(new
            {
                claimedBy = heldBy,
                claimExpiresAt = current.ClaimExpiresAt,
                expired
            }, PayloadJson),
            ct);
        await tx.CommitAsync(ct);
        return MutationResult.Ok(updated[0]);
    }

    private static string DescribeAdminUpdate(Matter before, Matter after)
    {
        var parts = new List<string>();
        if (!string.Equals(before.Status, after.Status, StringComparison.Ordinal))
        {
            parts.Add($"status {before.Status} -> {after.Status}");
        }

        if (!string.Equals(before.Owner, after.Owner, StringComparison.Ordinal))
        {
            parts.Add($"owner {before.Owner} -> {after.Owner}");
        }

        if (!string.Equals(before.NextStep, after.NextStep, StringComparison.Ordinal))
        {
            parts.Add($"next step {Show(before.NextStep)} -> {Show(after.NextStep)}");
        }

        return parts.Count == 0 ? "No fields changed." : string.Join("; ", parts);
    }

    private static string Show(string? value) => string.IsNullOrEmpty(value) ? "(none)" : value;

    private static async Task InsertEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        Guid matterId,
        string agent,
        string kind,
        string summary,
        string payloadJson,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO events (matter_id, agent, kind, summary, payload)
            VALUES ($1, $2, $3, $4, $5::jsonb);
            """,
            connection,
            tx);
        command.Parameters.AddWithValue(matterId);
        command.Parameters.AddWithValue(agent);
        command.Parameters.AddWithValue(kind);
        command.Parameters.AddWithValue(summary);
        command.Parameters.AddWithValue(payloadJson);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<Matter?> LockMatterAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        Guid id,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT {MatterColumns} FROM matters WHERE id = $1 FOR UPDATE;",
            connection,
            tx);
        command.Parameters.AddWithValue(id);
        var rows = await ReadMattersAsync(command, ct);
        return rows.Count == 0 ? null : rows[0];
    }

    private static async Task<List<Matter>> ReadMattersAsync(NpgsqlCommand command, CancellationToken ct)
    {
        var rows = new List<Matter>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new Matter(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetInt64(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
                reader.GetFieldValue<DateTimeOffset>(10),
                reader.GetFieldValue<DateTimeOffset>(11)));
        }

        return rows;
    }

    private static MatterEvent ReadEvent(NpgsqlDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetGuid(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetFieldValue<DateTimeOffset>(6));
}
