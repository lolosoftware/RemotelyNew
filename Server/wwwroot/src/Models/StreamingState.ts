import { FrameBuffer } from "../FrameBuffer.js";
export class StreamingState {
    readonly Frames = new FrameBuffer();
    StreamEnded = false;
}
