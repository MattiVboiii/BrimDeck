using System.Windows.Input;
using BrimDeck.Native;

namespace BrimDeck;

// Momentary mouse pass-through while the ClickThrough hotkey is held (DeckSettings.ClickThroughHold). Does not flip the sticky
// ClickThrough setting; tray and the Compatibility switch still do that.
public partial class App
{
    private PromptTimer? _holdPassThroughPoll;
    private int _holdPassThroughVk;

    // True while the hold hotkey is down. MainWindow.ClickThrough ORs this into its pass-through math.
    internal bool HoldPassThrough { get; private set; }

    private void ClickThroughFromHotkey()
    {
        if (Settings.ClickThroughHold) BeginHoldPassThrough();
        else ToggleClickThrough();
    }

    private void BeginHoldPassThrough()
    {
        if (HoldPassThrough) return;
        if (!HotkeyGesture.TryParse(Settings.ClickThroughHotkey, out _, out var key, bareKeyOk: true))
        {
            ToggleClickThrough();
            return;
        }
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0)
        {
            ToggleClickThrough();
            return;
        }
        HoldPassThrough = true;
        _holdPassThroughVk = vk;
        ApplyHoldPassThrough();
        _holdPassThroughPoll ??= new PromptTimer(Dispatcher, PollHoldPassThrough) { Interval = TimeSpan.FromMilliseconds(30) };
        _holdPassThroughPoll.Start();
    }

    private void PollHoldPassThrough()
    {
        if (PasteInput.KeyDown(_holdPassThroughVk)) return;
        EndHoldPassThrough();
    }

    private void EndHoldPassThrough()
    {
        _holdPassThroughPoll?.Stop();
        if (!HoldPassThrough) return;
        HoldPassThrough = false;
        ApplyHoldPassThrough();
    }

    private void ApplyHoldPassThrough()
    {
        foreach (var deck in Decks.ToList())
            if (deck is not null) deck.RefreshHoldPassThrough();
    }
}
