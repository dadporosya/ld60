using System;
using UnityEngine;

/// <summary>
/// One switch for every debug hotkey in the game (level keys, card choice, visual novel dialogue keys,
/// debug transitions, achievement test keys, perf HUD, pencil recording, ...). A component's own debug
/// toggle still decides whether IT listens; this switch turns them all off at once.
///
/// <para>Editor: toggled from <b>Tools &gt; Debug Hotkeys</b> (remembered per machine). Builds: on in
/// Development Builds, off in release builds. Call <see cref="Allowed"/> before reading a debug key.</para>
/// </summary>
public static class DebugHotkeys
{
    private static bool? enabled;

    /// <summary>Fires with the new value whenever <see cref="Enabled"/> changes.</summary>
    public static event Action<bool> Changed;

    public static bool Enabled
    {
        get => enabled ?? (Application.isEditor || Debug.isDebugBuild);
        set
        {
            if (enabled == value) return;
            enabled = value;
            h.Out($"Debug hotkeys {(value ? "enabled" : "disabled")}.");
            Changed?.Invoke(value);
        }
    }

    /// <summary>True when debug hotkeys are on globally and <paramref name="componentToggle"/> (the component's own debug switch) is on.</summary>
    public static bool Allowed(bool componentToggle = true) => componentToggle && Enabled;
}
