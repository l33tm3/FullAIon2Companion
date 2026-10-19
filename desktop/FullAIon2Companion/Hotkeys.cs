namespace FullAIon2Companion;

/// <summary>Global shortcuts from Configuración > Atajos, stored as text ("Ctrl+Alt+F") in the settings.</summary>
public static class Hotkeys
{
    /// <summary>Action keys in hotkey-id order (id = index + 1).</summary>
    public static readonly string[] Actions = ["overlay", "move", "drops", "panel", "reset", "minimap", "breakdown"];
    public static int IdOf(string action) => Array.IndexOf(Actions, action) + 1;
    public static string? ActionOf(int id) => id >= 1 && id <= Actions.Length ? Actions[id - 1] : null;
    /// <summary>Parses "Ctrl+Alt+F" into RegisterHotKey modifiers (with MOD_NOREPEAT) and a virtual key. A global
    /// shortcut needs Ctrl, Alt or Win: Shift+letter alone would swallow typing in the game's chat.</summary>
    public static bool TryParse(string combo, out uint mods, out uint vk)
    {
        mods = 0x4000; vk = 0;
        foreach (string raw in combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToUpperInvariant())
            {
                case "CTRL": mods |= 0x2; break;
                case "ALT": mods |= 0x1; break;
                case "SHIFT": mods |= 0x4; break;
                case "WIN": mods |= 0x8; break;
                case var key when key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]): vk = key[0]; break;
                case var key when key.Length is 2 or 3 && key[0] == 'F' && int.TryParse(key[1..], out int f) && f is >= 1 and <= 24: vk = (uint)(0x70 + f - 1); break;
                default: return false;
            }
        }
        return vk != 0 && (mods & 0xB) != 0;
    }
}
