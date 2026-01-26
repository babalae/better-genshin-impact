using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace BetterGenshinImpact.Core.Config;

internal static class UserStorage
{
    private const string DatabaseFileName = "config.db";
    private const string LegacyConfigFileName = "config.json";
    private const string UserFilesTable = "user_files";
    private const string MetaTable = "user_meta";
    private const string ConfigEntriesTable = "config_entries";
    private const string ConfigEntriesKeyColumn = "config_key";
    private const string IgnoreLegacyConfigMetaKey = "ignore_legacy_config";
    private static readonly object InitLock = new();
    private static readonly ReaderWriterLockSlim Lock = new();
    private static bool _initialized;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".json", ".json5", ".js", ".mjs", ".ts", ".lua", ".md", ".html", ".htm", ".css", ".xml", ".csv", ".yaml", ".yml", ".ini", ".config"
    };

    public static string DatabasePath => Path.Combine(Global.UserDataRoot, DatabaseFileName);

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        lock (InitLock)
        {
            if (_initialized)
            {
                return;
            }

            using var connection = OpenConnection();
            EnsureSchema(connection);
            MigrateFromLegacyTable(connection);
            MigrateFromDisk(connection);
            DeleteIgnoredEntries(connection);
            _initialized = true;
        }
    }

    public static bool TryReadText(string path, out string? content)
    {
        content = null;
        if (!TryNormalizeUserPath(path, out var normalized))
        {
            return false;
        }

        if (IsIgnoredPath(normalized))
        {
            return false;
        }

        Initialize();
        Lock.EnterReadLock();
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT content, is_text FROM {UserFilesTable} WHERE path = $path;";
            command.Parameters.AddWithValue("$path", normalized);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return false;
            }

            var isText = reader.GetInt32(1) == 1;
            if (!isText)
            {
                return false;
            }

            var bytes = (byte[])reader["content"];
            content = Encoding.UTF8.GetString(bytes);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            Lock.ExitReadLock();
        }
    }

    public static bool TryReadBytes(string path, out byte[]? content, out DateTimeOffset? updatedUtc)
    {
        content = null;
        updatedUtc = null;
        if (!TryNormalizeUserPath(path, out var normalized))
        {
            return false;
        }

        if (IsIgnoredPath(normalized))
        {
            return false;
        }

        Initialize();
        Lock.EnterReadLock();
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT content, updated_utc FROM {UserFilesTable} WHERE path = $path;";
            command.Parameters.AddWithValue("$path", normalized);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return false;
            }

            content = (byte[])reader["content"];
            updatedUtc = TryParseDateTimeOffset(reader["updated_utc"]?.ToString());
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            Lock.ExitReadLock();
        }
    }

    public static bool TryWriteText(string path, string content, DateTimeOffset? updatedUtc = null)
    {
        return TryWriteBytes(path, Encoding.UTF8.GetBytes(content), true, updatedUtc);
    }

    public static bool TryWriteBytes(string path, byte[] content, bool isText, DateTimeOffset? updatedUtc = null)
    {
        if (!TryNormalizeUserPath(path, out var normalized))
        {
            return false;
        }

        if (IsIgnoredPath(normalized))
        {
            return false;
        }

        Initialize();
        Lock.EnterWriteLock();
        try
        {
            using var connection = OpenConnection();
            UpsertEntry(connection, normalized, content, isText, updatedUtc);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            Lock.ExitWriteLock();
        }
    }

    public static bool Exists(string path)
    {
        if (!TryNormalizeUserPath(path, out var normalized))
        {
            return false;
        }

        if (IsIgnoredPath(normalized))
        {
            return false;
        }

        Initialize();
        Lock.EnterReadLock();
        try
        {
            using var connection = OpenConnection();
            return ExistsInternal(connection, normalized);
        }
        catch
        {
            return false;
        }
        finally
        {
            Lock.ExitReadLock();
        }
    }

    public static bool Delete(string path)
    {
        if (!TryNormalizeUserPath(path, out var normalized))
        {
            return false;
        }

        if (IsIgnoredPath(normalized))
        {
            return false;
        }

        Initialize();
        Lock.EnterWriteLock();
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $"DELETE FROM {UserFilesTable} WHERE path = $path;";
            command.Parameters.AddWithValue("$path", normalized);
            command.ExecuteNonQuery();
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            Lock.ExitWriteLock();
        }
    }

    public static DateTimeOffset? GetLastWriteTimeUtc(string path)
    {
        if (!TryNormalizeUserPath(path, out var normalized))
        {
            return null;
        }

        if (IsIgnoredPath(normalized))
        {
            return null;
        }

        Initialize();
        Lock.EnterReadLock();
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT updated_utc FROM {UserFilesTable} WHERE path = $path;";
            command.Parameters.AddWithValue("$path", normalized);
            var result = command.ExecuteScalar()?.ToString();
            return TryParseDateTimeOffset(result);
        }
        catch
        {
            return null;
        }
        finally
        {
            Lock.ExitReadLock();
        }
    }

    public static IReadOnlyList<UserFileEntry> ListEntries(string prefix = "", bool recursive = true)
    {
        Initialize();
        var entries = new List<UserFileEntry>();
        if (!TryNormalizeUserPrefix(prefix, out var normalizedPrefix))
        {
            return entries;
        }

        Lock.EnterReadLock();
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            if (string.IsNullOrEmpty(normalizedPrefix))
            {
                command.CommandText = $"SELECT path, length(content) as size, is_text, updated_utc FROM {UserFilesTable};";
            }
            else
            {
                var like = normalizedPrefix + "%";
                command.CommandText = $"SELECT path, length(content) as size, is_text, updated_utc FROM {UserFilesTable} WHERE path LIKE $prefix;";
                command.Parameters.AddWithValue("$prefix", like);
            }

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var path = reader["path"].ToString() ?? string.Empty;
                if (IsIgnoredPath(path))
                {
                    continue;
                }

                if (!ShouldInclude(path, normalizedPrefix, recursive))
                {
                    continue;
                }

                var size = reader["size"] is long len ? len : 0;
                var isText = reader["is_text"] is long textFlag && textFlag == 1;
                var updated = TryParseDateTimeOffset(reader["updated_utc"]?.ToString());
                entries.Add(new UserFileEntry(path, size, isText, updated));
            }
        }
        catch
        {
            return entries;
        }
        finally
        {
            Lock.ExitReadLock();
        }

        return entries;
    }

    public static bool TryNormalizeUserPath(string path, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var userRoot = Global.UserDataRoot;
            string fullPath;
            if (Path.IsPathRooted(path))
            {
                fullPath = Path.GetFullPath(path);
                if (!fullPath.StartsWith(userRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            else
            {
                var trimmed = path.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (trimmed.StartsWith($"User{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("User/", StringComparison.OrdinalIgnoreCase))
                {
                    trimmed = trimmed.Substring(5);
                }

                fullPath = Path.GetFullPath(Path.Combine(userRoot, trimmed));
                if (!fullPath.StartsWith(userRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            normalized = Path.GetRelativePath(userRoot, fullPath)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            return !string.IsNullOrEmpty(normalized) && normalized != ".";
        }
        catch
        {
            return false;
        }

    }

    public static bool IsTextFile(string path)
    {
        var extension = Path.GetExtension(path);
        return TextExtensions.Contains(extension);
    }

    internal static bool IsTemporaryPath(string normalizedPath)
    {
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return false;
        }

        var tempPrefix = $"Temp{Path.DirectorySeparatorChar}";
        var altPrefix = $"Temp{Path.AltDirectorySeparatorChar}";
        return normalizedPath.Equals("Temp", StringComparison.OrdinalIgnoreCase)
               || normalizedPath.StartsWith(tempPrefix, StringComparison.OrdinalIgnoreCase)
               || normalizedPath.StartsWith(altPrefix, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsIgnoredPath(string normalizedPath)
    {
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return true;
        }

        // 忽略临时文件和数据库文件
        if (IsTemporaryPath(normalizedPath) ||
            string.Equals(normalizedPath, DatabaseFileName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 忽略特定的配置文件 - 这些文件应该直接存储在磁盘上
        var ignoredFiles = new[]
        {
            "pick_black_lists.json",
            "pick_white_lists.json",
            "avatar_macro_default.json"
        };

        foreach (var file in ignoredFiles)
        {
            if (normalizedPath.Equals(file, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // 忽略脚本文件目录 - 这些文件应该直接存储在磁盘上，不需要数据库管理
        var scriptDirs = new[]
        {
            "JsScript",
            "KeyMouseScript",
            "AutoFight",
            "AutoGeniusInvokation",
            "AutoPathing",
            "ScriptGroup",
            "OneDragon",
            "Images"
        };

        foreach (var dir in scriptDirs)
        {
            var dirPrefix = $"{dir}{Path.DirectorySeparatorChar}";
            var altDirPrefix = $"{dir}{Path.AltDirectorySeparatorChar}";
            if (normalizedPath.Equals(dir, StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.StartsWith(dirPrefix, StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.StartsWith(altDirPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool LegacyConfigFileExists()
    {
        var path = Path.Combine(Global.UserDataRoot, LegacyConfigFileName);
        return File.Exists(path);
    }

    internal static void DeleteLegacyConfigFile()
    {
        var path = Path.Combine(Global.UserDataRoot, LegacyConfigFileName);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }

    internal static void MarkLegacyConfigIgnored()
    {
        using var connection = OpenConnection();
        EnsureSchema(connection);
        SetMeta(connection, IgnoreLegacyConfigMetaKey, "1");
    }

    private static bool TryNormalizeUserPrefix(string path, out string normalizedPrefix)
    {
        normalizedPrefix = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        if (!TryNormalizeUserPath(path, out normalizedPrefix))
        {
            return false;
        }

        if (!normalizedPrefix.EndsWith(Path.DirectorySeparatorChar))
        {
            normalizedPrefix += Path.DirectorySeparatorChar;
        }

        return true;
    }

    private static bool ShouldInclude(string path, string prefix, bool recursive)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return true;
        }

        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (recursive)
        {
            return true;
        }

        var remainder = path.Substring(prefix.Length);
        return !remainder.Contains(Path.DirectorySeparatorChar);
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
                               CREATE TABLE IF NOT EXISTS {UserFilesTable} (
                                   path TEXT PRIMARY KEY,
                                   content BLOB NOT NULL,
                                   is_text INTEGER NOT NULL,
                                   updated_utc TEXT NOT NULL
                               );
                               CREATE TABLE IF NOT EXISTS {MetaTable} (
                                   meta_key TEXT PRIMARY KEY,
                                   meta_value TEXT NOT NULL
                               );
                               """;
        command.ExecuteNonQuery();
    }

    private static void MigrateFromLegacyTable(SqliteConnection connection)
    {
        if (GetMeta(connection, "migrated_config_entries") == "1")
        {
            return;
        }

        if (!TableExists(connection, ConfigEntriesTable))
        {
            SetMeta(connection, "migrated_config_entries", "1");
            return;
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {ConfigEntriesKeyColumn}, value FROM {ConfigEntriesTable};";
            using var reader = command.ExecuteReader();
            var ignoreLegacyConfig = GetMeta(connection, IgnoreLegacyConfigMetaKey) == "1";
            while (reader.Read())
            {
                var key = reader[ConfigEntriesKeyColumn]?.ToString();
                var value = reader["value"]?.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                if (!TryNormalizeUserPath(key, out var normalized))
                {
                    continue;
                }

                if (IsIgnoredPath(normalized))
                {
                    continue;
                }

                if (ignoreLegacyConfig && normalized.Equals(LegacyConfigFileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (ExistsInternal(connection, normalized))
                {
                    continue;
                }

                UpsertEntry(connection, normalized, Encoding.UTF8.GetBytes(value), true, DateTimeOffset.UtcNow);
            }
        }
        finally
        {
            SetMeta(connection, "migrated_config_entries", "1");
        }
    }

    private static void MigrateFromDisk(SqliteConnection connection)
    {
        if (GetMeta(connection, "migrated_disk") == "1")
        {
            return;
        }

        var userRoot = Global.UserDataRoot;
        if (!Directory.Exists(userRoot))
        {
            SetMeta(connection, "migrated_disk", "1");
            return;
        }

        var files = Directory.GetFiles(userRoot, "*", SearchOption.AllDirectories);
        var ignoreLegacyConfig = GetMeta(connection, IgnoreLegacyConfigMetaKey) == "1";
        foreach (var file in files)
        {
            if (string.Equals(Path.GetFileName(file), DatabaseFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = Path.GetRelativePath(userRoot, file);
            if (!TryNormalizeUserPath(relative, out var normalized))
            {
                continue;
            }

            if (IsIgnoredPath(normalized))
            {
                continue;
            }

            if (ignoreLegacyConfig && normalized.Equals(LegacyConfigFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            byte[] bytes;
            DateTimeOffset updatedUtc;
            try
            {
                bytes = File.ReadAllBytes(file);
                updatedUtc = new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero);
            }
            catch
            {
                continue;
            }

            var existingUpdatedUtc = GetEntryUpdatedUtc(connection, normalized);
            if (existingUpdatedUtc == null || updatedUtc > existingUpdatedUtc)
            {
                UpsertEntry(connection, normalized, bytes, IsTextFile(file), updatedUtc);
            }
        }

        SetMeta(connection, "migrated_disk", "1");
    }

    private static bool ExistsInternal(SqliteConnection connection, string normalizedPath)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT 1 FROM {UserFilesTable} WHERE path = $path;";
        command.Parameters.AddWithValue("$path", normalizedPath);
        return command.ExecuteScalar() != null;
    }

    private static DateTimeOffset? GetEntryUpdatedUtc(SqliteConnection connection, string normalizedPath)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT updated_utc FROM {UserFilesTable} WHERE path = $path;";
        command.Parameters.AddWithValue("$path", normalizedPath);
        var result = command.ExecuteScalar()?.ToString();
        return TryParseDateTimeOffset(result);
    }

    private static void UpsertEntry(SqliteConnection connection, string normalizedPath, byte[] content, bool isText, DateTimeOffset? updatedUtc)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
                               INSERT INTO {UserFilesTable} (path, content, is_text, updated_utc)
                               VALUES ($path, $content, $isText, $updated)
                               ON CONFLICT(path) DO UPDATE SET
                                   content = excluded.content,
                                   is_text = excluded.is_text,
                                   updated_utc = excluded.updated_utc;
                               """;
        command.Parameters.AddWithValue("$path", normalizedPath);
        command.Parameters.Add("$content", SqliteType.Blob).Value = content;
        command.Parameters.AddWithValue("$isText", isText ? 1 : 0);
        command.Parameters.AddWithValue("$updated", (updatedUtc ?? DateTimeOffset.UtcNow).ToString("O"));
        command.ExecuteNonQuery();
    }

    private static string? GetMeta(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT meta_value FROM {MetaTable} WHERE meta_key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar()?.ToString();
    }

    private static void SetMeta(SqliteConnection connection, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
                               INSERT INTO {MetaTable} (meta_key, meta_value)
                               VALUES ($key, $value)
                               ON CONFLICT(meta_key) DO UPDATE SET meta_value = excluded.meta_value;
                               """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static void DeleteIgnoredEntries(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        var tempPrefix = $"Temp{Path.DirectorySeparatorChar}%";
        var altPrefix = $"Temp{Path.AltDirectorySeparatorChar}%";
        command.CommandText = $"""
                               DELETE FROM {UserFilesTable}
                               WHERE path = $temp
                                  OR path LIKE $prefix
                                  OR path LIKE $altPrefix
                                  OR path = $db;
                               """;
        command.Parameters.AddWithValue("$temp", "Temp");
        command.Parameters.AddWithValue("$prefix", tempPrefix);
        command.Parameters.AddWithValue("$altPrefix", altPrefix);
        command.Parameters.AddWithValue("$db", DatabaseFileName);
        command.ExecuteNonQuery();
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name = $table;";
        command.Parameters.AddWithValue("$table", tableName);
        return command.ExecuteScalar() != null;
    }

    private static SqliteConnection OpenConnection()
    {
        if (!Directory.Exists(Global.UserDataRoot))
        {
            Directory.CreateDirectory(Global.UserDataRoot);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };

        var connection = new SqliteConnection(builder.ToString());
        connection.Open();

        using var pragma = connection.CreateCommand();
        pragma.CommandText = """
                             PRAGMA journal_mode = WAL;
                             PRAGMA synchronous = NORMAL;
                             PRAGMA busy_timeout = 3000;
                             """;
        pragma.ExecuteNonQuery();

        return connection;
    }

    private static DateTimeOffset? TryParseDateTimeOffset(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result))
        {
            return result;
        }

        return null;
    }
}

internal readonly record struct UserFileEntry(string Path, long Size, bool IsText, DateTimeOffset? UpdatedUtc);
