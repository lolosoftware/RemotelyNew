using Remotely.Shared.Models.Dtos;
using MessagePack;
using System.Diagnostics.CodeAnalysis;

namespace Remotely.Shared.Helpers;

public static class DtoChunker
{
    public const int MaxMessageBytes = 4 * 1024 * 1024;
    public const int MaxBufferedBytes = 16 * 1024 * 1024;
    private const int MaxPendingMessages = 64;
    private static readonly object Sync = new();
    private static readonly Dictionary<string, PendingMessage> Pending = new(StringComparer.Ordinal);
    private static int _bufferedBytes;

    private sealed class PendingMessage(DtoType type, string? requestId)
    {
        public DtoType Type { get; } = type;
        public string? RequestId { get; } = requestId;
        public DateTimeOffset Expires { get; } = DateTimeOffset.UtcNow.AddSeconds(30);
        public List<byte[]> Chunks { get; } = new();
        public int Size { get; set; }
    }

    public static IEnumerable<DtoWrapper> ChunkDto<T>(T dto, DtoType dtoType, string requestId = "", int chunkSize = 50_000)
    {
        if (chunkSize <= 0 || chunkSize > 50_000) throw new ArgumentOutOfRangeException(nameof(chunkSize));
        var bytes = MessagePackSerializer.Serialize(dto);
        if (bytes.Length > MaxMessageBytes) throw new ArgumentException("Message exceeds the protocol limit.", nameof(dto));
        var id = Guid.NewGuid().ToString();
        for (var offset = 0; offset < bytes.Length; offset += chunkSize)
        {
            var length = Math.Min(chunkSize, bytes.Length - offset);
            yield return new DtoWrapper
            {
                DtoChunk = bytes.AsSpan(offset, length).ToArray(), DtoType = dtoType,
                SequenceId = offset / chunkSize, IsFirstChunk = offset == 0,
                IsLastChunk = offset + length == bytes.Length, RequestId = requestId, InstanceId = id
            };
        }
    }

    public static bool TryComplete<T>(DtoWrapper wrapper, [NotNullWhen(true)] out T? result)
    {
        result = default;
        if (wrapper is null || string.IsNullOrWhiteSpace(wrapper.InstanceId) || wrapper.InstanceId.Length > 64 ||
            wrapper.DtoChunk is null || wrapper.DtoChunk.Length == 0 || wrapper.DtoChunk.Length > 50_000 ||
            wrapper.SequenceId < 0 || wrapper.RequestId?.Length > 128) return false;

        byte[] buffer;
        lock (Sync)
        {
            foreach (var expired in Pending.Where(x => x.Value.Expires <= DateTimeOffset.UtcNow).Select(x => x.Key).ToArray())
                Remove(expired);
            if (wrapper.IsFirstChunk)
            {
                // Reusing an in-flight ID or starting at a nonzero sequence is invalid.
                if (wrapper.SequenceId != 0 || Pending.ContainsKey(wrapper.InstanceId))
                {
                    Remove(wrapper.InstanceId);
                    return false;
                }
                if (Pending.Count >= MaxPendingMessages) return false;
                Pending[wrapper.InstanceId] = new PendingMessage(wrapper.DtoType, wrapper.RequestId);
            }
            if (!Pending.TryGetValue(wrapper.InstanceId, out var pending)) return false;
            if (pending.Type != wrapper.DtoType || pending.RequestId != wrapper.RequestId ||
                wrapper.SequenceId != pending.Chunks.Count ||
                pending.Size + wrapper.DtoChunk.Length > MaxMessageBytes ||
                _bufferedBytes + wrapper.DtoChunk.Length > MaxBufferedBytes)
            {
                Remove(wrapper.InstanceId);
                return false;
            }
            // Own the memory; the caller cannot alter previously accepted chunks.
            pending.Chunks.Add(wrapper.DtoChunk.ToArray());
            pending.Size += wrapper.DtoChunk.Length;
            _bufferedBytes += wrapper.DtoChunk.Length;
            if (!wrapper.IsLastChunk) return false;
            buffer = new byte[pending.Size];
            var offset = 0;
            foreach (var chunk in pending.Chunks) { chunk.CopyTo(buffer, offset); offset += chunk.Length; }
            Remove(wrapper.InstanceId);
        }
        try
        {
            result = MessagePackSerializer.Deserialize<T>(buffer, ProtocolSerialization.Options);
            return result is not null;
        }
        catch (MessagePackSerializationException) { return false; }
    }

    private static void Remove(string id)
    {
        if (Pending.Remove(id, out var pending)) _bufferedBytes -= pending.Size;
    }
}
