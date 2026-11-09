# AI advice reference

[Player guide](HOW_TO_PLAY.md#read-the-ai-panel) | [Architecture](TexasHoldem_BluePrint.md)

`AI/TexasHoldem.AI.SmartPlayer/Decision/RecommendationEngine.cs` owns the advice text used by both apps. It provides a neutral heuristic recommendation for the human player. Opening opponents' cards does not turn it into an exact calculation using those cards.

## During a decision

`BuildRecommendation` emits these lines:

| Line | Content |
| --- | --- |
| 1 | Street, human hole cards and position. |
| 2 | Suggested action mix. R = raise, C = call, F = fold, B = bet, X = check. Numbers represent percentages. |
| 3-4 | Short hand and board rationale. |
| 5 | Pot odds and estimated equity. |
| 6 | `Action:` followed by the suggested play. |

The tablet screenshot in the player guide gives a real example: `B30 X70`, `Medium`, `Dry board`, `Odds - | Eq 56%`, `Action: Check`.

The engine uses the human cards, shared cards, position and betting context. It classifies preflop strength or the made hand, draws and board texture. Pot odds use the call amount divided by the current pot plus that call. No required call is displayed as a dash. Equity is an estimate based on heuristic bands; it is not an exhaustive comparison against every possible opponent hand.

The displayed action mix describes the recommendation. It does not automatically choose or submit the human player's action. Numeric tuning and adjustments belong in the implementation rather than a second table of purported current values here.

## At hand end

`BuildEndOfHandSummary` replaces the recommendation with:

- WINNER, PUSH or LOSER, followed by the human cards and position.
- The shared board.
- Net chip change, pot total and invested chips (`Inv`).
- Hand detail and board texture.
- The human player's last action.

The winner indication follows the engine's winner list, including side pots and split pots. `Inv` records the hand contribution; it is not the same as the final loss.

## Source ownership

`RecommendationEngine` owns text and recommendations. `HandBoardClassifier` owns the made-hand, draw and texture classification. The UI adapters supply the context and display the result. Opponent decisions are implemented separately in the SmartPlayer decision model, so changes to the advice rubric do not by themselves change every opponent's strategy.
