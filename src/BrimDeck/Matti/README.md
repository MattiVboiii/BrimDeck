# Matti fork code

Put fork-specific features in this folder as new `.cs` files. They are compiled automatically with the main app (`Sdk` default globs).

Keep changes outside this folder as small as possible so merges from [MoFeng2223/BrimDeck](https://github.com/MoFeng2223/BrimDeck) `main` stay easy. Prefer settings, scripts, or new types here over editing upstream hot spots (`UsageService.cs`, `MainWindow.*`, `Core/Settings.cs`, and similar).

## Current files

| File                        | Role                                                                     |
| --------------------------- | ------------------------------------------------------------------------ |
| `App.ClickThroughHold.cs`   | Hold-to-pass-through; bare Esc/CapsLock watched without stealing the key |
| `App.CursorAgent.cs`        | Wires the Cursor Agent watcher at app start                              |
| `CursorAgentWatcher.cs`     | Watches Cursor Agent activity for the compact indicator                  |
| `MainWindow.CursorAgent.cs` | Compact-row UI for Cursor Agent status                                   |
| `UsageTrendGraph.cs`        | Model usage trend chart drawn in model details                           |

Upstream `main` is mirrored on this fork’s `main` branch and merged into `matti` when it moves (currently synced through upstream v1.3.2).
