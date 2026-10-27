namespace BetterBluetooth.Core.Models;

/// <summary>
/// Immutable snapshot of one Bluetooth Classic device.
/// Updated via `with` expressions when watcher or engine events occur.
/// </summary>
public sealed record BluetoothDeviceInfo
{
    public required string Id          { get; init; }
    public required string Name        { get; init; }
    public required string Mac         { get; init; }
    public bool   IsPaired             { get; init; }
    public bool   IsConnected          { get; init; }
    public int?   Rssi                 { get; init; }
    public int    CodMajor             { get; init; }
    public string Phase                { get; init; } = "known";
    public bool   AutoForget           { get; set; }

    public string CodLabel => CodMajor switch
    {
        4 => "Audio",
        5 => "Peripheral",
        _ => CodMajor == 0 ? "?" : $"CoD {CodMajor}"
    };

    public string StateLabel =>
        IsConnected ? "Connected" :
        IsPaired    ? "Paired · Not connected" :
                      "Not paired";

    public string MacFormatted
    {
        get
        {
            var m = Mac.Replace("-", ":").ToUpperInvariant();
            return m.Length == 17 ? m : Mac;
        }
    }
}
