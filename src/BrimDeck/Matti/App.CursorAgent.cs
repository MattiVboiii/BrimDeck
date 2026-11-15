using BrimDeck.Core;
using BrimDeck.Matti;

namespace BrimDeck;

public partial class App
{
    internal CursorAgentWatcher CursorAgent { get; private set; } = null!;

    private void StartCursorAgentWatcher()
    {
        CursorAgent = new CursorAgentWatcher(Dispatcher, () => Settings);
        CursorAgent.Apply();
    }

    private void ApplyCursorAgentWatcher() => CursorAgent?.Apply();
}
