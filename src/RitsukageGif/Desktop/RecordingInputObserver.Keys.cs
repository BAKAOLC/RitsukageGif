using Avalonia.Input;

namespace RitsukageGif.Desktop
{
    internal sealed partial class RecordingInputObserver
    {
        private static readonly Dictionary<uint, Key> X11NamedKeys = new()
        {
            [KeyName("ESC")] = Key.Escape, [KeyName("TAB")] = Key.Tab, [KeyName("BKSP")] = Key.Back,
            [KeyName("RTRN")] = Key.Enter, [KeyName("SPCE")] = Key.Space,
            [KeyName("LFSH")] = Key.LeftShift, [KeyName("RTSH")] = Key.RightShift,
            [KeyName("LCTL")] = Key.LeftCtrl, [KeyName("RCTL")] = Key.RightCtrl,
            [KeyName("LALT")] = Key.LeftAlt, [KeyName("RALT")] = Key.RightAlt,
            [KeyName("LWIN")] = Key.LWin, [KeyName("RWIN")] = Key.RWin,
            [KeyName("CAPS")] = Key.CapsLock, [KeyName("MENU")] = Key.Apps,
            [KeyName("TLDE")] = Key.OemTilde, [KeyName("BKSL")] = Key.OemPipe,
            [KeyName("LSGT")] = Key.OemBackslash, [KeyName("INS")] = Key.Insert,
            [KeyName("DELE")] = Key.Delete, [KeyName("HOME")] = Key.Home, [KeyName("END")] = Key.End,
            [KeyName("PGUP")] = Key.PageUp, [KeyName("PGDN")] = Key.PageDown,
            [KeyName("LEFT")] = Key.Left, [KeyName("RGHT")] = Key.Right,
            [KeyName("UP")] = Key.Up, [KeyName("DOWN")] = Key.Down,
            [KeyName("NMLK")] = Key.NumLock, [KeyName("SCLK")] = Key.Scroll,
            [KeyName("PRSC")] = Key.PrintScreen, [KeyName("PAUS")] = Key.Pause,
            [KeyName("KPDV")] = Key.Divide, [KeyName("KPMU")] = Key.Multiply,
            [KeyName("KPSU")] = Key.Subtract, [KeyName("KPAD")] = Key.Add,
            [KeyName("KPEN")] = Key.Enter, [KeyName("KPDL")] = Key.Decimal,
        };

        private static uint KeyName(ReadOnlySpan<char> name)
        {
            uint result = 0;
            for (var index = 0; index < name.Length; index++) result |= (uint)name[index] << (index * 8);
            return result;
        }

        private static Key X11Key(uint name)
        {
            if (X11NamedKeys.TryGetValue(name, out var key)) return key;
            var first = (char)(name & 255);
            var second = (char)((name >> 8) & 255);
            var third = (char)((name >> 16) & 255);
            var fourth = (char)(name >> 24);
            switch (first, second, third, fourth)
            {
                case ('F', 'K', >= '0' and <= '9', >= '0' and <= '9'):
                    var function = (third - '0') * 10 + fourth - '0';
                    return function is >= 1 and <= 24 ? Key.F1 + function - 1 : Key.None;
                case ('K', 'P', >= '0' and <= '9', '\0'):
                    return Key.NumPad0 + third - '0';
                case ('A', _, >= '0' and <= '9', >= '0' and <= '9'): break;
                default: return Key.None;
            }

            var position = (third - '0') * 10 + fourth - '0' - 1;
            ReadOnlySpan<Key> row = second switch
            {
                'E' =>
                [
                    Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8, Key.D9, Key.D0, Key.OemMinus,
                    Key.OemPlus,
                ],
                'D' =>
                [
                    Key.Q, Key.W, Key.E, Key.R, Key.T, Key.Y, Key.U, Key.I, Key.O, Key.P, Key.OemOpenBrackets,
                    Key.OemCloseBrackets,
                ],
                'C' => [Key.A, Key.S, Key.D, Key.F, Key.G, Key.H, Key.J, Key.K, Key.L, Key.OemSemicolon, Key.OemQuotes],
                'B' => [Key.Z, Key.X, Key.C, Key.V, Key.B, Key.N, Key.M, Key.OemComma, Key.OemPeriod, Key.OemQuestion],
                _ => [],
            };
            return (uint)position < (uint)row.Length ? row[position] : Key.None;
        }

        private static Key MacKey(uint code)
        {
            return code switch
            {
                0 => Key.A, 1 => Key.S, 2 => Key.D, 3 => Key.F, 4 => Key.H, 5 => Key.G,
                6 => Key.Z, 7 => Key.X, 8 => Key.C, 9 => Key.V, 10 => Key.OemBackslash, 11 => Key.B,
                12 => Key.Q, 13 => Key.W, 14 => Key.E, 15 => Key.R, 16 => Key.Y, 17 => Key.T,
                18 => Key.D1, 19 => Key.D2, 20 => Key.D3, 21 => Key.D4, 22 => Key.D6, 23 => Key.D5,
                24 => Key.OemPlus, 25 => Key.D9, 26 => Key.D7, 27 => Key.OemMinus, 28 => Key.D8, 29 => Key.D0,
                30 => Key.OemCloseBrackets, 31 => Key.O, 32 => Key.U, 33 => Key.OemOpenBrackets,
                34 => Key.I, 35 => Key.P, 36 => Key.Enter, 37 => Key.L, 38 => Key.J, 39 => Key.OemQuotes,
                40 => Key.K, 41 => Key.OemSemicolon, 42 => Key.OemPipe, 43 => Key.OemComma,
                44 => Key.OemQuestion, 45 => Key.N, 46 => Key.M, 47 => Key.OemPeriod,
                48 => Key.Tab, 49 => Key.Space, 50 => Key.OemTilde, 51 => Key.Back, 53 => Key.Escape,
                54 => Key.RWin, 55 => Key.LWin, 56 => Key.LeftShift, 57 => Key.CapsLock,
                58 => Key.LeftAlt, 59 => Key.LeftCtrl, 60 => Key.RightShift, 61 => Key.RightAlt, 62 => Key.RightCtrl,
                65 => Key.Decimal, 67 => Key.Multiply, 69 => Key.Add, 71 => Key.Clear, 75 => Key.Divide,
                76 => Key.Enter, 78 => Key.Subtract, 81 => Key.OemPlus,
                82 => Key.NumPad0, 83 => Key.NumPad1, 84 => Key.NumPad2, 85 => Key.NumPad3, 86 => Key.NumPad4,
                87 => Key.NumPad5, 88 => Key.NumPad6, 89 => Key.NumPad7, 91 => Key.NumPad8, 92 => Key.NumPad9,
                64 => Key.F17, 79 => Key.F18, 80 => Key.F19, 90 => Key.F20,
                96 => Key.F5, 97 => Key.F6, 98 => Key.F7, 99 => Key.F3, 100 => Key.F8, 101 => Key.F9,
                103 => Key.F11, 105 => Key.F13, 106 => Key.F16, 107 => Key.F14,
                109 => Key.F10, 111 => Key.F12, 113 => Key.F15, 114 => Key.Help, 115 => Key.Home,
                116 => Key.PageUp, 117 => Key.Delete, 118 => Key.F4, 119 => Key.End, 120 => Key.F2,
                121 => Key.PageDown, 122 => Key.F1, 123 => Key.Left, 124 => Key.Right, 125 => Key.Down, 126 => Key.Up,
                72 => Key.VolumeUp, 73 => Key.VolumeDown, 74 => Key.VolumeMute,
                _ => Key.None,
            };
        }
    }
}
