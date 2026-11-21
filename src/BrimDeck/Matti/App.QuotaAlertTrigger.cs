using System.Windows.Threading;
using BrimDeck.Core;

namespace BrimDeck;

// Dev helper: trigger-alert.cmd opens the expanded warning panel for a random connected device.
public partial class App
{
    private const string QuotaAlertEventPrefix = "Local\\BrimDeck.QuotaAlert.";
    private EventWaitHandle? _quotaAlertEvent;
    private RegisteredWaitHandle? _quotaAlertListener;
    private static readonly Random AlertPick = new();

    internal static string QuotaAlertEventName => QuotaAlertEventPrefix + Environment.UserName;

    private void StartQuotaAlertTrigger()
    {
        try
        {
            _quotaAlertEvent = new EventWaitHandle(false, EventResetMode.AutoReset, QuotaAlertEventName);
            _quotaAlertListener = ThreadPool.RegisterWaitForSingleObject(
                _quotaAlertEvent, (_, _) => Dispatcher.BeginInvoke(ShowDemoDeviceAlert, DispatcherPriority.Normal),
                null, Timeout.Infinite, false);
            Exit += (_, _) => StopQuotaAlertTrigger();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private void StopQuotaAlertTrigger()
    {
        _quotaAlertListener?.Unregister(null);
        _quotaAlertListener = null;
        _quotaAlertEvent?.Dispose();
        _quotaAlertEvent = null;
    }

    private void ShowDemoDeviceAlert()
    {
        if (Exiting || Deck is null) return;
        SetUserHidden(false);
        WatchDevicesForSettings(true);
        Devices?.Refresh();

        var choices = Settings.DeviceChoices;
        var devices = (Devices?.All ?? [])
            .Where(device => device.Lowest is int)
            .Select(device => DeviceLists.Find(choices, device.Key)?.Label is { Length: > 0 } label ? device with { Name = label } : device)
            .ToList();
        if (devices.Count == 0) return;

        var device = devices[AlertPick.Next(devices.Count)];
        int percent = device.Lowest!.Value;
        string warning = Core.Devices.ChargeReady(percent, device.Charging, Settings.DeviceChargeAlertPercent)
            ? Loc.T("电量充足", "Charged enough")
            : Core.Devices.Level(percent, device.Charging, Settings.DeviceWarningPercent, Settings.DeviceLowPercent) switch
            {
                MemoryLevel.Critical => Loc.T("电量低", "Battery low"),
                MemoryLevel.Elevated => Loc.T("电量偏低", "Getting low"),
                _ => Loc.T("电量偏低", "Getting low"),
            };
        foreach (var deck in Decks)
            deck.ShowDeviceAlert(device, warning);
    }
}
