using BrimDeck.Core;

namespace BrimDeck;

public partial class MainWindow
{
    private readonly DeviceLevels _deviceLevels = new();

    // A charging device that rises to the charge-notice threshold briefly shows the same notice as a quota rise.
    partial void DetectDeviceAlerts()
    {
        if (!Settings.DeviceChargeAlerts) return;
        var devices = DeviceLists.Expanded(_app.Devices?.Showing ?? [], Settings.DeviceChoices);
        var rise = _deviceLevels.Update(devices, Settings.DeviceChargeAlertPercent);
        if (rise is null || IsSuppressed) return;
        ShowDeviceAlert(rise.Device, rise.Warning);
    }

    internal void ShowDeviceAlert(DeviceBattery device, string warning)
    {
        var theme = Settings.ConfiguredApps.FirstOrDefault() ?? Settings.Apps.FirstOrDefault() ?? new AppEntry { DisplayName = device.Name };
        double used = device.Lowest ?? 0;
        ShowAlert(new CompactAlertInfo(theme, device.Name, warning + " ", used, null, warning, device));
    }
}
