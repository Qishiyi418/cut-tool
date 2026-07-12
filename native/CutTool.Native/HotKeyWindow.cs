using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace CutTool.Native
{
    internal sealed class HotKeyWindow : NativeWindow, IDisposable
    {
        private const int HotKeyId = 1;
        private bool registered;

        internal event EventHandler CaptureRequested;

        internal HotKeyWindow()
        {
            CreateHandle(new CreateParams());
        }

        internal bool Register(string text)
        {
            Unregister();
            uint modifiers;
            uint key;
            if (!HotKeyParser.TryParse(text, out modifiers, out key)) return false;
            registered = NativeMethods.RegisterHotKey(Handle, HotKeyId, modifiers, key);
            return registered;
        }

        internal void PostCapture()
        {
            NativeMethods.PostMessage(Handle, NativeMethods.WM_APP_CAPTURE, IntPtr.Zero, IntPtr.Zero);
        }

        private void Unregister()
        {
            if (!registered) return;
            NativeMethods.UnregisterHotKey(Handle, HotKeyId);
            registered = false;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == NativeMethods.WM_HOTKEY || message.Msg == NativeMethods.WM_APP_CAPTURE)
            {
                EventHandler handler = CaptureRequested;
                if (handler != null) handler(this, EventArgs.Empty);
                return;
            }
            base.WndProc(ref message);
        }

        public void Dispose()
        {
            Unregister();
            DestroyHandle();
        }
    }

    internal static class HotKeyParser
    {
        private static readonly Dictionary<string, Keys> NamedKeys = new Dictionary<string, Keys>(StringComparer.OrdinalIgnoreCase)
        {
            { "Space", Keys.Space }, { "Left", Keys.Left }, { "Right", Keys.Right },
            { "Up", Keys.Up }, { "Down", Keys.Down }, { "PrintScreen", Keys.PrintScreen }
        };

        internal static bool TryParse(string text, out uint modifiers, out uint key)
        {
            modifiers = 0;
            key = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string[] parts = text.Split('+');
            string keyName = null;
            foreach (string raw in parts)
            {
                string part = raw.Trim();
                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) modifiers |= NativeMethods.MOD_CONTROL;
                else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) modifiers |= NativeMethods.MOD_ALT;
                else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) modifiers |= NativeMethods.MOD_SHIFT;
                else if (part.Equals("Super", StringComparison.OrdinalIgnoreCase) || part.Equals("Win", StringComparison.OrdinalIgnoreCase)) modifiers |= NativeMethods.MOD_WIN;
                else if (keyName == null) keyName = part;
                else return false;
            }

            if (string.IsNullOrEmpty(keyName)) return false;
            Keys parsed;
            if (keyName.Length == 1)
            {
                char character = char.ToUpperInvariant(keyName[0]);
                if ((character < 'A' || character > 'Z') && (character < '0' || character > '9')) return false;
                parsed = (Keys)character;
            }
            else if (keyName.StartsWith("F", StringComparison.OrdinalIgnoreCase))
            {
                int functionNumber;
                if (!int.TryParse(keyName.Substring(1), out functionNumber) || functionNumber < 1 || functionNumber > 24) return false;
                parsed = Keys.F1 + (functionNumber - 1);
            }
            else if (!NamedKeys.TryGetValue(keyName, out parsed))
            {
                return false;
            }

            if (modifiers == 0 && parsed < Keys.F1) return false;
            key = (uint)parsed;
            return true;
        }
    }
}
