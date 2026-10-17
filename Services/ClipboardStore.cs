using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace ZTool.Services;

public enum ClipboardKind
{
    Text,
    Image,
}

/// <summary>One thing that was copied. <see cref="Content"/> is the body or the image file's name.</summary>
public sealed record ClipboardEntry(
    long Id,
    ClipboardKind Kind,
    string Content,
    string Preview,
    bool Pinned,
    long CreatedAt,
    long LastUsedAt,
    string SourceProcess);

/// <summary>
/// The history itself: one file, one table, one unique key per piece of content.
/// Copying something already known moves it to the top rather than making a
/// second row — a history is a timeline of what you wanted, not of every click.
/// </summary>
public sealed class ClipboardStore : IDisposable
{
    private const int SchemaVersion = 1;

    private readonly object _gate = new();
    private readonly SqliteConnection _connection;

    public ClipboardStore()
        : this(Path.Combine(SettingsStore.Folder, "clipboard.db"))
    {
    }

    /// <summary>Takes a path so the history can be exercised against a scratch file.</summary>
    public ClipboardStore(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        _connection = new SqliteConnection($"Data Source={databasePath};Cache=Shared");
        _connection.Open();

        using (var command = _connection.CreateCommand())
        {
            command.CommandText =
                """
                PRAGMA journal_mode = WAL;
                CREATE TABLE IF NOT EXISTS entries (
                    id             INTEGER PRIMARY KEY,
                    kind           TEXT    NOT NULL,
                    content        TEXT    NOT NULL,
                    preview        TEXT    NOT NULL,
                    pinned         INTEGER NOT NULL DEFAULT 0,
                    created_at     INTEGER NOT NULL,
                    last_used_at   INTEGER NOT NULL,
                    source_process TEXT    NOT NULL DEFAULT '',
                    content_hash   TEXT    NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ux_entries_hash ON entries (content_hash);
                CREATE INDEX IF NOT EXISTS ix_entries_last_used ON entries (last_used_at);
                CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                """;
            command.ExecuteNonQuery();
        }

        using var version = _connection.CreateCommand();
        version.CommandText = "INSERT INTO meta (key, value) VALUES ('schema_version', $v) ON CONFLICT(key) DO UPDATE SET value = $v";
        version.Parameters.AddWithValue("$v", SchemaVersion.ToString());
        version.ExecuteNonQuery();
    }

    /// <summary>
    /// Records a copy. Known content moves to the top and keeps whatever it was
    /// pinned to; new content lands on top and gets the eviction pass.
    /// </summary>
    public ClipboardEntry Upsert(ClipboardKind kind, string content, string preview, string contentHash, string sourceProcess, int capacity)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            using (var find = _connection.CreateCommand())
            {
                find.CommandText = "SELECT id FROM entries WHERE content_hash = $h";
                find.Parameters.AddWithValue("$h", contentHash);

                if (find.ExecuteScalar() is long existing)
                {
                    using (var touch = _connection.CreateCommand())
                    {
                        touch.CommandText = "UPDATE entries SET last_used_at = $t, source_process = CASE WHEN source_process = '' THEN $p ELSE source_process END WHERE id = $id";
                        touch.Parameters.AddWithValue("$t", now);
                        touch.Parameters.AddWithValue("$p", sourceProcess);
                        touch.Parameters.AddWithValue("$id", existing);
                        touch.ExecuteNonQuery();
                    }

                    return Find(existing) ?? throw new InvalidOperationException("entry vanished between update and read");
                }
            }

            long inserted;
            using (var insert = _connection.CreateCommand())
            {
                insert.CommandText =
                    """
                    INSERT INTO entries (kind, content, preview, pinned, created_at, last_used_at, source_process, content_hash)
                    VALUES ($k, $c, $p, 0, $t, $t, $s, $h)
                    RETURNING id
                    """;
                insert.Parameters.AddWithValue("$k", kind == ClipboardKind.Text ? "text" : "image");
                insert.Parameters.AddWithValue("$c", content);
                insert.Parameters.AddWithValue("$p", preview);
                insert.Parameters.AddWithValue("$t", now);
                insert.Parameters.AddWithValue("$s", sourceProcess);
                insert.Parameters.AddWithValue("$h", contentHash);
                inserted = Convert.ToInt64(insert.ExecuteScalar());
            }

            Evict(capacity, inserted);
            return Find(inserted)!;
        }
    }

    /// <summary>
    /// Pinned first, then newest use. Limit is negative for "all of it".
    /// </summary>
    public IReadOnlyList<ClipboardEntry> Query(string? search, int limit)
    {
        lock (_gate)
        {
            var rows = new List<ClipboardEntry>();

            using var command = _connection.CreateCommand();
            command.CommandText =
                """
                SELECT id, kind, content, preview, pinned, created_at, last_used_at, source_process
                FROM entries
                WHERE ($q = '' OR content LIKE $like OR preview LIKE $like)
                ORDER BY pinned DESC, last_used_at DESC, id DESC
                LIMIT $limit
                """;

            var needle = string.IsNullOrEmpty(search) ? string.Empty : search.Trim();
            command.Parameters.AddWithValue("$q", needle);
            command.Parameters.AddWithValue("$like", $"%{needle}%");
            command.Parameters.AddWithValue("$limit", limit < 0 ? -1 : limit);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(Read(reader));
            }

            return rows;
        }
    }

    public void Touch(long id)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "UPDATE entries SET last_used_at = $t WHERE id = $id";
            command.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }
    }

    public void SetPinned(long id, bool pinned)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "UPDATE entries SET pinned = $v WHERE id = $id";
            command.Parameters.AddWithValue("$v", pinned ? 1 : 0);
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Removes one entry and hands back anything on disk it owned.</summary>
    public string? Delete(long id)
    {
        lock (_gate)
        {
            var owned = Find(id)?.Content;

            using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM entries WHERE id = $id";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();

            return owned;
        }
    }

    /// <summary>Every image filename still referenced — anything else on disk is an orphan.</summary>
    public IReadOnlyList<string> ReferencedImages()
    {
        lock (_gate)
        {
            var names = new List<string>();

            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT content FROM entries WHERE kind = 'image'";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }

            return names;
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                using var command = _connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM entries";
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }
    }

    /// <summary>
    /// Keeps the table at or under <paramref name="capacity"/> by dropping the
    /// oldest unpinned rows. Pinned rows are never the ones that go, so a table
    /// of all-pinned entries simply stays over — pinning is a decision. And the
    /// row that triggered the pass is never it: you just copied that one.
    /// </summary>
    private void Evict(int capacity, long inserted)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM entries WHERE id IN (
                SELECT id FROM entries WHERE pinned = 0 AND id <> $fresh
                ORDER BY last_used_at ASC, id ASC
                LIMIT MAX(0, (SELECT COUNT(*) FROM entries) - $cap)
            )
            """;
        command.Parameters.AddWithValue("$cap", capacity);
        command.Parameters.AddWithValue("$fresh", inserted);
        command.ExecuteNonQuery();
    }

    public ClipboardEntry? Find(long id)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, kind, content, preview, pinned, created_at, last_used_at, source_process
            FROM entries WHERE id = $id
            """;
        command.Parameters.AddWithValue("$id", id);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    private static ClipboardEntry Read(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1) == "text" ? ClipboardKind.Text : ClipboardKind.Image,
        reader.GetString(2),
        reader.GetString(3),
        reader.GetInt64(4) != 0,
        reader.GetInt64(5),
        reader.GetInt64(6),
        reader.GetString(7));

    public void Dispose()
    {
        lock (_gate)
        {
            _connection.Dispose();
        }
    }
}
