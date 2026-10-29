namespace RitsukageGif.Desktop
{
    internal static class HotkeyKeyMap
    {
        private static readonly (uint Code, string Name, string X11, uint? Mac)[] Keys =
        [
            (0x03, "Cancel", "Cancel", null), (0x08, "Back", "BackSpace", 51), (0x09, "Tab", "Tab", 48),
            (0x0c, "Clear", "Clear", 71),
            (0x0d, "Enter", "Return", 36), (0x13, "Pause", "Pause", null),
            (0x14, "CapsLock", "Caps_Lock", 57), (0x1b, "Escape", "Escape", 53),
            (0x20, "Space", "space", 49), (0x21, "PageUp", "Prior", 116),
            (0x22, "PageDown", "Next", 121), (0x23, "End", "End", 119),
            (0x24, "Home", "Home", 115), (0x25, "Left", "Left", 123),
            (0x26, "Up", "Up", 126), (0x27, "Right", "Right", 124),
            (0x28, "Down", "Down", 125), (0x29, "Select", "Select", null), (0x2a, "Print", "Print", null),
            (0x2b, "Execute", "Execute", null), (0x2c, "PrintScreen", "Print", null),
            (0x2d, "Insert", "Insert", 114), (0x2e, "Delete", "Delete", 117),
            (0x2f, "Help", "Help", 114), (0x5d, "Apps", "Menu", null), (0x5f, "Sleep", "XF86Sleep", null),
            (0x60, "NumPad0", "KP_0", 82), (0x61, "NumPad1", "KP_1", 83),
            (0x62, "NumPad2", "KP_2", 84), (0x63, "NumPad3", "KP_3", 85),
            (0x64, "NumPad4", "KP_4", 86), (0x65, "NumPad5", "KP_5", 87),
            (0x66, "NumPad6", "KP_6", 88), (0x67, "NumPad7", "KP_7", 89),
            (0x68, "NumPad8", "KP_8", 91), (0x69, "NumPad9", "KP_9", 92),
            (0x6a, "Multiply", "KP_Multiply", 67), (0x6b, "Add", "KP_Add", 69),
            (0x6c, "Separator", "KP_Separator", null),
            (0x6d, "Subtract", "KP_Subtract", 78), (0x6e, "Decimal", "KP_Decimal", 65),
            (0x6f, "Divide", "KP_Divide", 75), (0x90, "NumLock", "Num_Lock", 71),
            (0x91, "Scroll", "Scroll_Lock", null),
            (0xa6, "BrowserBack", "XF86Back", null), (0xa7, "BrowserForward", "XF86Forward", null),
            (0xa8, "BrowserRefresh", "XF86Refresh", null), (0xa9, "BrowserStop", "XF86Stop", null),
            (0xaa, "BrowserSearch", "XF86Search", null), (0xab, "BrowserFavorites", "XF86Favorites", null),
            (0xac, "BrowserHome", "XF86HomePage", null), (0xad, "VolumeMute", "XF86AudioMute", 74),
            (0xae, "VolumeDown", "XF86AudioLowerVolume", 73), (0xaf, "VolumeUp", "XF86AudioRaiseVolume", 72),
            (0xb0, "MediaNextTrack", "XF86AudioNext", null), (0xb1, "MediaPreviousTrack", "XF86AudioPrev", null),
            (0xb2, "MediaStop", "XF86AudioStop", null), (0xb3, "MediaPlayPause", "XF86AudioPlay", null),
            (0xb4, "LaunchMail", "XF86Mail", null), (0xb5, "SelectMedia", "XF86AudioMedia", null),
            (0xb6, "LaunchApplication1", "XF86Launch0", null), (0xb7, "LaunchApplication2", "XF86Launch1", null),
            (0xba, "OemSemicolon", "semicolon", 41), (0xbb, "OemPlus", "equal", 24),
            (0xbc, "OemComma", "comma", 43), (0xbd, "OemMinus", "minus", 27),
            (0xbe, "OemPeriod", "period", 47), (0xbf, "OemQuestion", "slash", 44),
            (0xc0, "OemTilde", "grave", 50), (0xdb, "OemOpenBrackets", "bracketleft", 33),
            (0xdc, "OemPipe", "backslash", 42), (0xdd, "OemCloseBrackets", "bracketright", 30),
            (0xde, "OemQuotes", "apostrophe", 39), (0xdf, "Oem8", "section", null), (0xe2, "OemBackslash", "less", 10),
        ];

        internal static bool TryParse(string name, out uint code)
        {
            name = name.ToUpperInvariant() switch
            {
                "RETURN" => "Enter", "ESC" => "Escape", "BACKSPACE" => "Back",
                "`" => "OemTilde",
                "PRIOR" => "PageUp", "NEXT" => "PageDown", "CAPITAL" => "CapsLock",
                "SCROLLLOCK" => "Scroll", "SNAPSHOT" => "PrintScreen",
                "OEM1" => "OemSemicolon", "OEM2" => "OemQuestion", "OEM3" => "OemTilde",
                "OEM4" => "OemOpenBrackets", "OEM5" => "OemPipe", "OEM6" => "OemCloseBrackets",
                "OEM7" => "OemQuotes", "OEM102" => "OemBackslash", _ => name,
            };
            foreach (var key in Keys)
                if (name.Equals(key.Name, StringComparison.OrdinalIgnoreCase))
                {
                    code = key.Code;
                    return true;
                }

            if (name.Length == 2 && char.ToUpperInvariant(name[0]) == 'D' && char.IsAsciiDigit(name[1]))
            {
                code = name[1];
                return true;
            }

            code = 0;
            return false;
        }

        internal static string X11Name(uint code)
        {
            foreach (var key in Keys)
                if (key.Code == code)
                    return key.X11;
            return code is >= 0x70 and <= 0x87 ? "F" + (code - 0x6f) : ((char)code).ToString().ToLowerInvariant();
        }

        internal static uint? MacCode(uint code)
        {
            foreach (var key in Keys)
                if (key.Code == code)
                    return key.Mac;
            return null;
        }
    }
}
