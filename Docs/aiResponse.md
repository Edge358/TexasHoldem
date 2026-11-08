## Recommendation Display Spec (Current)

### Output Schema

Current condensed multi-line format (matches UI panel):
- Line 1: `Street | HoleCards | Position`
- Line 2: `Mix` (R/C/F or B/X)
- Line 3: Short rationale (hand descriptor)
- Line 4: Short rationale (texture/board)
- Line 5: `Odds xx% | Eq yy%`
- Line 6: `Action: ...`

### Preflop Recommendation Tiers (Baseline)

Suggested tier-to-mix mapping (baseline, can be tuned):
- Premium: Raise 70 / Call 25 / Fold 5
- Strong: Raise 45 / Call 45 / Fold 10
- Medium: Raise 20 / Call 50 / Fold 30
- Weak: Raise 5 / Call 25 / Fold 70

### Postflop Recommendation Inputs

For a simple heuristic recommendation, use:
- `MadeHandClass` (Monster/Strong/Medium/Weak/Air)
- `DrawClass` (Combo/FD/OESD/Gutshot/TwoOver/None)
- `BoardTexture` (Dry/SemiWet/Wet)
- `Pot odds` (ToCall / (Pot + ToCall))
- `SPR` (HeroStack / Pot)

### Recommendation Engine Location

- Implemented in `AI/TexasHoldem.AI.SmartPlayer/Decision/RecommendationEngine.cs`.
- Neutral baseline (not archetype-specific).

### Advisor Personality

- Default recommendation should be neutral/standard (not LAG/Nit-specific).
- Optional toggle: show advice for a selected archetype (future).

### Default "Reg" Advice (Concept)

- Use a single neutral "Reg" baseline as the default advice for the human player.
- Optional comparison view: show how Rock/LAG/Maniac would differ (future toggle).
- Implementation should be small: reuse existing signals (MadeHand/Draw/Texture + pot odds + SPR),
  then map to a concise Action/Mix/Why line.

### Minimal "Reg" Recommendation Rubric (Draft)

Inputs: MadeHandClass, DrawClass, BoardTexture, PotOdds (ToCall / (Pot + ToCall)), SPR.

Preflop (use PreflopTier mapping already listed):
- If no bet to you: use tier mix; default Action = highest mix.
- If facing a bet: tighten one tier (e.g., Medium -> Weak) unless PotOdds <= 0.25.

Postflop:
- Monster: prefer Raise/Bet; allow Call only if board is Wet and SPR > 6.
- Strong: Raise/Bet when PotOdds <= 0.30; otherwise Call.
- Medium: Call when PotOdds <= 0.20; otherwise Fold. If ToCall == 0 and board Dry/SemiWet, allow small Bet.
- Weak/Air:
  * With Combo/FD/OESD: Call if PotOdds <= 0.20 (or Bet small if ToCall == 0 on Dry/SemiWet).
  * With no draw: check/fold; allow rare bluff on Dry boards only if heads-up.

Texture adjustments:
- Dry: allow more small bets and lighter calls.
- Wet: downgrade one step (raise -> call, call -> fold) unless Monster.

SPR adjustments:
- SPR <= 2: increase aggression one step (call -> raise, fold -> call) with Strong+ or Combo draws.
- SPR >= 8: reduce aggression one step with Medium or weaker.

Output guidance:
- Action: pick the highest-weight action after adjustments.
- Mix: use simple 70/20/10 style splits unless a rule forces a single action.

### Condensed Window Layout (Current)

Per street (current hand only; clears on next hand):

Pre | Ac Jc | BTN
R60 C30 F10
Strong suited Broadway +
Good open on button
Odds - | Eq -
Action: Raise to 6

Flop | Ac Jc | Ad Kc Qc
B60 C30 X10
Top pair + nut FD +
Gutshot on wet board.
Odds - | Eq 55%
Action: Bet 0.33 pot

Turn | Ac Jc | Ad Kc Qc 3s
R20 C70 F10
Top pair + nut FD +
Gutshot on wet board.
Odds 22% | Eq 48%
Action: Call

River | Ac Jc | Ad Kc Qc 3s Ah
R55 C35 F10
Trips top kicker
Value vs worse Ax / two pair
Odds 25% | Eq 70%
Action: Raise to 60

### End-of-Hand Summary (Current)

WINNER | Qc 10h | SB
Board 8s Qs 9c As
Net +94 | Pot 180 | Inv 198
2nd Pair
Medium on wet board
Last Action: Raise 112
