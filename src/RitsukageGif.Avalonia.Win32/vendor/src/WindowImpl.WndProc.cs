// Copyright (c) The Avalonia Project. All rights reserved.
// Licensed under the MIT license. See licence.md file in the project root for full license information.

using System;
using Avalonia.Controls.Platform;

namespace Avalonia.Win32
{
    internal partial class WindowImpl
    {
        public INativeControlHostImpl NativeControlHost => _nativeControlHost;

        protected virtual bool ShouldTakeFocusOnClick => true;

        protected virtual IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            var lRet = IntPtr.Zero;
            var callDwp = true;

            if (IsClientAreaExtendedToDecorations) lRet = CustomCaptionProc(hWnd, msg, wParam, lParam, ref callDwp);

            if (callDwp) lRet = AppWndProc(hWnd, msg, wParam, lParam);

            return lRet;
        }
    }
}
