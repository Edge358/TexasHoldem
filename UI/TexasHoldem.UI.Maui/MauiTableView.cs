namespace TexasHoldem.UI.Maui;

using System;
using System.Collections.Generic;
using System.Text;

using TexasHoldem.Presentation;

/// <summary>
/// MAUI implementation of <see cref="ITableView"/>. Mirrors WpfTableView.
///
/// Threading note. WpfTableView marshals with Dispatcher.Invoke, which blocks the
/// calling thread, so a cache written inside its OnUi callback is visible to the
/// engine thread the moment the call returns. MainThread.BeginInvokeOnMainThread
/// does not block, so every cache the engine reads back through a getter is written
/// synchronously here, before the control update is queued. Only control mutation
/// happens on the UI thread.
/// </summary>
public sealed class MauiTableView : ITableView
{
    /// <summary>
    /// What a masked seat that is still in the hand shows: two card backs, each
    /// a pair of full blocks, in the colour the board's half-white renders to
    /// on the felt, so they read as cards and not as a button colour (a
    /// card-back red was tried and was too close to Fold; navy was too close to
    /// Call; the board's grey solid blended in). A masked seat that has folded
    /// shows nothing, so a glance says who is still in. Session 8 had every
    /// masked seat blank, and the user could not tell live seats from folded
    /// ones; session 6 had "?? ??" on all eight, which read as clutter.
    /// Decided 2026-09-11 (session 9). WPF keeps its own "?? ??".
    ///
    /// The colour is opaque on purpose. It began as the board's own #77FFFFFF,
    /// and on the tablet the two glyphs of each back overlap by a pixel, so
    /// the half-transparency doubled up into a bright seam down the middle of
    /// every back. #88AAA1 is what #77FFFFFF measured as over the felt, so the
    /// look the user chose is kept and the seam is gone.
    /// </summary>
    private const string MaskedCards = "██ ██";
    private static readonly Color MaskedCardsColor = Color.FromArgb("#FF88AAA1");

    /// <summary>
    /// 40 design units, about 7 mm on the tablet; the 26 it started at was
    /// about 4 mm and the user could not find it. Session 9.
    /// </summary>
    private const double DealerButtonSize = 40;

    /// <summary>
    /// U+FE0E, VARIATION SELECTOR-15. Android resolves the four card suits to the
    /// colour emoji font by default, which draws them full width and overflows the
    /// 176x44 card slot. This selector asks for the text glyph instead, which is what
    /// WPF draws and what the tuned slot sizes assume.
    /// </summary>
    private const char TextPresentationSelector = '\uFE0E';

    private static readonly Color RedSuitColor = Color.FromArgb("#FF640A0A");
    private static readonly Color BlackSuitColor = Color.FromArgb("#FF121212");
    private static readonly Color DefaultTextColor = Color.FromArgb("#FFF2F2F2");
    private static readonly Color DefaultSeatBorderColor = Color.FromArgb("#664E4E4E");
    private static readonly Color DefaultCardBorderColor = Color.FromArgb("#55282828");
    private static readonly Color HumanSeatBorderColor = Color.FromArgb("#FFEBEBEB");
    private static readonly Color WinnerSeatBorderColor = Color.FromArgb("#FFE6C85C");

    private readonly Label[] seatNames;
    private readonly Label[] seatStacks;
    private readonly Label[] seatCards;
    private readonly Border[] seatInfoBorders;
    private readonly Border[] seatCardBorders;
    private readonly Label[] seatBets;
    private readonly Label[] seatStatuses;
    private readonly bool[] seatIsHuman;
    private readonly bool[] seatIsTurnActive;
    private readonly bool[] seatIsWinner;
    private readonly bool[] seatIsHovered;
    private readonly bool[] seatIsFolded;
    /// <summary>
    /// Null while the seat's hand is in progress. Once the hand ends: true if
    /// the seat's cards were turned over at showdown, false if it folded or the
    /// hand ended on folds. One value, so "ended" and "showed" cannot drift.
    /// </summary>
    private readonly bool?[] seatEndedAtShowdown;
    private readonly string[] seatCardCache;
    private readonly Label communityCards;
    private readonly Label pot;
    private readonly Label actionLog;
    private readonly ScrollView actionLogScroll;
    private readonly Label turnInfo;
    private readonly View dealerButton;
    private readonly Label handCounter;
    private readonly Label buyInCounter;
    private readonly Label aiRecommendation;
    private readonly Dictionary<string, int> seatIndexByPlayerName;
    private readonly Dictionary<int, string> playerNameBySeatIndex;
    private readonly string[] seatNameCache;
    private readonly object seatNameLock = new object();
    private readonly int[] seatContributionTotals;
    private readonly object seatContributionLock = new object();
    private int dealerSeatIndex;
    private CardVisibilityMode cardVisibilityMode;
    private EndOfHandReveal endOfHandReveal;

    public MauiTableView(
        Label[] seatNames,
        Label[] seatStacks,
        Label[] seatCards,
        Border[] seatInfoBorders,
        Border[] seatCardBorders,
        Label[] seatBets,
        Label[] seatStatuses,
        Label communityCards,
        Label pot,
        Label actionLog,
        ScrollView actionLogScroll,
        Label turnInfo,
        View dealerButton,
        Label handCounter,
        Label buyInCounter,
        Label aiRecommendation)
    {
        this.seatNames = seatNames;
        this.seatStacks = seatStacks;
        this.seatCards = seatCards;
        this.seatInfoBorders = seatInfoBorders;
        this.seatCardBorders = seatCardBorders;
        this.seatBets = seatBets;
        this.seatStatuses = seatStatuses;
        this.seatIsHuman = new bool[seatNames.Length];
        this.seatIsTurnActive = new bool[seatNames.Length];
        this.seatIsWinner = new bool[seatNames.Length];
        this.seatIsHovered = new bool[seatNames.Length];
        this.seatIsFolded = new bool[seatNames.Length];
        this.seatEndedAtShowdown = new bool?[seatNames.Length];
        this.seatCardCache = new string[seatNames.Length];
        this.communityCards = communityCards;
        this.pot = pot;
        this.actionLog = actionLog;
        this.actionLogScroll = actionLogScroll;
        this.actionLog.SizeChanged += this.ActionLog_SizeChanged;
        this.turnInfo = turnInfo;
        this.dealerButton = dealerButton;
        this.handCounter = handCounter;
        this.buyInCounter = buyInCounter;
        this.aiRecommendation = aiRecommendation;
        this.dealerSeatIndex = -1;
        this.seatIndexByPlayerName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        this.playerNameBySeatIndex = new Dictionary<int, string>();
        this.seatNameCache = new string[seatNames.Length];
        this.seatContributionTotals = new int[seatNames.Length];
        this.cardVisibilityMode = CardVisibilityMode.ShowAll;
        this.endOfHandReveal = EndOfHandReveal.Showdown;
    }

    public void SetSeatName(int seatIndex, string name)
    {
        var index = this.ToIndex(seatIndex);
        lock (this.seatNameLock)
        {
            this.seatNameCache[index] = name ?? string.Empty;
        }

        OnUi(() => this.seatNames[index].Text = name);
    }

    public void RegisterPlayerName(int seatIndex, string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return;
        }

        this.seatIndexByPlayerName[playerName] = seatIndex;
        this.playerNameBySeatIndex[seatIndex] = playerName;
    }

    public string GetSeatName(int seatIndex)
    {
        var index = this.ToIndex(seatIndex);
        lock (this.seatNameLock)
        {
            return this.seatNameCache[index] ?? string.Empty;
        }
    }

    public string GetSeatNameByPlayer(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return string.Empty;
        }

        if (this.seatIndexByPlayerName.TryGetValue(playerName, out var seatIndex))
        {
            return this.GetSeatName(seatIndex);
        }

        return playerName;
    }

    public string GetPlayerNameBySeat(int seatIndex)
    {
        return this.playerNameBySeatIndex.TryGetValue(seatIndex, out var name)
            ? name
            : string.Empty;
    }

    public int GetSeatContributionTotal(int seatIndex)
    {
        var index = this.ToIndex(seatIndex);
        lock (this.seatContributionLock)
        {
            return this.seatContributionTotals[index];
        }
    }

    public void SetSeatStack(int seatIndex, int stack)
    {
        var index = this.ToIndex(seatIndex);
        OnUi(() => this.seatStacks[index].Text = stack.ToString("N0"));
    }

    public void SetSeatCards(int seatIndex, string cards)
    {
        var index = this.ToIndex(seatIndex);
        this.seatCardCache[index] = cards ?? string.Empty;

        // A new deal ends the previous hand's reveal, so the seat starts the
        // hand masked again under SHOWDOWN and ALL END, and it is not folded
        // either: clearing that here rather than waiting for the decorator's
        // SetSeatFolded(false) two calls later means the seat is never drawn
        // blank for a hop with last hand's fold still set. Review, session 9.
        this.seatEndedAtShowdown[index] = null;
        this.seatIsFolded[index] = false;
        OnUi(() => this.ApplySeatCards(index));
    }

    public void SetSeatIsHuman(int seatIndex, bool isHuman)
    {
        var index = this.ToIndex(seatIndex);
        this.seatIsHuman[index] = isHuman;
        OnUi(() =>
        {
            this.ApplySeatInfoBorder(index);
            this.ApplySeatCards(index);
        });
    }

    public void SetSeatTurnActive(int seatIndex, bool isActive)
    {
        var index = this.ToIndex(seatIndex);
        this.seatIsTurnActive[index] = isActive;
        OnUi(() =>
        {
            this.ApplySeatInfoBorder(index);
            this.ApplySeatCardBorder(index);
        });
    }

    public void SetSeatBet(int seatIndex, int total, int lastContribution, string prefix = "")
    {
        var index = this.ToIndex(seatIndex);
        lock (this.seatContributionLock)
        {
            this.seatContributionTotals[index] = total;
        }

        // The plus number is what this action put in; the parentheses is the
        // seat's total in the pot for the hand, and it is always in parentheses
        // so the two cannot be confused. A check with 16 already in reads
        // "X   (16)", not "X   16". Session 9, 2026-09-11.
        var prefixText = string.IsNullOrWhiteSpace(prefix) ? string.Empty : $"{prefix}   ";
        var text = string.Empty;
        if (total > 0 && lastContribution > 0)
        {
            text = $"{prefixText}+{lastContribution} ({total})";
        }
        else if (total > 0)
        {
            text = $"{prefixText}({total})";
        }
        else if (!string.IsNullOrWhiteSpace(prefixText))
        {
            text = prefixText.Trim();
        }

        OnUi(() => this.seatBets[index].Text = text);
    }

    public void SetSeatStatus(int seatIndex, string status)
    {
        var index = this.ToIndex(seatIndex);
        OnUi(() => this.seatStatuses[index].Text = status);
    }

    public void SetSeatFolded(int seatIndex, bool isFolded)
    {
        var index = this.ToIndex(seatIndex);
        if (this.seatIsFolded[index] == isFolded)
        {
            // The decorator calls this after every action of every seat; only a
            // change is worth a redraw of the card label. Review, session 9.
            return;
        }

        this.seatIsFolded[index] = isFolded;
        OnUi(() => this.ApplySeatCards(index));
    }

    public void SetSeatHover(int seatIndex, bool isHovered)
    {
        var index = this.ToIndex(seatIndex);
        this.seatIsHovered[index] = isHovered;
        OnUi(() => this.ApplySeatCards(index));
    }

    public void SetCardVisibility(CardVisibilityMode mode, EndOfHandReveal reveal)
    {
        this.cardVisibilityMode = mode;
        this.endOfHandReveal = reveal;
        OnUi(() =>
        {
            for (var i = 0; i < this.seatCards.Length; i++)
            {
                this.ApplySeatCards(i);
            }
        });
    }

    public void RevealSeatAtHandEnd(int seatIndex, bool wasAtShowdown)
    {
        var index = this.ToIndex(seatIndex);
        this.seatEndedAtShowdown[index] = wasAtShowdown;
        OnUi(() => this.ApplySeatCards(index));
    }

    /// <summary>
    /// Puts the table back to the look it has when the app opens: every seat
    /// at stack 0 with no cards, bet, fold, winner or human border; an empty
    /// board; Pot: 0; no dealer button; the counters at Hands: 0 and Buy-Ins:
    /// 1; no AI line. Called when a game stops, so a stakes change lands on
    /// the launch screen rather than on the last hand, which read as a game
    /// still in progress. Seat names and statuses are the page's to set; the
    /// turn info line is left for the caller's message. Replaces session 9's
    /// HideDealerButton, which hid the button and left the rest. Session 10,
    /// 2026-09-12.
    /// </summary>
    public void ResetToLaunchLook()
    {
        lock (this.seatContributionLock)
        {
            Array.Clear(this.seatContributionTotals, 0, this.seatContributionTotals.Length);
        }

        for (var i = 0; i < this.seatCards.Length; i++)
        {
            this.seatCardCache[i] = string.Empty;
            this.seatEndedAtShowdown[i] = null;
            this.seatIsFolded[i] = false;
            this.seatIsHovered[i] = false;
            this.seatIsHuman[i] = false;
            this.seatIsTurnActive[i] = false;
            this.seatIsWinner[i] = false;
        }

        OnUi(() =>
        {
            for (var i = 0; i < this.seatCards.Length; i++)
            {
                this.seatStacks[i].Text = "0";
                this.seatBets[i].Text = string.Empty;
                this.ApplySeatInfoBorder(i);
                this.ApplySeatCardBorder(i);
                this.ApplySeatCards(i);
            }

            SetCardText(this.communityCards, string.Empty);
            this.pot.Text = "Pot: 0";
            this.handCounter.Text = "Hands: 0";
            this.buyInCounter.Text = "Buy-Ins: 1";
            this.aiRecommendation.Text = string.Empty;
            this.dealerButton.IsVisible = false;
        });
    }

    public void SetSeatWinner(int seatIndex, bool isWinner)
    {
        var index = this.ToIndex(seatIndex);
        this.seatIsWinner[index] = isWinner;
        OnUi(() =>
        {
            var cardBorder = this.seatCardBorders[index];
            cardBorder.Stroke = isWinner ? WinnerSeatBorderColor : DefaultCardBorderColor;
            cardBorder.StrokeThickness = isWinner ? 2 : 1;
            this.ApplySeatInfoBorder(index);
            this.ApplySeatCardBorder(index);

            if (isWinner)
            {
                this.seatStatuses[index].Text = "Winner";
            }
        });
    }

    public void SetCommunityCards(string cards)
    {
        OnUi(() => SetCardText(this.communityCards, cards));
    }

    public void SetPot(int pot)
    {
        OnUi(() => this.pot.Text = $"Pot: {pot}");
    }

    public void SetTurnInfo(string text)
    {
        OnUi(() => this.turnInfo.Text = text);
    }

    public void SetHandNumber(int handNumber)
    {
        OnUi(() => this.handCounter.Text = $"Hands: {handNumber}");
    }

    public void SetBuyIns(int buyIns)
    {
        OnUi(() => this.buyInCounter.Text = $"Buy-Ins: {buyIns}");
    }

    public void ClearLog()
    {
        OnUi(() => this.actionLog.Text = string.Empty);
    }

    public void AddLog(string message)
    {
        OnUi(() =>
        {
            var current = this.actionLog.Text ?? string.Empty;
            if (current.Length > 12000)
            {
                current = current[^6000..];
            }

            if (current.Length > 0)
            {
                current += Environment.NewLine;
            }

            this.actionLog.Text = current + message;
        });
    }

    /// <summary>
    /// Keeps the action log scrolled to its newest line.
    ///
    /// AddLog used to call ScrollToAsync immediately after setting the text,
    /// before Android had laid the label out again, so the scroll was clamped
    /// to the previous content height. Worse, while the Log panel is hidden no
    /// layout runs at all, so opening it showed the very first line of the
    /// session. Found by code review and confirmed on device 2026-09-10: the
    /// log text ended at "Round: Flop" while the panel was showing
    /// "---- New hand ----" from hand one. Windows scrolls its log to the end.
    ///
    /// Scrolling from the label's own SizeChanged runs after every re-layout,
    /// including the first one after the panel is shown, so the newest line is
    /// always the one in view.
    /// </summary>
    private void ActionLog_SizeChanged(object? sender, EventArgs e)
    {
        _ = this.actionLogScroll.ScrollToAsync(this.actionLog, ScrollToPosition.End, false);
    }

    public void SetAiRecommendation(string text)
    {
        OnUi(() => this.aiRecommendation.Text = text ?? string.Empty);
    }

    public int GetDealerSeat()
    {
        return this.dealerSeatIndex;
    }

    public void SetDealerSeat(int seatIndex)
    {
        var index = this.ToIndex(seatIndex);
        this.dealerSeatIndex = seatIndex;

        OnUi(() =>
        {
            // The button sits on the card slot's outer end, centred on the
            // slot's height and overlapping its edge by 6 units: the right end
            // for every seat except 6 and 7, whose right ends face the centre
            // panel, so theirs is the left end. It used to sit at the slot's
            // left in the bet row, which the widened bet slots now fill.
            // Session 9, 2026-09-11. WpfTableView keeps the old arithmetic.
            var slot = AbsoluteLayout.GetLayoutBounds(this.seatCardBorders[index]);
            if (double.IsNaN(slot.X) || double.IsNaN(slot.Y))
            {
                return;
            }

            const double overlap = 6;
            var x = seatIndex == 6 || seatIndex == 7
                ? slot.X - DealerButtonSize + overlap
                : slot.X + slot.Width - overlap;
            var y = slot.Y + (slot.Height - DealerButtonSize) / 2;
            this.dealerButton.IsVisible = true;

            AbsoluteLayout.SetLayoutBounds(this.dealerButton, new Rect(x, y, DealerButtonSize, DealerButtonSize));
        });
    }

    private static void OnUi(Action action)
    {
        if (MainThread.IsMainThread)
        {
            action();
            return;
        }

        MainThread.BeginInvokeOnMainThread(action);
    }

    private int ToIndex(int seatIndex)
    {
        if (seatIndex < 1 || seatIndex > this.seatNames.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(seatIndex));
        }

        return seatIndex - 1;
    }

    private void ApplySeatInfoBorder(int index)
    {
        var border = this.seatInfoBorders[index];

        if (this.seatIsWinner[index])
        {
            border.Stroke = WinnerSeatBorderColor;
            border.StrokeThickness = 2;
            return;
        }

        border.Stroke = this.seatIsHuman[index] ? HumanSeatBorderColor : DefaultSeatBorderColor;
        border.StrokeThickness = this.seatIsHuman[index] ? 2 : 1;
    }

    private void ApplySeatCardBorder(int index)
    {
        var border = this.seatCardBorders[index];

        if (this.seatIsWinner[index])
        {
            border.Stroke = WinnerSeatBorderColor;
            border.StrokeThickness = 2;
            return;
        }

        if (this.seatIsTurnActive[index] && this.seatIsHuman[index])
        {
            border.Stroke = HumanSeatBorderColor;
            border.StrokeThickness = 2;
            return;
        }

        border.Stroke = DefaultCardBorderColor;
        border.StrokeThickness = 1;
    }

    private void ApplySeatCards(int index)
    {
        var cards = this.seatCardCache[index];
        var showCards = this.seatIsHuman[index]
            || this.cardVisibilityMode == CardVisibilityMode.ShowAll
            || (this.cardVisibilityMode == CardVisibilityMode.PeekOnHover && this.seatIsHovered[index])
            || (this.seatEndedAtShowdown[index] is bool showed
                && (this.endOfHandReveal == EndOfHandReveal.Everyone || showed));

        // A folded seat's slot is dimmed only while it has cards in it (ALWAYS
        // mid-hand, or a reveal at the end). An empty slot is drawn at full
        // strength whatever the seat did, so every empty box on the felt looks
        // the same; dimming the blank ones under SHOWDOWN made them look like a
        // different colour. Session 9, 2026-09-11.
        var hasCards = !string.IsNullOrWhiteSpace(cards);
        this.seatCardBorders[index].Opacity = this.seatIsFolded[index] && showCards && hasCards ? 0.35 : 1.0;

        if (showCards)
        {
            SetCardText(this.seatCards[index], cards);
            return;
        }

        // Masked. A live seat shows the card backs; a folded seat, or one with
        // no cards dealt yet, shows nothing.
        var label = this.seatCards[index];
        var showBacks = !this.seatIsFolded[index] && hasCards;
        label.FormattedText = null;
        label.TextColor = MaskedCardsColor;
        label.Text = showBacks ? MaskedCards : string.Empty;
    }

    /// <summary>
    /// Mirrors WpfTableView.GetSuitBrush.
    /// </summary>
    private static Color GetSuitColor(string card)
    {
        if (string.IsNullOrWhiteSpace(card))
        {
            return DefaultTextColor;
        }

        var suit = card[^1];
        return suit switch
        {
            '♥' => RedSuitColor, // heart
            '♦' => RedSuitColor, // diamond
            '♠' => BlackSuitColor, // spade
            '♣' => BlackSuitColor, // club
            _ => DefaultTextColor,
        };
    }

    private static string ForceTextPresentation(string card)
    {
        var builder = new StringBuilder(card.Length + 2);
        foreach (var ch in card)
        {
            builder.Append(ch);
            if (ch is '♠' or '♣' or '♥' or '♦')
            {
                builder.Append(TextPresentationSelector);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Mirrors WpfTableView.SetCardText: one span per card so each suit carries its own
    /// colour, with a plain space between cards.
    /// </summary>
    private static void SetCardText(Label label, string cards)
    {
        if (string.IsNullOrWhiteSpace(cards))
        {
            label.FormattedText = null;
            label.Text = string.Empty;
            return;
        }

        var formatted = new FormattedString();
        var parts = cards.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var card = parts[i];
            formatted.Spans.Add(new Span
            {
                Text = ForceTextPresentation(card),
                TextColor = GetSuitColor(card),
            });

            if (i < parts.Length - 1)
            {
                formatted.Spans.Add(new Span { Text = " " });
            }
        }

        label.FormattedText = formatted;
    }
}
