namespace BrimDeck.Core;

// Per-device charge-notice state: a rise into "charging at or above the threshold" after the first reading briefly shows a notice
// (MainWindow.DetectDeviceAlerts). The first look only seeds, so a device already charged when BrimDeck starts does not alert.
public sealed class DeviceLevels
{
    public sealed record Rise(DeviceBattery Device, string Warning);

    private readonly Dictionary<string, bool> _ready = [];
    private bool _seeded;

    // The first device that crossed into charge-ready since the last call, or null.
    public Rise? Update(IReadOnlyList<DeviceBattery> devices, int threshold)
    {
        Rise? risen = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var device in devices)
        {
            if (device.Lowest is not int) continue;
            string key = device.Key;
            seen.Add(key);
            bool ready = Devices.ChargeReady(device.Lowest, device.Charging, threshold);
            bool was = _ready.TryGetValue(key, out var previous) && previous;
            _ready[key] = ready;
            if (_seeded && ready && !was && risen is null)
                risen = new Rise(device, Loc.T("电量充足", "Charged enough"));
        }
        foreach (var key in _ready.Keys.Where(key => !seen.Contains(key)).ToList())
            _ready.Remove(key);
        if (seen.Count > 0) _seeded = true;
        return risen;
    }
}
