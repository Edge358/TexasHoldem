# Texas Hold'em architecture

[Project overview and build commands](../README.md) | [Player guide](HOW_TO_PLAY.md)

This is a map of the current source. Player-facing behavior belongs in the player guide; possible future features belong in [PROJECT_TODO.md](../PROJECT_TODO.md).

## Projects

| Location | Responsibility |
| --- | --- |
| `TexasHoldem.Logic/` | Deck, hand evaluation, betting, pots and the game loop. |
| `AI/TexasHoldem.AI.SmartPlayer/` | Opponent profiles, heuristic decisions, hand/board classification and advice. |
| `AI/TexasHoldem.AI.DummyPlayer/` | Simple baseline opponent. |
| `TexasHoldem.Presentation/` | Shared UI contracts and human-player/decorator integration. |
| `UI/TexasHoldem.UI.Wpf/` | Windows desktop app, .NET 8 WPF. |
| `UI/TexasHoldem.UI.Maui/` | Android tablet app, .NET 9 MAUI. |
| `UI/TexasHoldem.UI.Console/` | Legacy console entry point. |
| `Tests/TexasHoldem.Logic.Tests/` | Engine and AI unit tests. |
| `Tests/TexasHoldem.Tests.GameSimulations/` | Console simulations between AI players. |

## Game and presentation flow

The UI starts the game on a background task. A human decision waits for a UI response; computer players choose actions through the AI layer. The table adapters update cards, stacks, bets, the pot, turn information and winners.

`TexasHoldemGame` accepts the starting money, small blind and computer stack cap. The apps offer six stakes presets and start stacks at 100 big blinds. Broke players rebuy. Computer stacks above the configured cap are trimmed at the start of a hand; the human stack is exempt.

`HandLogic` runs each hand. End-of-hand context exposes the board, winners, starting and ending stacks, contributions and pot total for the result display. A winner can have won a side pot or a share of a split pot.

The normal UI flow pauses between hands so players can review the result. Fold and Next Hand can release that pause. The user guide explains the special Fold/check/next-hand behavior and the distinction between folding normally and skipping the review pause.

## Windows app

Start with `MainWindow.xaml`, `MainWindow.xaml.cs`, `WpfTableView.cs` and `WpfUiDecorator.cs` in the WPF project.

The table uses a 1280 by 720 logical canvas within a uniformly scaled Viewbox. The AI and Log panels are part of that surface. Window placement and UI preferences are saved when Save on Exit is enabled. This is preference persistence, not game-state restoration.

Windows exposes mouse-hover card peeking, opponent-style context menus and keyboard shortcuts. These controls are not interchangeable with the Android settings sheet.

## Android app

Start with `MainPage.xaml`, `MainPage.xaml.cs` and `MauiTableView.cs` in the MAUI project. The Android UI adapts the shared table to touch controls and landscape tablet dimensions.

The gear opens an in-table settings sheet. Card visibility has Always, Showdown and All End modes. Stakes can change before a game or between hands; Back to Game applies a changed stakes selection by ending the old session. The user starts the new session explicitly.

Android preferences retain stakes, card visibility and four optional sizing buttons. They do not retain the current deal or chip stacks. The Log control exists in the layout but is hidden from the released tablet interface.

Human input handling includes a short guard against rapid repeated taps. When completing a decision, pending request fields are cleared before releasing the game thread. Preserve that order: releasing the thread first can allow a new request to be overwritten. Next Hand also distinguishes a live hand from the transition into the between-hand pause.

## AI and advice

`SmartPlayer` builds decision features and uses opponent profiles and the heuristic decision model to choose actions. `RecommendationEngine` separately builds the human advice panel and the end-of-hand summary. The estimate and suggested mix are heuristic, not an exact poker solver.

See [AI advice reference](aiResponse.md) for the display contract. Exact tuning values live in the source, particularly `PlayerProfile.cs`, `HeuristicDecisionModel.cs`, `HandBoardClassifier.cs` and `RecommendationEngine.cs`; duplicating their numeric tables in documentation makes them easy to misread after a change.

## Validation

Use the build and test entry points in the project README. For a UI change, also check the actual target app. A Windows build does not validate Android layout or touch input, and engine tests alone do not validate either UI.

For input changes, exercise the transition from one human decision to the next, folding through a hand, the between-hand pause, rapid taps on Android, and settings changes between hands. For display changes, check cards, turn and winner indications, the AI panel and the settings controls on the target screen.
