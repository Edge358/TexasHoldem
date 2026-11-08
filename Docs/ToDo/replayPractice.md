# Replay / Practice Mode (Sandbox Hands)

## Goal
Add a **Practice Mode** that lets the human play sandbox hands without affecting the live cash game.
Practice hands can:
- Use **chosen hole cards** for the human.
- Deal the rest of the deck randomly.
- Allow changing hole cards and AI archetypes **between hands**.

No impact to the live game state (stacks, history, buy-ins).

## Core Behavior
- Human is always Seat 4 (same as main game).
- Default AI lineup matches current archetypes; user can change archetypes between hands via the existing seat menu.
- Practice mode runs its own hand loop with the existing “pause between hands” flow.
- The user can choose:
  - **Same two cards every hand**, or
  - **Pick new cards between hands**.

## UI/UX Options
Entry points:
- A “Practice” button near Start/Next Hand.
- Or a small dropdown beside Start: Start New / Continue / Practice.

Practice controls (simple version):
- Two dropdowns for card rank + suit (or a card picker).
- A checkbox: “Use same cards each hand.”
- Optional: stack size + blinds selection (default to current game size).

## Engine Requirements (Key)
We need a way to inject the human’s two cards **before the hand is dealt**:
- Option A: Add a fixed/decorated deck (override shuffle/deal for first two cards).
- Option B: Modify dealing logic to accept forced hole cards for a specific seat.
- Option C: Provide a deterministic RNG seed + deck order (more complex).

Option B is the simplest for practice-only mode.

## Persistence (Optional)
If Save on Exit is enabled:
- Persist last selected practice hole cards.
- Persist “use same cards” toggle.

## Isolation From Live Game
Practice should be a separate game instance:
- Does not touch the live stacks.
- Does not write to the live action log (or writes to a separate practice log).
- Can reuse the same UI, but should display “Practice Mode” in the status line.

## Replay Tie-In (Later)
If we later want “Replay Last Hand as Practice”:
- Capture last hand’s hole cards + board order.
- Feed them into Practice Mode as a fixed deck (same logic as forced hole cards).

## Files Likely Involved
- `UI/TexasHoldem.UI.Wpf/MainWindow.xaml`
  - Add practice entry button + card picker UI.
- `UI/TexasHoldem.UI.Wpf/MainWindow.xaml.cs`
  - Launch and manage practice session.
  - Use existing hand pause flow to allow card/AI changes between hands.
- `TexasHoldem.Logic/GameMechanics/HandLogic.cs` or dealing logic
  - Inject forced hole cards for a specific seat.
- `TexasHoldem.Logic/Cards/Deck.cs`
  - Optionally wrap or override deck order.

## Edge Cases
- Ensure chosen cards are removed from the deck before dealing to others.
- Prevent selecting duplicates or invalid combinations.
- If forcing cards fails, fall back to random deal and warn the user.

## Open Questions
- Should practice stacks always be 100bb, or configurable?
- Should practice include “End Hand” logging, or remain minimal?
- Should the AI decision log be enabled/disabled separately for practice?

