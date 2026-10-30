export type KeyboardMode = "text" | "physical";

export interface KeyEvent {
    key: string;
    code: string;
    ctrlKey: boolean;
    altKey: boolean;
    metaKey: boolean;
    repeat: boolean;
    isComposing: boolean;
    getModifierState(name: string): boolean;
}

export interface KeyboardSink {
    down(key: string, code?: string): Promise<void>;
    up(key: string, code?: string): Promise<void>;
    text(value: string): Promise<void>;
    reset(): Promise<void>;
}

/** Keeps key releases paired with their original presses, even if modifiers change. */
export class RemoteKeyboard {
    private pressed = new Map<string, { key: string; code?: string }>();
    private pending: Promise<void> = Promise.resolve();
    private queued = 0;
    mode: KeyboardMode = "text";

    constructor(private sink: KeyboardSink, private onError: (error: unknown) => void = console.error) {}

    keyDown(event: KeyEvent): boolean {
        if (event.isComposing || event.key === "Process" || event.key === "Unidentified") return false;
        if (this.mode === "text" && event.key === "Dead") return false;
        if (event.repeat && this.queued > 32) return true;

        const altGraph = event.getModifierState("AltGraph");
        if (this.mode === "text" && event.key === "AltGraph") return true;
        const printable = Array.from(event.key).length === 1;
        if (this.mode === "text" && printable &&
            (altGraph || (!event.ctrlKey && !event.altKey && !event.metaKey))) {
            if (altGraph) this.releaseAll();
            this.text(event.key);
            return true;
        }

        const identity = event.code || event.key;
        const original = this.pressed.get(identity);
        if (original && !event.repeat) return true;
        const key = original || { key: event.key, code: this.mode === "physical" ? event.code : undefined };
        this.pressed.set(identity, key);
        this.enqueue(() => this.sink.down(key.key, key.code));
        return true;
    }

    keyUp(event: Pick<KeyEvent, "code" | "key">): boolean {
        const identity = event.code || event.key;
        const key = this.pressed.get(identity);
        if (!key) return false;
        this.pressed.delete(identity);
        this.enqueue(() => this.sink.up(key.key, key.code));
        return true;
    }

    text(value: string): void {
        if (value) this.enqueue(() => this.sink.text(value));
    }

    releaseAll(): void {
        for (const key of this.pressed.values()) this.enqueue(() => this.sink.up(key.key, key.code));
        this.pressed.clear();
        this.enqueue(() => this.sink.reset());
    }

    flush(): Promise<void> { return this.pending; }

    private enqueue(command: () => Promise<void>): void {
        this.queued++;
        this.pending = this.pending.then(command).catch(this.onError).finally(() => { this.queued--; });
    }
}
