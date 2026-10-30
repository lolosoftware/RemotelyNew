import { DtoType } from "./Enums/DtoType.js";
import { DtoWrapper } from "./Interfaces/Dtos.js";
import { MessagePack } from "./Interfaces/MessagePack.js";
import { CreateGUID } from "./Utilities.js";

const maxMessageBytes = 4 * 1024 * 1024;
const maxBufferedBytes = 16 * 1024 * 1024;
const Chunks = new Map<string, { chunks: Uint8Array[]; size: number; type: DtoType; request: string; expires: number }>();
let bufferedBytes = 0;
function remove(id: string) {
    const entry = Chunks.get(id);
    if (entry) bufferedBytes -= entry.size;
    Chunks.delete(id);
}
const MsgPack: MessagePack = window["MessagePack"];

export function ChunkDto<T>(dto: T, dtoType: DtoType, requestId = null, chunkSize = 50000) : DtoWrapper[] {
    if (chunkSize <= 0 || chunkSize > 50000) throw new Error("Invalid chunk size.");
    const messageBytes = MsgPack.encode(dto);
    if (messageBytes.length > maxMessageBytes) throw new Error("Message exceeds the protocol limit.");
    const instanceId = CreateGUID();
    const chunks: Array<Uint8Array> = [];
    const wrappers: Array<DtoWrapper> = [];
    
    for (let i = 0; i < messageBytes.length; i += chunkSize) {

        const chunk = messageBytes.subarray(i, i + chunkSize);
        chunks.push(chunk);
    }

    for (let i = 0; i < chunks.length; i++) {
        const chunk = chunks[i];

        const wrapper = new DtoWrapper();
        wrapper.DtoChunk = chunk;
        wrapper.DtoType = dtoType;
        wrapper.InstanceId = instanceId;
        wrapper.IsFirstChunk = i == 0;
        wrapper.IsLastChunk = i == chunks.length - 1;
        wrapper.RequestId = requestId;
        wrapper.SequenceId = i;

        wrappers.push(wrapper);
    }

    return wrappers;
}


export function TryComplete<T>(wrapper: DtoWrapper): T | undefined {
    if (typeof wrapper.InstanceId !== "string" || !wrapper.InstanceId || wrapper.InstanceId.length > 64 ||
        !(wrapper.DtoChunk instanceof Uint8Array) || wrapper.DtoChunk.length === 0 || wrapper.DtoChunk.length > 50000 ||
        !Number.isInteger(wrapper.SequenceId) || wrapper.SequenceId < 0) return undefined;
    const now = Date.now();
    for (const [id, entry] of Chunks) if (entry.expires <= now) remove(id);
    if (wrapper.IsFirstChunk) {
        if (wrapper.SequenceId !== 0 || Chunks.has(wrapper.InstanceId)) { remove(wrapper.InstanceId); return undefined; }
        if (Chunks.size >= 64) return undefined;
        Chunks.set(wrapper.InstanceId, { chunks: [], size: 0, type: wrapper.DtoType, request: wrapper.RequestId, expires: now + 30000 });
    }
    const entry = Chunks.get(wrapper.InstanceId);
    if (!entry) return undefined;
    if (wrapper.SequenceId !== entry.chunks.length || wrapper.DtoType !== entry.type || wrapper.RequestId !== entry.request ||
        entry.size + wrapper.DtoChunk.length > maxMessageBytes || bufferedBytes + wrapper.DtoChunk.length > maxBufferedBytes) {
        remove(wrapper.InstanceId); return undefined;
    }
    entry.chunks.push(wrapper.DtoChunk.slice());
    entry.size += wrapper.DtoChunk.length;
    bufferedBytes += wrapper.DtoChunk.length;
    if (!wrapper.IsLastChunk) return undefined;
    const buffer = new Uint8Array(entry.size);
    let offset = 0;
    for (const chunk of entry.chunks) { buffer.set(chunk, offset); offset += chunk.length; }
    remove(wrapper.InstanceId);
    try { return MsgPack.decode<T>(buffer); }
    catch { return undefined; }
}
