export class DtoWrapper {
    DtoChunk;
    DtoType;
    IsFirstChunk;
    IsLastChunk;
    RequestId;
    InstanceId;
    SequenceId;
}
export class EmptyDto {
}
export class TextTransferDto {
    constructor(text, typeText) {
        this.Text = text;
        this.TypeText = typeText;
    }
    Text;
    TypeText;
}
export class CtrlAltDelDto {
}
export class FileDto {
    constructor(buffer, fileName, messageId, endOfFile, startOfFile) {
        this.Buffer = buffer;
        this.FileName = fileName;
        this.MessageId = messageId;
        this.EndOfFile = endOfFile;
        this.StartOfFile = startOfFile;
    }
    Buffer;
    FileName;
    MessageId;
    EndOfFile;
    StartOfFile;
}
export class KeyDownDto {
    Code;
    constructor(key, Code) {
        this.Code = Code;
        this.Key = key;
    }
    Key;
}
export class KeyPressDto {
    constructor(key) {
        this.Key = key;
    }
    Key;
}
export class KeyUpDto {
    Code;
    constructor(key, Code) {
        this.Code = Code;
        this.Key = key;
    }
    Key;
}
export class MouseDownDto {
    constructor(button, percentX, percentY) {
        this.Button = button;
        this.PercentX = percentX;
        this.PercentY = percentY;
    }
    Button;
    PercentX;
    PercentY;
}
export class MouseMoveDto {
    constructor(percentX, percentY) {
        this.PercentX = percentX;
        this.PercentY = percentY;
    }
    PercentX;
    PercentY;
}
export class MouseUpDto {
    constructor(button, percentX, percentY) {
        this.Button = button;
        this.PercentX = percentX;
        this.PercentY = percentY;
    }
    Button;
    PercentX;
    PercentY;
}
export class MouseWheelDto {
    constructor(deltaX, deltaY) {
        this.DeltaX = deltaX;
        this.DeltaY = deltaY;
    }
    DeltaX;
    DeltaY;
}
export class SelectScreenDto {
    constructor(displayName) {
        this.DisplayName = displayName;
    }
    DisplayName;
}
export class FrameReceivedDto {
    constructor(timestamp) {
        this.Timestamp = timestamp;
    }
    Timestamp;
}
export class TapDto {
    constructor(percentX, percentY) {
        this.PercentX = percentX;
        this.PercentY = percentY;
    }
    PercentX;
    PercentY;
}
export class ToggleAudioDto {
    constructor(toggleOn) {
        this.ToggleOn = toggleOn;
    }
    ToggleOn;
}
export class ToggleBlockInputDto {
    constructor(toggleOn) {
        this.ToggleOn = toggleOn;
    }
    ToggleOn;
}
export class WindowsSessionsDto {
    WindowsSessions;
}
//# sourceMappingURL=Dtos.js.map