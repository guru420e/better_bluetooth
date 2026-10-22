namespace BluetoothRectifier;

/// <summary>
/// Immutable snapshot of one Bluetooth Classic device.
/// Updated via `with` expression when the watcher fires an Updated event.
/// AutoForget is the only mutable field — it is set by the user, not the OS.
///
/// CodMajor reference: 4 = Audio, 5 = Peripheral/HID, 0 = unknown.
/// </summary>
public sealed record BluetoothDeviceInfo
{
    /// <summary>WinRT DeviceInformation.Id — opaque but stable across restarts.</summary>
    public required string Id          { get; init; }
    public required string Name        { get; init; }

    /// <summary>
    /// Raw MAC address string from System.Devices.Aep.DeviceAddress.
    /// Use MacFormatted for display (normalises to XX:XX:XX:XX:XX:XX upper-case).
    /// </summary>
    public required string Mac         { get; init; }

    public bool   IsPaired    { get; init; }
    public bool   IsConnected { get; init; }

    /// <summary>RSSI in dBm. Null if the OS didn't report it.</summary>
    public int?   Rssi        { get; init; }

    /// <summary>Bluetooth Class of Device — major class only.</summary>
    public int    CodMajor    { get; init; }

    /// <summary>
    /// "known"  = device appeared during the initial enumeration (before EnumerationCompleted).
    ///            These are remembered/paired devices even if currently off.
    /// "live"   = device appeared after EnumerationCompleted; it is actively advertising.
    /// </summary>
    public string Phase       { get; init; } = "known";

    /// <summary>Set by the user via the UI toggle. NOT persisted here — see AutoForgetStore.</summary>
    public bool AutoForget { get; set; }

    // ── Derived display helpers ────────────────────────────────────────

    public string CodLabel => CodMajor switch
    {
        4 => "Audio",
        5 => "Peripheral",
        _ => CodMajor == 0 ? "?" : $"CoD {CodMajor}"
    };

    public string StateLabel =>
        IsConnected  ? "Connected" :
        IsPaired     ? "Paired · Not connected" :
                       "Not paired";

    /// <summary>Normalises the OS-supplied MAC to upper-case XX:XX:XX:XX:XX:XX.</summary>
    public string MacFormatted
    {
        get
        {
            var m = Mac.Replace("-", ":").ToUpperInvariant();
            return m.Length == 17 ? m : Mac;
        }
    }
}
