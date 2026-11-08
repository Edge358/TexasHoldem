namespace TexasHoldem.Presentation;

/// <summary>
/// What the table reveals when a hand ends and the cards are otherwise masked.
/// The view keeps the per-seat facts (the hand ended; the seat was at showdown)
/// and applies the mode when it draws, so a change between hands shows at once.
/// </summary>
public enum EndOfHandReveal
{
    /// <summary>Only the seats whose cards were turned over at showdown.</summary>
    Showdown = 0,

    /// <summary>Every seat, folders included.</summary>
    Everyone = 1,
}
