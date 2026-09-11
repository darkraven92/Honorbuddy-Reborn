using System.Text;
using Styx.Logic.Questing;
using Styx.WoWInternals;

namespace Honorbuddy5875.Runtime;

/// <summary>
/// WoW 1.12.1 build-5875 compatibility backend for Honorbuddy's WoWCache quest store.
///
/// Original Honorbuddy 2.0.0.5999 walks the client's in-memory DBCache through
/// BlueMagic. This port keeps the same public StyxWoW.Cache -> Cache -> InfoBlock ->
/// QuestCacheEntry chain while using Vanilla's persistent questcache.wdb as the
/// read-only record index. If the current client has not flushed a live quest record
/// to disk yet, an identity-only entry is supplied for an active quest-log ID so the
/// public Honorbuddy PlayerQuest semantics remain intact.
/// </summary>
internal static class Vanilla5875QuestCache
{
    private const int HeaderSize = 20;
    private const string QuestSignatureOnDisk = "TSQW";

    private static readonly object Sync = new();
    private static string? _path;
    private static long _length = -1;
    private static DateTime _writeTimeUtc = DateTime.MinValue;
    private static QuestCacheDiagnostics _diagnostics;
    private static Dictionary<uint, QuestCacheRecord> _records = new();

    public static bool TryGetQuestEntry(uint questId, out QuestCacheEntry entry)
    {
        if (questId == 0)
        {
            entry = default;
            return false;
        }

        EnsureLoaded();
        lock (Sync)
        {
            if (_records.ContainsKey(questId))
            {
                entry = new QuestCacheEntry { Id = questId };
                return true;
            }
        }

        // The 1.12 client can have a freshly queried record in memory before its
        // persistent WDB is flushed. Original Honorbuddy would still resolve it
        // through the in-memory cache. Preserve that high-level behavior with the
        // minimum identity information already proven in the live quest log.
        try
        {
            if (Styx.StyxWoW.IsInGame &&
                Styx.StyxWoW.Me?.QuestLog.ContainsQuest(questId) == true)
            {
                entry = new QuestCacheEntry { Id = questId };
                return true;
            }
        }
        catch
        {
            // Cache lookup must remain non-fatal when no live object manager exists.
        }

        entry = default;
        return false;
    }

    public static bool ContainsPersistedRecord(uint questId)
    {
        EnsureLoaded();
        lock (Sync)
            return _records.ContainsKey(questId);
    }

    public static QuestCacheDiagnostics GetDiagnostics()
    {
        EnsureLoaded();
        lock (Sync)
            return _diagnostics;
    }

    private static void EnsureLoaded()
    {
        string? path = ResolveQuestCachePath();
        if (path is null)
        {
            lock (Sync)
            {
                _path = null;
                _length = -1;
                _writeTimeUtc = DateTime.MinValue;
                _records = new Dictionary<uint, QuestCacheRecord>();
                _diagnostics = new QuestCacheDiagnostics(
                    Path: null,
                    Signature: string.Empty,
                    Build: 0,
                    Locale: string.Empty,
                    InternalRecordSize: 0,
                    RecordVersion: 0,
                    RecordCount: 0,
                    IsValid5875QuestCache: false,
                    Error: "questcache.wdb was not found");
            }
            return;
        }

        var info = new FileInfo(path);
        long length = info.Exists ? info.Length : -1;
        DateTime writeTimeUtc = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue;

        lock (Sync)
        {
            if (string.Equals(_path, path, StringComparison.Ordinal) &&
                _length == length &&
                _writeTimeUtc == writeTimeUtc)
            {
                return;
            }
        }

        Load(path, length, writeTimeUtc);
    }

    private static void Load(string path, long length, DateTime writeTimeUtc)
    {
        var records = new Dictionary<uint, QuestCacheRecord>();
        QuestCacheDiagnostics diagnostics;

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);

            if (stream.Length < HeaderSize)
                throw new InvalidDataException($"Quest cache is shorter than the {HeaderSize}-byte Vanilla header.");

            string signature = Encoding.ASCII.GetString(reader.ReadBytes(4));
            uint build = reader.ReadUInt32();
            string localeRaw = Encoding.ASCII.GetString(reader.ReadBytes(4));
            uint internalRecordSize = reader.ReadUInt32();
            uint recordVersion = reader.ReadUInt32();
            string locale = ReverseAscii(localeRaw);

            if (!string.Equals(signature, QuestSignatureOnDisk, StringComparison.Ordinal))
                throw new InvalidDataException($"Unexpected quest-cache signature '{signature}', expected '{QuestSignatureOnDisk}'.");
            if (build != 5875)
                throw new InvalidDataException($"Quest cache belongs to client build {build}, expected 5875.");

            while (stream.Position + 8 <= stream.Length)
            {
                long recordHeaderOffset = stream.Position;
                uint id = reader.ReadUInt32();
                uint dataLength = reader.ReadUInt32();

                if (id == 0 && dataLength == 0)
                    break;

                if (id == 0)
                    throw new InvalidDataException($"Quest cache contains a zero ID at file offset 0x{recordHeaderOffset:X}.");

                long dataOffset = stream.Position;
                long end = dataOffset + dataLength;
                if (end < dataOffset || end > stream.Length)
                    throw new InvalidDataException(
                        $"Quest {id} record length {dataLength} exceeds the WDB file at offset 0x{recordHeaderOffset:X}.");

                records[id] = new QuestCacheRecord(id, dataOffset, dataLength);
                stream.Position = end;
            }

            diagnostics = new QuestCacheDiagnostics(
                Path: path,
                Signature: signature,
                Build: build,
                Locale: locale,
                InternalRecordSize: internalRecordSize,
                RecordVersion: recordVersion,
                RecordCount: records.Count,
                IsValid5875QuestCache: true,
                Error: null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            records.Clear();
            diagnostics = new QuestCacheDiagnostics(
                Path: path,
                Signature: string.Empty,
                Build: 0,
                Locale: string.Empty,
                InternalRecordSize: 0,
                RecordVersion: 0,
                RecordCount: 0,
                IsValid5875QuestCache: false,
                Error: ex.Message);
        }

        lock (Sync)
        {
            _path = path;
            _length = length;
            _writeTimeUtc = writeTimeUtc;
            _records = records;
            _diagnostics = diagnostics;
        }
    }

    private static string? ResolveQuestCachePath()
    {
        string? configuredRoot = Environment.GetEnvironmentVariable("HB_WOW_ROOT");
        string root = !string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.GetFullPath(configuredRoot)
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Games",
                "WoW Vanilla");

        // Vanilla 1.12.x clients commonly keep WDB files directly below the
        // game-root WDB directory (for example WDB/questcache.wdb), while
        // later layouts use Cache/WDB/<locale>/questcache.wdb. Keep this
        // difference entirely inside the build-5875 compatibility backend.
        string[] wdbRoots =
        {
            Path.Combine(root, "WDB"),
            Path.Combine(root, "Cache", "WDB")
        };

        var candidates = new List<string>();

        foreach (string wdbRoot in wdbRoots)
        {
            if (!Directory.Exists(wdbRoot))
                continue;

            string direct = Path.Combine(wdbRoot, "questcache.wdb");
            if (File.Exists(direct))
                candidates.Add(direct);

            try
            {
                candidates.AddRange(Directory
                    .EnumerateFiles(wdbRoot, "*", SearchOption.AllDirectories)
                    .Where(path => string.Equals(
                        Path.GetFileName(path),
                        "questcache.wdb",
                        StringComparison.OrdinalIgnoreCase)));
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore inaccessible cache subdirectories and continue with
                // any other valid Vanilla/later-layout candidates.
            }
            catch (IOException)
            {
                // A cache directory may disappear while the client is running.
            }
        }

        return candidates
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static string ReverseAscii(string value)
    {
        char[] chars = value.ToCharArray();
        Array.Reverse(chars);
        return new string(chars).TrimEnd('\0');
    }

    private readonly record struct QuestCacheRecord(uint Id, long DataOffset, uint DataLength);
}

internal readonly record struct QuestCacheDiagnostics(
    string? Path,
    string Signature,
    uint Build,
    string Locale,
    uint InternalRecordSize,
    uint RecordVersion,
    int RecordCount,
    bool IsValid5875QuestCache,
    string? Error);
