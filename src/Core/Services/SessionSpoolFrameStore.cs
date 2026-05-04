using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ComCross.Shared.Interfaces;
using ComCross.Shared.Models;
using Microsoft.Extensions.Logging;

namespace ComCross.Core.Services;

public sealed class SessionSpoolFrameStore : IFrameStore
{
    private const int SegmentMagic = 0x46534343; // CCSF
    private const int SegmentSchemaVersion = 1;
    private const int ManifestSchemaVersion = 2;
    private const int CheckpointEveryFrames = 256;
    private const string DefaultWorkspaceId = "default";

    private readonly ComCrossPathService _paths;
    private readonly SettingsService _settingsService;
    private readonly IStoragePolicyService _storagePolicy;
    private readonly IStorageHealthService _storageHealth;
    private readonly ILogger<SessionSpoolFrameStore> _logger;
    private readonly ConcurrentDictionary<string, SessionSpoolState> _sessions = new(StringComparer.Ordinal);
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public SessionSpoolFrameStore(
        ComCrossPathService paths,
        SettingsService settingsService,
        IStoragePolicyService storagePolicy,
        IStorageHealthService storageHealth,
        ILogger<SessionSpoolFrameStore> logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _storagePolicy = storagePolicy ?? throw new ArgumentNullException(nameof(storagePolicy));
        _storageHealth = storageHealth ?? throw new ArgumentNullException(nameof(storageHealth));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public event Action<string>? FramesAppended;

    public long Append(
        string sessionId,
        DateTime timestampUtc,
        FrameDirection direction,
        byte[] rawData,
        MessageFormat format,
        string source,
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Missing sessionId.", nameof(sessionId));
        }

        rawData ??= Array.Empty<byte>();
        source ??= string.Empty;

        var state = GetState(sessionId);
        var normalizedAttributes = MessageFrameAttributes.Normalize(
            attributes,
            diagnostic => _logger.LogDebug("Dropped message frame attribute: {Diagnostic}", diagnostic));

        long frameId;
        lock (state.Gate)
        {
            EnsureLoaded(state);

            frameId = state.Manifest.LastFrameId + 1;
            var record = new MessageFrameRecord(
                frameId,
                sessionId,
                timestampUtc.Kind == DateTimeKind.Utc ? timestampUtc : timestampUtc.ToUniversalTime(),
                direction,
                rawData,
                format,
                source,
                normalizedAttributes);

            AppendRecord(state, record);
            CleanupSessionIfNeeded(state);
        }

        CleanupGlobalIfNeeded();

        try
        {
            FramesAppended?.Invoke(sessionId);
        }
        catch
        {
        }

        return frameId;
    }

    public IReadOnlyList<FrameRecord> ReadAfter(string sessionId, long afterFrameId, int maxCount, out long firstAvailableFrameId)
    {
        firstAvailableFrameId = 0;

        if (string.IsNullOrWhiteSpace(sessionId) || maxCount <= 0)
        {
            return Array.Empty<FrameRecord>();
        }

        var state = GetState(sessionId);
        lock (state.Gate)
        {
            EnsureLoaded(state);
            firstAvailableFrameId = state.Manifest.FirstAvailableFrameId;

            if (state.Manifest.LastFrameId <= 0)
            {
                return Array.Empty<FrameRecord>();
            }

            var startFrameId = Math.Max(afterFrameId + 1, state.Manifest.FirstAvailableFrameId);
            if (startFrameId > state.Manifest.LastFrameId)
            {
                return Array.Empty<FrameRecord>();
            }

            var results = new List<FrameRecord>(Math.Min(maxCount, 1024));
            foreach (var segment in state.Manifest.Segments
                         .Where(s => s.LastFrameId >= startFrameId)
                         .OrderBy(s => s.SegmentId))
            {
                var path = Path.Combine(state.Directory, segment.FileName);
                foreach (var frame in ReadSegment(path, segment.ByteCount))
                {
                    if (frame.FrameId < startFrameId)
                    {
                        continue;
                    }

                    results.Add(ToLegacyFrameRecord(frame));
                    if (results.Count >= maxCount)
                    {
                        return results;
                    }
                }
            }

            return results;
        }
    }

    public (long FirstAvailableFrameId, long LastFrameId, long DroppedFrames) GetWindowInfo(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return (0, 0, 0);
        }

        var state = GetState(sessionId);
        lock (state.Gate)
        {
            EnsureLoaded(state);
            return (state.Manifest.FirstAvailableFrameId, state.Manifest.LastFrameId, state.Manifest.DroppedFrames);
        }
    }

    public void Clear(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        var state = GetState(sessionId);
        lock (state.Gate)
        {
            if (Directory.Exists(state.Directory))
            {
                try
                {
                    Directory.Delete(state.Directory, recursive: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete session spool directory: {SessionId}", sessionId);
                }
            }

            state.Loaded = false;
            state.Manifest = CreateEmptyManifest(sessionId);
        }
    }

    private SessionSpoolState GetState(string sessionId)
        => _sessions.GetOrAdd(sessionId, id => new SessionSpoolState(id, GetSessionDirectory(id), CreateEmptyManifest(id)));

    private string GetSessionDirectory(string sessionId)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionId))).ToLowerInvariant();
        return Path.Combine(_paths.SessionSpoolDirectory, DefaultWorkspaceId, hash);
    }

    private static SpoolManifest CreateEmptyManifest(string sessionId)
        => new()
        {
            SchemaVersion = ManifestSchemaVersion,
            WorkspaceId = DefaultWorkspaceId,
            SessionId = sessionId,
            ActiveSegmentId = 0,
            FirstAvailableFrameId = 1,
            LastFrameId = 0,
            SegmentMaxBytes = 0,
            TotalSpoolBytes = 0,
            DroppedFrames = 0,
            Segments = new List<SpoolSegmentManifest>()
        };

    private void EnsureLoaded(SessionSpoolState state)
    {
        if (state.Loaded)
        {
            return;
        }

        Directory.CreateDirectory(state.Directory);

        var manifestPath = GetManifestPath(state);
        state.Manifest = TryLoadManifest(manifestPath) ?? RebuildManifest(state);
        state.Manifest.SessionId = state.SessionId;
        state.Manifest.WorkspaceId = DefaultWorkspaceId;
        state.Manifest.SchemaVersion = ManifestSchemaVersion;
        state.Manifest.SegmentMaxBytes = GetSegmentMaxBytes();

        EnsureActiveSegment(state);
        state.Loaded = true;
    }

    private SpoolManifest? TryLoadManifest(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(manifestPath);
            return JsonSerializer.Deserialize<SpoolManifest>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read spool manifest: {ManifestPath}", manifestPath);
            return null;
        }
    }

    private SpoolManifest RebuildManifest(SessionSpoolState state)
    {
        var manifest = CreateEmptyManifest(state.SessionId);
        var files = Directory.Exists(state.Directory)
            ? Directory.EnumerateFiles(state.Directory, "*.csf").Order(StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();

        foreach (var file in files)
        {
            var frames = ReadSegment(file, null).ToList();
            if (frames.Count == 0)
            {
                continue;
            }

            var validBytes = ScanValidBytes(file);
            var allocatedBytes = new FileInfo(file).Length;
            var segmentId = TryParseSegmentId(Path.GetFileNameWithoutExtension(file));
            if (segmentId <= 0)
            {
                segmentId = manifest.Segments.Count == 0 ? 1 : manifest.Segments.Max(s => s.SegmentId) + 1;
            }

            manifest.Segments.Add(new SpoolSegmentManifest
            {
                SegmentId = segmentId,
                FileName = Path.GetFileName(file),
                FirstFrameId = frames[0].FrameId,
                LastFrameId = frames[^1].FrameId,
                ByteCount = validBytes,
                AllocatedBytes = allocatedBytes,
                Preallocated = allocatedBytes > validBytes,
                Sealed = true,
                CreatedAtUtc = File.GetCreationTimeUtc(file)
            });
        }

        if (manifest.Segments.Count > 0)
        {
            manifest.Segments = manifest.Segments.OrderBy(s => s.SegmentId).ToList();
            manifest.ActiveSegmentId = manifest.Segments[^1].SegmentId;
            manifest.Segments[^1].Sealed = false;
            manifest.FirstAvailableFrameId = manifest.Segments[0].FirstFrameId;
            manifest.LastFrameId = manifest.Segments[^1].LastFrameId;
            manifest.TotalSpoolBytes = manifest.Segments.Sum(GetAccountingBytes);
        }

        return manifest;
    }

    private static long TryParseSegmentId(string value)
        => long.TryParse(value, out var id) ? id : 0;

    private void EnsureActiveSegment(SessionSpoolState state)
    {
        if (state.Manifest.Segments.FirstOrDefault(s => s.SegmentId == state.Manifest.ActiveSegmentId) is { } active
            && File.Exists(Path.Combine(state.Directory, active.FileName)))
        {
            active.Sealed = false;
            return;
        }

        var nextSegmentId = GetNextAvailableSegmentId(state);

        var segment = new SpoolSegmentManifest
        {
            SegmentId = nextSegmentId,
            FileName = FormatSegmentFileName(nextSegmentId),
            FirstFrameId = 0,
            LastFrameId = 0,
            ByteCount = 0,
            AllocatedBytes = 0,
            Preallocated = false,
            Sealed = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        InitializeSegmentFile(state, segment);
        state.Manifest.Segments.Add(segment);
        state.Manifest.ActiveSegmentId = segment.SegmentId;
        state.Manifest.TotalSpoolBytes += GetAccountingBytes(segment);
        SaveManifest(state);
    }

    private void AppendRecord(SessionSpoolState state, MessageFrameRecord record)
    {
        var recordBytes = EncodeRecord(record);
        var segmentMaxBytes = GetSegmentMaxBytes();
        var active = GetActiveSegment(state);

        if (active.LastFrameId > 0 && active.ByteCount + recordBytes.Length + sizeof(int) > segmentMaxBytes)
        {
            active.Sealed = true;
            SaveManifest(state);
            CreateNextSegment(state);
            active = GetActiveSegment(state);
        }

        var path = Path.Combine(state.Directory, active.FileName);
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read, 64 * 1024))
        {
            stream.Position = active.ByteCount;
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(recordBytes.Length);
            writer.Write(recordBytes);
            writer.Flush();
            stream.Flush();
        }

        var bytesWritten = recordBytes.Length + sizeof(int);
        var previousAccountingBytes = GetAccountingBytes(active);
        active.FirstFrameId = active.FirstFrameId == 0 ? record.FrameId : active.FirstFrameId;
        active.LastFrameId = record.FrameId;
        active.ByteCount += bytesWritten;
        active.AllocatedBytes = Math.Max(active.AllocatedBytes, active.ByteCount);
        state.Manifest.LastFrameId = record.FrameId;
        state.Manifest.TotalSpoolBytes += GetAccountingBytes(active) - previousAccountingBytes;
        if (state.Manifest.FirstAvailableFrameId <= 0)
        {
            state.Manifest.FirstAvailableFrameId = record.FrameId;
        }

        state.FramesSinceCheckpoint++;
        if (state.FramesSinceCheckpoint >= CheckpointEveryFrames)
        {
            state.FramesSinceCheckpoint = 0;
            SaveManifest(state);
        }
    }

    private SpoolSegmentManifest GetActiveSegment(SessionSpoolState state)
    {
        var active = state.Manifest.Segments.FirstOrDefault(s => s.SegmentId == state.Manifest.ActiveSegmentId);
        if (active is null)
        {
            EnsureActiveSegment(state);
            active = state.Manifest.Segments.First(s => s.SegmentId == state.Manifest.ActiveSegmentId);
        }

        return active;
    }

    private void CreateNextSegment(SessionSpoolState state)
    {
        var nextSegmentId = GetNextAvailableSegmentId(state);
        var segment = new SpoolSegmentManifest
        {
            SegmentId = nextSegmentId,
            FileName = FormatSegmentFileName(nextSegmentId),
            FirstFrameId = 0,
            LastFrameId = 0,
            ByteCount = 0,
            AllocatedBytes = 0,
            Preallocated = false,
            Sealed = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        InitializeSegmentFile(state, segment);
        state.Manifest.Segments.Add(segment);
        state.Manifest.ActiveSegmentId = segment.SegmentId;
        state.Manifest.TotalSpoolBytes += GetAccountingBytes(segment);
        SaveManifest(state);
    }

    private static string FormatSegmentFileName(long segmentId)
        => $"{segmentId:0000000000000000}.csf";

    private static long GetNextAvailableSegmentId(SessionSpoolState state)
    {
        var segmentId = state.Manifest.Segments.Count == 0
            ? 1
            : state.Manifest.Segments.Max(s => s.SegmentId) + 1;

        while (File.Exists(Path.Combine(state.Directory, FormatSegmentFileName(segmentId))))
        {
            segmentId++;
        }

        return segmentId;
    }

    private void InitializeSegmentFile(SessionSpoolState state, SpoolSegmentManifest segment)
    {
        var path = Path.Combine(state.Directory, segment.FileName);
        var headerBytes = WriteSegmentHeader(path, state.SessionId, segment.SegmentId, segment.CreatedAtUtc);
        segment.ByteCount = headerBytes;

        var policy = _storagePolicy.Current;
        var segmentMaxBytes = GetSegmentMaxBytes(policy);
        if (policy.PreallocateSegments)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read, 4096);
            stream.SetLength(segmentMaxBytes);
            segment.AllocatedBytes = segmentMaxBytes;
            segment.Preallocated = true;
        }
        else
        {
            segment.AllocatedBytes = headerBytes;
            segment.Preallocated = false;
        }
    }

    private static long WriteSegmentHeader(string path, string sessionId, long segmentId, DateTime createdAtUtc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(SegmentMagic);
        writer.Write(SegmentSchemaVersion);
        writer.Write(segmentId);
        writer.Write(createdAtUtc.Ticks);
        WriteString(writer, sessionId);
        writer.Flush();
        stream.Flush();
        return stream.Position;
    }

    private static byte[] EncodeRecord(MessageFrameRecord record)
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory, Encoding.UTF8, leaveOpen: true);

        writer.Write(record.SchemaVersion);
        writer.Write(record.FrameId);
        WriteString(writer, record.SessionId);
        writer.Write(record.TimestampUtc.Ticks);
        writer.Write((byte)record.Direction);
        writer.Write((int)record.Format);
        WriteString(writer, record.Source);
        writer.Write(record.AttributeSchemaVersion);
        WriteString(writer, JsonSerializer.Serialize(record.Attributes));
        writer.Write(record.RawData.Length);
        writer.Write(record.RawData);
        writer.Flush();

        return memory.ToArray();
    }

    private static IEnumerable<MessageFrameRecord> ReadSegment(string path, long? validBytes)
    {
        if (!File.Exists(path))
        {
            yield break;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        if (!TryReadSegmentHeader(reader))
        {
            yield break;
        }

        var readLimit = validBytes is > 0
            ? Math.Min(validBytes.Value, stream.Length)
            : stream.Length;

        while (stream.Position + sizeof(int) <= readLimit)
        {
            var recordLength = reader.ReadInt32();
            if (recordLength <= 0 || stream.Position + recordLength > readLimit)
            {
                yield break;
            }

            var bytes = reader.ReadBytes(recordLength);
            if (TryDecodeRecord(bytes, out var record))
            {
                yield return record;
            }
        }
    }

    private static long ScanValidBytes(string path)
    {
        if (!File.Exists(path))
        {
            return 0;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (!TryReadSegmentHeader(reader))
        {
            return 0;
        }

        var validBytes = stream.Position;
        while (stream.Position + sizeof(int) <= stream.Length)
        {
            var lengthPosition = stream.Position;
            var recordLength = reader.ReadInt32();
            if (recordLength <= 0 || stream.Position + recordLength > stream.Length)
            {
                return validBytes;
            }

            var bytes = reader.ReadBytes(recordLength);
            if (bytes.Length != recordLength || !TryDecodeRecord(bytes, out _))
            {
                return validBytes;
            }

            validBytes = stream.Position;

            if (stream.Position == lengthPosition)
            {
                return validBytes;
            }
        }

        return validBytes;
    }

    private static bool TryReadSegmentHeader(BinaryReader reader)
    {
        try
        {
            if (reader.ReadInt32() != SegmentMagic)
            {
                return false;
            }

            _ = reader.ReadInt32();
            _ = reader.ReadInt64();
            _ = reader.ReadInt64();
            _ = ReadString(reader);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDecodeRecord(byte[] bytes, out MessageFrameRecord record)
    {
        record = null!;

        try
        {
            using var memory = new MemoryStream(bytes);
            using var reader = new BinaryReader(memory, Encoding.UTF8, leaveOpen: true);

            var schemaVersion = reader.ReadInt32();
            var frameId = reader.ReadInt64();
            var sessionId = ReadString(reader);
            var timestampUtc = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
            var direction = (FrameDirection)reader.ReadByte();
            var format = (MessageFormat)reader.ReadInt32();
            var source = ReadString(reader);
            var attributeSchemaVersion = reader.ReadInt32();
            var attributesJson = ReadString(reader);
            var payloadLength = reader.ReadInt32();
            if (payloadLength < 0 || memory.Position + payloadLength > memory.Length)
            {
                return false;
            }

            var payload = reader.ReadBytes(payloadLength);
            var attributes = JsonSerializer.Deserialize<Dictionary<string, string>>(attributesJson)
                             ?? new Dictionary<string, string>(StringComparer.Ordinal);
            record = new MessageFrameRecord(
                schemaVersion,
                frameId,
                sessionId,
                timestampUtc,
                direction,
                payload,
                format,
                source,
                attributes,
                attributeSchemaVersion);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteString(BinaryWriter writer, string? value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length < 0)
        {
            throw new InvalidDataException("Negative string length.");
        }

        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
        {
            throw new EndOfStreamException();
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static FrameRecord ToLegacyFrameRecord(MessageFrameRecord record)
        => new(
            record.FrameId,
            record.SessionId,
            record.TimestampUtc,
            record.Direction,
            record.RawData,
            record.Format,
            record.Source,
            record.Attributes,
            record.AttributeSchemaVersion);

    private void CleanupSessionIfNeeded(SessionSpoolState state)
    {
        var maxBytes = Math.Max(1, _settingsService.Current.Logs.MaxPerSessionSizeMb) * 1024L * 1024L;
        while (state.Manifest.TotalSpoolBytes > maxBytes)
        {
            var victim = state.Manifest.Segments
                .Where(s => s.Sealed)
                .OrderBy(s => s.SegmentId)
                .FirstOrDefault();
            if (victim is null)
            {
                break;
            }

            DeleteSegment(state, victim);
        }

        if (state.Manifest.TotalSpoolBytes > maxBytes)
        {
            ReportStoragePressure("session-spool-limit-active-segment");
        }
    }

    private void CleanupGlobalIfNeeded()
    {
        var maxBytes = Math.Max(1, _settingsService.Current.Logs.MaxTotalSizeMb) * 1024L * 1024L;
        var root = Path.Combine(_paths.SessionSpoolDirectory, DefaultWorkspaceId);
        if (!Directory.Exists(root))
        {
            return;
        }

        var loadedStates = _sessions.Values
            .Where(s => s.Loaded)
            .ToList();
        var loadedDirectories = loadedStates
            .Select(s => s.Directory)
            .ToHashSet(StringComparer.Ordinal);
        var candidates = new List<GlobalCleanupCandidate>();
        long total = 0;

        foreach (var state in loadedStates)
        {
            lock (state.Gate)
            {
                total += Math.Max(0, state.Manifest.TotalSpoolBytes);
                foreach (var segment in state.Manifest.Segments.Where(s => s.Sealed))
                {
                    candidates.Add(new GlobalCleanupCandidate(state, null, null, segment));
                }
            }
        }

        var manifests = Directory.EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories)
            .Where(path => Path.GetDirectoryName(path) is not { } directory || !loadedDirectories.Contains(directory))
            .Select(path => (Path: path, Manifest: TryLoadManifest(path)))
            .Where(item => item.Manifest is not null)
            .Select(item => (item.Path, Manifest: item.Manifest!))
            .ToList();

        foreach (var item in manifests)
        {
            total += Math.Max(0, item.Manifest.TotalSpoolBytes);
            foreach (var segment in item.Manifest.Segments.Where(s => s.Sealed))
            {
                candidates.Add(new GlobalCleanupCandidate(null, item.Path, item.Manifest, segment));
            }
        }

        if (total <= maxBytes)
        {
            return;
        }

        foreach (var item in candidates.OrderBy(item => item.Segment.CreatedAtUtc))
        {
            if (total <= maxBytes)
            {
                break;
            }

            if (item.State is not null)
            {
                lock (item.State.Gate)
                {
                    if (!item.State.Manifest.Segments.Contains(item.Segment) || !item.Segment.Sealed)
                    {
                        continue;
                    }

                    var removedBytes = GetAccountingBytes(item.Segment);
                    DeleteSegment(item.State, item.Segment);
                    total -= removedBytes;
                }
                continue;
            }

            if (item.ManifestPath is null || item.Manifest is null)
            {
                continue;
            }

            var directory = Path.GetDirectoryName(item.ManifestPath);
            if (directory is null)
            {
                continue;
            }

            var filePath = Path.Combine(directory, item.Segment.FileName);
            var byteCount = GetAccountingBytes(item.Segment);
            TryDeleteFile(filePath);
            item.Manifest.Segments.Remove(item.Segment);
            item.Manifest.TotalSpoolBytes -= byteCount;
            item.Manifest.DroppedFrames += Math.Max(0, item.Segment.LastFrameId - item.Segment.FirstFrameId + 1);
            item.Manifest.FirstAvailableFrameId = item.Manifest.Segments.Count == 0
                ? item.Manifest.LastFrameId + 1
                : item.Manifest.Segments.Where(s => s.LastFrameId > 0).Select(s => s.FirstFrameId).DefaultIfEmpty(item.Manifest.LastFrameId + 1).Min();
            File.WriteAllText(item.ManifestPath, JsonSerializer.Serialize(item.Manifest, _jsonOptions));
            total -= byteCount;
        }

        if (total > maxBytes)
        {
            ReportStoragePressure("global-spool-limit-active-segments");
        }
    }

    private void ReportStoragePressure(string reason)
    {
        _ = _storageHealth.ReportAsync(StorageHealth.Degraded, reason);
    }

    private void DeleteSegment(SessionSpoolState state, SpoolSegmentManifest segment)
    {
        TryDeleteFile(Path.Combine(state.Directory, segment.FileName));
        state.Manifest.Segments.Remove(segment);
        state.Manifest.TotalSpoolBytes -= GetAccountingBytes(segment);
        state.Manifest.DroppedFrames += Math.Max(0, segment.LastFrameId - segment.FirstFrameId + 1);
        state.Manifest.FirstAvailableFrameId = state.Manifest.Segments.Count == 0
            ? state.Manifest.LastFrameId + 1
            : state.Manifest.Segments.Where(s => s.LastFrameId > 0).Select(s => s.FirstFrameId).DefaultIfEmpty(state.Manifest.LastFrameId + 1).Min();
        SaveManifest(state);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private long GetSegmentMaxBytes()
        => GetSegmentMaxBytes(_storagePolicy.Current);

    private long GetSegmentMaxBytes(StoragePolicy policy)
    {
        var configured = Math.Max(1, _settingsService.Current.Logs.MaxFileSizeMb);
        var policySize = Math.Max(1, policy.SegmentSizeMb);
        return Math.Min(configured, policySize) * 1024L * 1024L;
    }

    private static long GetAccountingBytes(SpoolSegmentManifest segment)
        => Math.Max(0, segment.Preallocated ? segment.AllocatedBytes : segment.ByteCount);

    private static string GetManifestPath(SessionSpoolState state)
        => Path.Combine(state.Directory, "manifest.json");

    private void SaveManifest(SessionSpoolState state)
    {
        var path = GetManifestPath(state);
        var tempPath = path + ".tmp";
        Directory.CreateDirectory(state.Directory);
        File.WriteAllText(tempPath, JsonSerializer.Serialize(state.Manifest, _jsonOptions));
        File.Move(tempPath, path, overwrite: true);
    }

    private sealed class SessionSpoolState
    {
        public SessionSpoolState(string sessionId, string directory, SpoolManifest manifest)
        {
            SessionId = sessionId;
            Directory = directory;
            Manifest = manifest;
        }

        public string SessionId { get; }
        public string Directory { get; }
        public object Gate { get; } = new();
        public SpoolManifest Manifest { get; set; }
        public bool Loaded { get; set; }
        public int FramesSinceCheckpoint { get; set; }
    }

    private sealed record GlobalCleanupCandidate(
        SessionSpoolState? State,
        string? ManifestPath,
        SpoolManifest? Manifest,
        SpoolSegmentManifest Segment);

    private sealed class SpoolManifest
    {
        public int SchemaVersion { get; set; }
        public string WorkspaceId { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public long ActiveSegmentId { get; set; }
        public long FirstAvailableFrameId { get; set; } = 1;
        public long LastFrameId { get; set; }
        public long SegmentMaxBytes { get; set; }
        public long TotalSpoolBytes { get; set; }
        public long DroppedFrames { get; set; }
        public List<SpoolSegmentManifest> Segments { get; set; } = new();
    }

    private sealed class SpoolSegmentManifest
    {
        public long SegmentId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public long FirstFrameId { get; set; }
        public long LastFrameId { get; set; }
        public long ByteCount { get; set; }
        public long AllocatedBytes { get; set; }
        public bool Preallocated { get; set; }
        public bool Sealed { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
