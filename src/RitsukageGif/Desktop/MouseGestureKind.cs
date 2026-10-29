namespace RitsukageGif.Desktop
{
    [Flags]
    internal enum MouseGestureKind
    {
        None = 0,
        LeftClick = 1,
        RightClick = 2,
        MiddleClick = 4,
        LeftDoubleClick = 8,
        RightDoubleClick = 16,
        MiddleDoubleClick = 32,
        LeftDrag = 64,
        RightDrag = 128,
        MiddleDrag = 256,
        WheelUp = 512,
        WheelDown = 1024,
        WheelLeft = 2048,
        WheelRight = 4096,
        Wheel = 8192,
        X1Click = 16384,
        X2Click = 32768,
        X1DoubleClick = 65536,
        X2DoubleClick = 131072,
        X1Drag = 262144,
        X2Drag = 524288,

        Clicks = LeftClick | RightClick | MiddleClick | LeftDoubleClick | RightDoubleClick | MiddleDoubleClick |
                 X1Click | X2Click | X1DoubleClick | X2DoubleClick,
        Drags = LeftDrag | RightDrag | MiddleDrag | X1Drag | X2Drag,
        Wheels = WheelUp | WheelDown | WheelLeft | WheelRight | Wheel,
        Discrete = Clicks | Wheels,
    }
}
