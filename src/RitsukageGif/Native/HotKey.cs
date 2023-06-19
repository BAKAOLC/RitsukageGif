using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RitsukageGif.Native
{
    internal static class HotKey
    {
        public static void RegKey(IntPtr hwnd, int hotKey_id, KeyModifiers keyModifiers, Keys key)
        {
            if (User32.RegisterHotKey(hwnd, hotKey_id, keyModifiers, key)) return;
            if (Marshal.GetLastWin32Error() == 1409)
            {
                throw new InvalidOperationException("热键已被占用");
            }
            else
            {
                throw new InvalidOperationException("热键注册失败");
            }
        }

        public static void UnRegKey(IntPtr hwnd, int hotKey_id)
        {
            User32.RemoveRegisterHotKey(hwnd, hotKey_id);
        }
    }
}