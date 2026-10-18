using System.Globalization;
using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Windows.Shell;

/// <summary>Modifier flags for a global hotkey (values mirror the Win32 <c>MOD_*</c> constants).</summary>
[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = NativeMethods.MOD_ALT,
    Control = NativeMethods.MOD_CONTROL,
    Shift = NativeMethods.MOD_SHIFT,
    Win = NativeMethods.MOD_WIN,
}

/// <summary>
/// A parsed global hotkey: modifiers + virtual-key code. Pure value type; parsing and formatting are
/// fully unit tested and the service only performs the Win32 registration.
/// </summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, uint VirtualKey)
{
    /// <summary>Parses a gesture string such as <c>Ctrl+Alt+D</c>, <c>Win+Shift+S</c> or <c>Ctrl+F12</c>.</summary>
    public static bool TryParse(string? text, out HotkeyGesture gesture, out string? error)
    {
        gesture = default;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Hotkey is empty.";
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            error = "Hotkey is empty.";
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        uint virtualKey = 0;
        var keyFound = false;

        foreach (var rawPart in parts)
        {
            var part = rawPart.Trim();
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    if (modifiers.HasFlag(HotkeyModifiers.Control))
                    {
                        error = "Duplicate modifier 'Ctrl'.";
                        return false;
                    }

                    modifiers |= HotkeyModifiers.Control;
                    continue;
                case "alt" or "menu":
                    if (modifiers.HasFlag(HotkeyModifiers.Alt))
                    {
                        error = "Duplicate modifier 'Alt'.";
                        return false;
                    }

                    modifiers |= HotkeyModifiers.Alt;
                    continue;
                case "shift":
                    if (modifiers.HasFlag(HotkeyModifiers.Shift))
                    {
                        error = "Duplicate modifier 'Shift'.";
                        return false;
                    }

                    modifiers |= HotkeyModifiers.Shift;
                    continue;
                case "win" or "windows" or "meta" or "super":
                    if (modifiers.HasFlag(HotkeyModifiers.Win))
                    {
                        error = "Duplicate modifier 'Win'.";
                        return false;
                    }

                    modifiers |= HotkeyModifiers.Win;
                    continue;
            }

            if (keyFound)
            {
                error = $"Hotkey contains more than one non-modifier key ('{part}').";
                return false;
            }

            if (!TryParseKey(part, out virtualKey))
            {
                error = $"Unknown hotkey key '{part}'.";
                return false;
            }

            keyFound = true;
        }

        if (!keyFound)
        {
            error = "Hotkey has no non-modifier key.";
            return false;
        }

        gesture = new HotkeyGesture(modifiers, virtualKey);
        return true;
    }

    /// <summary>Parses a gesture or throws <see cref="FormatException"/> with the parser error.</summary>
    public static HotkeyGesture Parse(string text)
        => TryParse(text, out var gesture, out var error) ? gesture : throw new FormatException(error);

    private static bool TryParseKey(string token, out uint virtualKey)
    {
        virtualKey = 0;
        if (token.Length == 0)
        {
            return false;
        }

        if (token.Length == 1)
        {
            var c = char.ToUpperInvariant(token[0]);
            if (c is >= 'A' and <= 'Z')
            {
                virtualKey = c;
                return true;
            }

            if (c is >= '0' and <= '9')
            {
                virtualKey = c;
                return true;
            }
        }

        if (token.Length is >= 2 and <= 3 && (token[0] is 'F' or 'f')
            && int.TryParse(token.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var fn)
            && fn is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + fn - 1); // VK_F1..VK_F24
            return true;
        }

        virtualKey = token.ToLowerInvariant() switch
        {
            "space" => 0x20,
            "tab" => 0x09,
            "enter" or "return" => 0x0D,
            "esc" or "escape" => 0x1B,
            "backspace" => 0x08,
            "insert" or "ins" => 0x2D,
            "delete" or "del" => 0x2E,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" or "pgup" => 0x21,
            "pagedown" or "pgdn" => 0x22,
            "left" => 0x25,
            "up" => 0x26,
            "right" => 0x27,
            "down" => 0x28,
            "printscreen" or "prtsc" => 0x2C,
            "pause" => 0x13,
            "capslock" => 0x14,
            "numlock" => 0x90,
            "scrolllock" => 0x91,
            "volumeup" => 0xAF,
            "volumedown" => 0xAE,
            "volumemute" => 0xAD,
            "medianext" => 0xB0,
            "mediaprev" => 0xB1,
            "mediastop" => 0xB2,
            "mediaplaypause" => 0xB3,
            _ => 0,
        };

        return virtualKey != 0;
    }

    /// <summary>Canonical string form, e.g. <c>Ctrl+Alt+D</c>.</summary>
    public override string ToString()
    {
        if (VirtualKey == 0)
        {
            return "<empty>";
        }

        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(HotkeyKeyFormatter.Format(VirtualKey));
        return string.Join('+', parts);
    }

    /// <summary>Maps to the Win32 <c>MOD_*</c> value (always includes <c>MOD_NOREPEAT</c> when registering).</summary>
    public uint ToNativeModifiers() => (uint)Modifiers;
}

/// <summary>Formats virtual-key codes back to stable names.</summary>
public static class HotkeyKeyFormatter
{
    public static string Format(uint virtualKey)
    {
        if (virtualKey is >= 'A' and <= 'Z')
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= '0' and <= '9')
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x70 and <= 0x87)
        {
            return $"F{virtualKey - 0x70 + 1}";
        }

        return virtualKey switch
        {
            0x20 => "Space",
            0x0C => "Clear",
            0x09 => "Tab",
            0x0D => "Enter",
            0x1B => "Esc",
            0x08 => "Backspace",
            0x2D => "Insert",
            0x2E => "Delete",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2C => "PrintScreen",
            0x13 => "Pause",
            0x14 => "CapsLock",
            0x90 => "NumLock",
            0x91 => "ScrollLock",
            0xAF => "VolumeUp",
            0xAE => "VolumeDown",
            0xAD => "VolumeMute",
            0xB0 => "MediaNext",
            0xB1 => "MediaPrev",
            0xB2 => "MediaStop",
            0xB3 => "MediaPlayPause",
            _ => $"VK(0x{virtualKey:X2})",
        };
    }
}
