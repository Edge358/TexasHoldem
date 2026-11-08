# Restore Game (Between-Hands Snapshot)

## Goal
Add an optional "Continue Last Game" flow that restores a lightweight snapshot taken only between hands. No mid-hand resume, no hand history replay. If the app closes mid-hand, ignore that state and resume from the last completed-hand snapshot (or offer only "Start New Game").

## Scope (between-hands only)
Persist enough data to restart the game in the same state as the last completed hand:
- **Per-seat stack** values
- **Per-seat archetype** (SmartPlayer profile) + human seat identity
- **Game size preset** (blinds) selection
- **Dealer button position** (seat index)
- **Hands played count** (displayed hand counter)
- **Human buy-ins count**
- **UI toggles** (already persisted): action log, AI decision log, AI recommendations, raise controls, card visibility mode

No need to restore:
- Board/cards
- Pot, bets, or action history
- Action log contents (start fresh)

## UX Behavior
- Add a new option to start flow: **"Continue Last Game"** (available only if a valid snapshot exists).
- If "Continue" is used, start a new hand using the saved stacks/archetypes/blinds and restore dealer position + hand counter + buy-ins.
- If the snapshot is missing/invalid or app was closed mid-hand, hide/disable "Continue."
- "Save on Exit" remains the master switch: only save/restore snapshots when enabled.

## Persistence Strategy
Use user-scoped settings (same as window placement and UI preferences). New settings to add:
- `LastGameSnapshotIsValid` (bool)
- `LastGameGameSizeIndex` (int)
- `LastGameDealerSeat` (int)
- `LastGameHandsPlayed` (int)
- `LastGameHumanBuyIns` (int)
- `LastGameSeatStacks` (string or JSON-encoded list of 8 ints)
- `LastGameSeatArchetypes` (string or JSON-encoded list of 8 archetype enums)
- `LastGameHumanSeatIndex` (int) (currently seat 4, but keep explicit)

Implementation note: a simple CSV or JSON string in settings is fine (no external file).

## When to Save
Only when:
- **Save on Exit** is checked, and
- Game is **between hands** (hand pause state), and
- No pending player action.

If closed mid-hand, do not overwrite the last valid snapshot.

## When to Restore
On app startup:
- If Save on Exit is checked **and** `LastGameSnapshotIsValid == true`, enable "Continue Last Game."
- If user chooses "Continue", load snapshot and start a new hand with those values.

## Engine/UI Touch Points
Likely files:
- `UI/TexasHoldem.UI.Wpf/MainWindow.xaml`
  - Add a "Continue Last Game" button or a dropdown menu next to Start.
- `UI/TexasHoldem.UI.Wpf/MainWindow.xaml.cs`
  - Capture snapshot on close (between hands only).
  - Restore snapshot on start if user chooses "Continue."
  - Apply saved dealer seat and hand counter.
  - Restore buy-ins counter and per-seat stacks.
  - Ensure archetype labels match restored profiles.
- `UI/TexasHoldem.UI.Wpf/WpfUiDecorator.cs`
  - Expose/accept a starting stack per seat (if not already passed via game start).
  - Ensure buy-ins counter can be set on resume.
- `TexasHoldem.Logic/GameMechanics/TexasHoldemGame.cs`
  - Confirm ability to start with custom stacks per player (may need a new constructor or StartGame context override).

## Suggested Implementation Approach
1) **Define snapshot model** (small POCO) in WPF UI layer.
2) **Serialize** snapshot to settings on exit if eligible.
3) **Restore** snapshot on demand (Continue flow).
4) **Start new game** using:
   - selected blinds from snapshot
   - initial stacks from snapshot (per player)
   - restored archetypes (SmartPlayer profiles)
5) **Apply UI state**:
   - dealer position
   - hand counter
   - buy-ins
   - clear logs

## Edge Cases
- If snapshot seat count doesn’t match current seats (should be 8), ignore snapshot.
- Clamp or validate stacks to >0 and reasonable max to avoid crashes.
- If archetype enum doesn’t parse, fall back to default (Reg or previous).
- If Save on Exit is unchecked, do not restore or save snapshot.

## Open Questions (resolve before coding)
- Button placement: replace Start with a split button or add a second button?
- Should "Continue" be disabled if game size preset list changes?
- Do we need to persist seat renames if any are added later?

