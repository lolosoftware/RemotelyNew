/** Bounds memory and preserves differential-frame order across arbitrary network chunks. */
export class FrameBuffer {
    static headerSize = 28;
    static maxFrameBytes = 32 * 1024 * 1024;
    static maxBufferedBytes = 64 * 1024 * 1024;
    buffer = new Blob();
    get size() { return this.buffer.size; }
    append(chunk) {
        if (this.buffer.size + chunk.byteLength > FrameBuffer.maxBufferedBytes)
            throw new Error("Remote display buffer exceeded its limit.");
        this.buffer = new Blob([this.buffer, chunk.slice().buffer]);
    }
    clear() { this.buffer = new Blob(); }
    async read() {
        if (this.buffer.size < FrameBuffer.headerSize)
            return undefined;
        const header = new DataView(await this.buffer.slice(0, FrameBuffer.headerSize).arrayBuffer());
        const length = header.getInt32(0, true);
        const x = header.getFloat32(4, true);
        const y = header.getFloat32(8, true);
        const width = header.getFloat32(12, true);
        const height = header.getFloat32(16, true);
        const timestamp = Number(header.getBigUint64(20, true));
        if (length <= 0 || length > FrameBuffer.maxFrameBytes ||
            ![x, y, width, height].every(Number.isFinite) || x < 0 || y < 0 ||
            width <= 0 || height <= 0 || width > 32768 || height > 32768 ||
            width * height > 64 * 1024 * 1024 || !Number.isSafeInteger(timestamp))
            throw new Error("Invalid remote display frame.");
        const end = FrameBuffer.headerSize + length;
        if (this.buffer.size < end)
            return undefined;
        const image = this.buffer.slice(FrameBuffer.headerSize, end);
        this.buffer = this.buffer.slice(end);
        return { image, x, y, width, height, timestamp };
    }
}
//# sourceMappingURL=FrameBuffer.js.map