# Matti fork code

Put fork-specific features in this folder as new `.cs` files. They are compiled automatically with the main app (`Sdk` default globs).

Keep changes outside this folder as small as possible so merges from [MoFeng2223/BrimDeck](https://github.com/MoFeng2223/BrimDeck) `main` stay easy. Prefer settings, scripts, or new types here over editing upstream hot spots (`UsageService.cs`, `MainWindow.*`, `Core/Settings.cs`, and similar).

Do not add placeholder features in this PR; use this folder when you actually implement something.