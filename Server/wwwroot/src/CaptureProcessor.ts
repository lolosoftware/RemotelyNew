import { ViewerApp } from "./App.js";
import { StreamingState } from "./Models/StreamingState.js";
import { Screen2DContext, SetStatusMessage } from "./UI.js";

export async function ProcessStream(state: StreamingState): Promise<void> {
    if (state.StreamEnded) { state.Frames.clear(); return; }
    try {
        const started = performance.now();
        for (let i = 0; i < 4; i++) {
            const frame = await state.Frames.read();
            if (!frame) break;
            const bitmap = await createImageBitmap(frame.image);
            try {
                if (state.StreamEnded) return;
                Screen2DContext.drawImage(bitmap, frame.x, frame.y, frame.width, frame.height);
            } finally { bitmap.close(); }
            await ViewerApp.MessageSender.SendFrameReceived(frame.timestamp);
            if (performance.now() - started > 12) break;
        }
    } catch (error) {
        console.error("Remote display failed.", error);
        state.StreamEnded = true;
        state.Frames.clear();
        SetStatusMessage("Display stream failed. Please reconnect.");
        await ViewerApp.ViewerHubConnection.Connection.stop();
    } finally {
        if (!state.StreamEnded) requestAnimationFrame(() => { void ProcessStream(state); });
    }
}
