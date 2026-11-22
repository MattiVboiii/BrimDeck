using BrimDeck.Core;

namespace BrimDeck;

public partial class MainWindow
{
    private readonly DeviceLevels _deviceLevels = new();
    private readonly Queue<DeviceLevels.Rise> _pendingDeviceAlerts = new();

    // A charging device that rises to the charge-notice threshold briefly shows the same notice as a quota rise.
    partial void DetectDeviceAlerts()
    {
        if (!Settings.DeviceChargeAlerts)
        {
            _pendingDeviceAlerts.Clear();
            return;
        }
        var devices = DeviceLists.Expanded(_app.Devices?.Showing ?? [], Settings.DeviceChoices);
        var rises = _deviceLevels.Update(devices, Settings.DeviceChargeAlertPercent);
        if (IsSuppressed) return;
        foreach (var rise in rises) _pendingDeviceAlerts.Enqueue(rise);
        TryShowPendingDeviceAlert();
    }

    internal void TryShowPendingDeviceAlert()
    {
        if (!Settings.DeviceChargeAlerts || _alert is not null || IsSuppressed) return;
        if (_pendingDeviceAlerts.Count == 0) return;
        var rise = _pendingDeviceAlerts.Dequeue();
        ShowDeviceAlert(rise.Device, rise.Warning);
    }
}
