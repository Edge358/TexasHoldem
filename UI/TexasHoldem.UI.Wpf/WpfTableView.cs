namespace TexasHoldem.UI.Wpf;

using System;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

public sealed class WpfTableView
{
    public enum CardVisibilityMode
    {
        ShowAll = 0,
        HumanOnly = 1,
        PeekOnHover = 2,
    }

    private const string MaskedCards = "?? ??";
    private static readonly Brush RedSuitBrush = new SolidColorBrush(Color.FromRgb(100, 10, 10));
    private static readonly Brush BlackSuitBrush = new SolidColorBrush(Color.FromRgb(18, 18, 18));
    private static readonly Brush DefaultTextBrush = new SolidColorBrush(Color.FromRgb(242, 242, 242));
    private static readonly Brush DefaultSeatBorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x4E, 0x4E, 0x4E));
    private static readonly Brush DefaultCardBorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x28, 0x28, 0x28));
    private static readonly Brush HumanSeatBorderBrush = new SolidColorBrush(Color.FromRgb(235, 235, 235));
    private static readonly Brush TurnSeatBorderBrush = new SolidColorBrush(Color.FromRgb(255, 214, 107));
    private static readonly Brush WinnerSeatBorderBrush = new SolidColorBrush(Color.FromRgb(230, 200, 92));

    private readonly TextBox[] seatNames;
    private readonly TextBox[] seatStacks;
    private readonly TextBlock[] seatCards;
    private readonly Border[] seatCardBorders;
    private readonly Border[] seatInfoBorders;
    private readonly TextBox[] seatBets;
    private readonly TextBox[] seatStatuses;
    private readonly bool[] seatIsHuman;
    private readonly bool[] seatIsTurnActive;
    private readonly bool[] seatIsWinner;
    private readonly bool[] seatIsHovered;
    private readonly string[] seatCardCache;
    private readonly TextBlock communityCards;
    private readonly TextBox pot;
    private readonly TextBox actionLog;
    private readonly TextBlock turnInfo;
    private readonly System.Windows.Shapes.Ellipse dealerButton;
    private readonly TextBlock handCounter;
    private readonly TextBlock buyInCounter;
    private readonly TextBox aiRecommendation;
    private int dealerSeatIndex;
    private readonly Dictionary<string, int> seatIndexByPlayerName;
    private readonly Dictionary<int, string> playerNameBySeatIndex;
    private readonly string[] seatNameCache;
    private readonly object seatNameLock = new object();
    private readonly int[] seatContributionTotals;
    private readonly object seatContributionLock = new object();
    private readonly Dispatcher dispatcher;
    private CardVisibilityMode cardVisibilityMode;

    public WpfTableView(
        Dispatcher dispatcher,
        TextBox[] seatNames,
        TextBox[] seatStacks,
        TextBlock[] seatCards,
        Border[] seatInfoBorders,
        Border[] seatCardBorders,
        TextBox[] seatBets,
        TextBox[] seatStatuses,
        TextBlock communityCards,
        TextBox pot,
        TextBox actionLog,
        TextBlock turnInfo,
        System.Windows.Shapes.Ellipse dealerButton,
        TextBlock handCounter,
        TextBlock buyInCounter,
        TextBox aiRecommendation)
    {
        this.dispatcher = dispatcher;
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
        this.seatCardCache = new string[seatNames.Length];
        this.communityCards = communityCards;
        this.pot = pot;
        this.actionLog = actionLog;
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
    }

    public void SetSeatName(int seatIndex, string name)
    {
        var index = this.ToIndex(seatIndex);
        lock (this.seatNameLock)
        {
            this.seatNameCache[index] = name ?? string.Empty;
        }

        this.OnUi(() => this.seatNames[index].Text = name);
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
        this.OnUi(() => this.seatStacks[this.ToIndex(seatIndex)].Text = stack.ToString("N0"));
    }

    public void SetSeatCards(int seatIndex, string cards)
    {
        this.OnUi(() =>
        {
            var index = this.ToIndex(seatIndex);
            this.seatCardCache[index] = cards ?? string.Empty;
            this.ApplySeatCards(index);
        });
    }

    public void SetSeatIsHuman(int seatIndex, bool isHuman)
    {
        this.OnUi(() =>
        {
            var index = this.ToIndex(seatIndex);
            this.seatIsHuman[index] = isHuman;
            this.ApplySeatInfoBorder(index);
            this.ApplySeatCards(index);
        });
    }

    public void SetSeatTurnActive(int seatIndex, bool isActive)
    {
        this.OnUi(() =>
        {
            var index = this.ToIndex(seatIndex);
            this.seatIsTurnActive[index] = isActive;
            this.ApplySeatInfoBorder(index);
            this.ApplySeatCardBorder(index);
        });
    }

    public void SetSeatBet(int seatIndex, int total, int lastContribution, string prefix = "")
    {
        this.OnUi(() =>
        {
            var index = this.ToIndex(seatIndex);
            lock (this.seatContributionLock)
            {
                this.seatContributionTotals[index] = total;
            }

            var prefixText = string.IsNullOrWhiteSpace(prefix) ? string.Empty : $"{prefix}   ";
            var text = string.Empty;
            if (total > 0 && lastContribution > 0)
            {
                text = $"{prefixText}+{lastContribution} ({total})";
            }
            else if (total > 0)
            {
                text = $"{prefixText}{total}";
            }
            else if (!string.IsNullOrWhiteSpace(prefixText))
            {
                text = prefixText.Trim();
            }

            this.seatBets[index].Text = text;
        });
    }

    public void SetSeatStatus(int seatIndex, string status)
    {
        this.OnUi(() => this.seatStatuses[this.ToIndex(seatIndex)].Text = status);
    }

    public void SetSeatFolded(int seatIndex, bool isFolded)
    {
        this.OnUi(() => this.seatCardBorders[this.ToIndex(seatIndex)].Opacity = isFolded ? 0.35 : 1.0);
    }

    public void SetSeatHover(int seatIndex, bool isHovered)
    {
        this.OnUi(() =>
        {
            var index = this.ToIndex(seatIndex);
            this.seatIsHovered[index] = isHovered;
            this.ApplySeatCards(index);
        });
    }

    public void SetCardVisibilityMode(CardVisibilityMode mode)
    {
        this.OnUi(() =>
        {
            this.cardVisibilityMode = mode;
            for (var i = 0; i < this.seatCards.Length; i++)
            {
                this.ApplySeatCards(i);
            }
        });
    }

    public void SetSeatWinner(int seatIndex, bool isWinner)
    {
        this.OnUi(() =>
        {
            var index = this.ToIndex(seatIndex);
            this.seatIsWinner[index] = isWinner;

            var cardBorder = this.seatCardBorders[index];
            cardBorder.BorderBrush = isWinner ? WinnerSeatBorderBrush : DefaultCardBorderBrush;
            cardBorder.BorderThickness = isWinner ? new System.Windows.Thickness(2) : new System.Windows.Thickness(1);
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
        this.OnUi(() => SetCardText(this.communityCards, cards));
    }

    public void SetPot(int pot)
    {
        this.OnUi(() => this.pot.Text = $"Pot: {pot}");
    }

    public void SetTurnInfo(string text)
    {
        this.OnUi(() => this.turnInfo.Text = text);
    }

    public void SetHandNumber(int handNumber)
    {
        this.OnUi(() => this.handCounter.Text = $"Hands: {handNumber}");
    }

    public void SetBuyIns(int buyIns)
    {
        this.OnUi(() => this.buyInCounter.Text = $"Buy-Ins: {buyIns}");
    }

    public void ClearLog()
    {
        this.OnUi(() => this.actionLog.Clear());
    }

    public void AddLog(string message)
    {
        this.OnUi(() =>
        {
            if (this.actionLog.Text.Length > 12000)
            {
                this.actionLog.Text = this.actionLog.Text[^6000..];
            }

            if (this.actionLog.Text.Length > 0)
            {
                this.actionLog.AppendText(Environment.NewLine);
            }

            this.actionLog.AppendText(message);
            this.actionLog.ScrollToEnd();
        });
    }

    public void SetAiRecommendation(string text)
    {
        this.OnUi(() => this.aiRecommendation.Text = text ?? string.Empty);
    }

    public int GetDealerSeat()
    {
        return this.dealerSeatIndex;
    }

    public void SetDealerSeat(int seatIndex)
    {
        this.OnUi(() =>
        {
            var cardBorder = this.seatCardBorders[this.ToIndex(seatIndex)];
            var left = Canvas.GetLeft(cardBorder);
            var top = Canvas.GetTop(cardBorder);
            var width = cardBorder.ActualWidth > 0 ? cardBorder.ActualWidth : cardBorder.Width;
            var height = cardBorder.ActualHeight > 0 ? cardBorder.ActualHeight : cardBorder.Height;

            if (double.IsNaN(left) || double.IsNaN(top))
            {
                return;
            }

            var x = left;
            var y = seatIndex == 1 || seatIndex == 8
                ? top + height + 6
                : top - this.dealerButton.Height - 6;

            Canvas.SetLeft(this.dealerButton, x);
            Canvas.SetTop(this.dealerButton, y);
            this.dealerSeatIndex = seatIndex;
        });
    }

    private static Brush GetSuitBrush(string card)
    {
        if (string.IsNullOrWhiteSpace(card))
        {
            return DefaultTextBrush;
        }

        var suit = card[^1];
        return suit switch
        {
            '\u2665' => RedSuitBrush, // heart
            '\u2666' => RedSuitBrush, // diamond
            '\u2660' => BlackSuitBrush, // spade
            '\u2663' => BlackSuitBrush, // club
            _ => DefaultTextBrush,
        };
    }

    private static void SetCardText(TextBlock textBlock, string cards)
    {
        textBlock.Inlines.Clear();

        if (string.IsNullOrWhiteSpace(cards))
        {
            return;
        }

        var parts = cards.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var card = parts[i];
            var run = new Run(card)
            {
                Foreground = GetSuitBrush(card),
            };
            textBlock.Inlines.Add(run);

            if (i < parts.Length - 1)
            {
                textBlock.Inlines.Add(new Run(" "));
            }
        }
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
            border.BorderBrush = WinnerSeatBorderBrush;
            border.BorderThickness = new System.Windows.Thickness(2);
            return;
        }

        border.BorderBrush = this.seatIsHuman[index] ? HumanSeatBorderBrush : DefaultSeatBorderBrush;
        border.BorderThickness = this.seatIsHuman[index]
            ? new System.Windows.Thickness(2)
            : new System.Windows.Thickness(1);
    }

    private void ApplySeatCardBorder(int index)
    {
        var border = this.seatCardBorders[index];

        if (this.seatIsWinner[index])
        {
            border.BorderBrush = WinnerSeatBorderBrush;
            border.BorderThickness = new System.Windows.Thickness(2);
            return;
        }

        if (this.seatIsTurnActive[index] && this.seatIsHuman[index])
        {
            border.BorderBrush = HumanSeatBorderBrush;
            border.BorderThickness = new System.Windows.Thickness(2);
            return;
        }

        border.BorderBrush = DefaultCardBorderBrush;
        border.BorderThickness = new System.Windows.Thickness(1);
    }

    private void ApplySeatCards(int index)
    {
        var cards = this.seatCardCache[index];
        var showCards = this.seatIsHuman[index]
            || this.cardVisibilityMode == CardVisibilityMode.ShowAll
            || (this.cardVisibilityMode == CardVisibilityMode.PeekOnHover && this.seatIsHovered[index]);

        SetCardText(this.seatCards[index], showCards ? cards : MaskedCards);
    }

    private void OnUi(Action action)
    {
        if (this.dispatcher.CheckAccess())
        {
            action();
            return;
        }

        this.dispatcher.Invoke(action);
    }
}
