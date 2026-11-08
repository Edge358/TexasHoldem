namespace TexasHoldem.UI.Wpf;

using System.Linq;
using System.Collections.Generic;

using TexasHoldem.AI.SmartPlayer.Decision;
using TexasHoldem.Logic.Cards;
using TexasHoldem.Logic.Extensions;
using TexasHoldem.Logic.Players;

public sealed class WpfUiDecorator : PlayerDecorator
{
    private readonly int seatIndex;
    private readonly WpfTableView tableView;
    private readonly IHandPauseController handPauseController;
    private readonly bool isHuman;
    private int handContribution;
    private int handStartStack;
    private int? lastEndStack;
    private int buyInCount;
    private bool hasSeenFirstHand;
    private Card? firstCard;
    private Card? secondCard;
    private List<Card> communityCards;
    private string lastHumanAction;
    private bool hasLoggedCardsThisHand;
    private int smallBlindAmount;

    public WpfUiDecorator(IPlayer player, int seatIndex, WpfTableView tableView, IHandPauseController handPauseController)
        : base(player)
    {
        this.seatIndex = seatIndex;
        this.tableView = tableView;
        this.handPauseController = handPauseController;
        this.isHuman = player is WpfHumanPlayer;
        this.communityCards = new List<Card>();
        this.lastHumanAction = string.Empty;
        this.hasLoggedCardsThisHand = false;
        this.smallBlindAmount = 1;
    }

    public override void StartHand(IStartHandContext context)
    {
        this.handPauseController.WaitForNextHand();

        var cards = $"{context.FirstCard} {context.SecondCard}";
        this.tableView.SetHandNumber(context.HandNumber);
        this.tableView.SetSeatStack(this.seatIndex, context.MoneyLeft);
        this.tableView.SetSeatCards(this.seatIndex, cards);
        this.tableView.SetSeatStatus(this.seatIndex, "In hand");
        this.tableView.SetSeatFolded(this.seatIndex, false);
        this.tableView.SetSeatWinner(this.seatIndex, false);
        this.handContribution = 0;
        this.handStartStack = context.MoneyLeft;
        this.smallBlindAmount = context.SmallBlind;
        this.tableView.SetSeatBet(this.seatIndex, this.handContribution, 0);
        this.UpdateBuyIns(context.MoneyLeft);
        this.firstCard = context.FirstCard;
        this.secondCard = context.SecondCard;
        this.communityCards.Clear();
        this.lastHumanAction = string.Empty;
        this.hasLoggedCardsThisHand = false;
        if (this.isHuman)
        {
            this.tableView.SetSeatIsHuman(this.seatIndex, true);
            this.tableView.SetSeatTurnActive(this.seatIndex, false);
        }

        this.tableView.RegisterPlayerName(this.seatIndex, this.Player.Name);

        if (context.FirstPlayerName == this.Player.Name)
        {
            this.tableView.SetDealerSeat(this.seatIndex);
        }

        if (this.seatIndex == 1)
        {
            this.tableView.AddLog("---- New hand ----");
            this.tableView.SetAiRecommendation(string.Empty);
        }

        base.StartHand(context);
    }

    public override void StartRound(IStartRoundContext context)
    {
        var cards = context.CommunityCards == null
            ? string.Empty
            : context.CommunityCards.CardsToString();
        this.tableView.SetCommunityCards(cards);
        this.tableView.SetPot(context.CurrentPot);
        this.communityCards = context.CommunityCards?.ToList() ?? new List<Card>();

        if (this.seatIndex == 1)
        {
            this.tableView.AddLog($"Round: {context.RoundType}");
        }

        base.StartRound(context);
    }

    public override PlayerAction PostingBlind(IPostingBlindContext context)
    {
        this.tableView.SetPot(context.CurrentPot);
        this.tableView.SetSeatStack(this.seatIndex, context.CurrentStackSize);
        this.tableView.SetSeatStatus(this.seatIndex, "Posted blind");

        this.handContribution += context.BlindAction.Money;
        var blindPrefix = GetBlindPrefix(context.BlindAction.Money);
        this.tableView.SetSeatBet(this.seatIndex, this.handContribution, context.BlindAction.Money, blindPrefix);
        var seatName = this.tableView.GetSeatName(this.seatIndex);
        this.tableView.AddLog($"{seatName} posts blind {context.BlindAction.Money}");
        return base.PostingBlind(context);
    }

    public override PlayerAction GetTurn(IGetTurnContext context)
    {
        if (this.isHuman)
        {
            this.tableView.SetSeatTurnActive(this.seatIndex, true);
            this.tableView.SetSeatStatus(this.seatIndex, "Your turn");
            this.UpdateAiRecommendation(context);
        }

        this.LogCardsOnce();

        this.tableView.SetTurnInfo($"{this.Player.Name} to act ({context.RoundType})");
        this.tableView.SetPot(context.CurrentPot);
        this.tableView.SetSeatStack(this.seatIndex, context.MoneyLeft);

        var action = base.GetTurn(context);
        this.tableView.SetSeatStatus(this.seatIndex, ActionStatus(action, context));
        var seatName = this.tableView.GetSeatName(this.seatIndex);
        this.tableView.AddLog($"{seatName} {ActionText(action, context)}");

        var contribution = action.Type switch
        {
            PlayerActionType.CheckCall => context.MoneyToCall,
            PlayerActionType.Raise => context.MoneyToCall + action.Money,
            _ => 0,
        };
        this.handContribution += contribution;
        var actionPrefix = GetActionPrefix(action, context);
        this.tableView.SetSeatBet(this.seatIndex, this.handContribution, contribution, actionPrefix);
        this.tableView.SetSeatFolded(this.seatIndex, action.Type == PlayerActionType.Fold);

        var moneyAfterAction = action.Type == PlayerActionType.Fold
            ? context.MoneyLeft
            : context.MoneyLeft - action.Money - context.MoneyToCall;

        this.tableView.SetSeatStack(this.seatIndex, moneyAfterAction);
        if (this.isHuman)
        {
            this.lastHumanAction = ActionSummary(action, context);
            this.tableView.SetSeatTurnActive(this.seatIndex, false);
        }
        return action;
    }

    public override void EndHand(IEndHandContext context)
    {
        if (this.isHuman && context.EndStacks.TryGetValue(this.Player.Name, out var endStackValue))
        {
            this.lastEndStack = endStackValue;
        }

        if (context.Winners.Contains(this.Player.Name))
        {
            this.tableView.SetSeatWinner(this.seatIndex, true);
        }

        if (context.EndStacks.TryGetValue(this.Player.Name, out var endStack))
        {
            var delta = endStack - this.handStartStack;
            var deltaText = delta > 0 ? $"+{delta}" : delta.ToString();
            var status = context.Winners.Contains(this.Player.Name)
                ? $"Winner {deltaText}"
                : deltaText;
            this.tableView.SetSeatStatus(this.seatIndex, status);
            this.tableView.SetSeatStack(this.seatIndex, endStack);
        }

        if (this.seatIndex == 1 && context.ShowdownCards.Count > 0)
        {
            this.tableView.AddLog("Showdown:");
            foreach (var entry in context.ShowdownCards)
            {
                var cards = entry.Value.CardsToString();
                var seatName = this.tableView.GetSeatNameByPlayer(entry.Key);
                this.tableView.AddLog($"{seatName}: {cards}");
            }
        }

        if (this.seatIndex == 1 && context.CommunityCards.Count > 0)
        {
            this.tableView.AddLog($"Board: {context.CommunityCards.CardsToString()}");
        }

        if (this.seatIndex == 1)
        {
            this.tableView.AddLog($"Pot: {context.PotTotal}");
        }

        if (this.seatIndex == 1 && context.Winners.Count > 0)
        {
            var winners = string.Join(", ", context.Winners.Select(this.tableView.GetSeatNameByPlayer));
            this.tableView.AddLog($"Winners: {winners}");
        }

        if (this.seatIndex == 1)
        {
            for (var seat = 1; seat <= 8; seat++)
            {
                var seatName = this.tableView.GetSeatName(seat);
                var playerName = this.tableView.GetPlayerNameBySeat(seat);
                var contrib = this.tableView.GetSeatContributionTotal(seat);
                var finalStack = context.EndStacks.TryGetValue(playerName, out var endValue) ? endValue : 0;
                var startStack = context.StartStacks.TryGetValue(playerName, out var startValue) ? startValue : finalStack;
                var delta = finalStack - startStack;
                var deltaText = delta >= 0 ? $"+{delta}" : delta.ToString();
                this.tableView.AddLog($"{seatName}  Net: {deltaText}, Inv: {contrib}");
            }

            this.tableView.AddLog("Hand complete. Click Next Hand to continue.");
            this.handPauseController.RequestHandPause();
        }

        if (this.isHuman && this.firstCard != null && this.secondCard != null)
        {
            var isWinner = context.Winners.Contains(this.Player.Name);
            var finalStack = context.EndStacks.TryGetValue(this.Player.Name, out var value)
                ? value
                : this.handStartStack;
            var netDelta = finalStack - this.handStartStack;
            var inv = this.handContribution;
            var summary = RecommendationEngine.BuildEndOfHandSummary(
                this.firstCard,
                this.secondCard,
                context.CommunityCards.ToList(),
                this.seatIndex,
                this.tableView.GetDealerSeat(),
                8,
                isWinner,
                context.PotTotal,
                inv,
                netDelta,
                this.lastHumanAction);
            this.tableView.SetAiRecommendation(summary);
        }

        base.EndHand(context);
    }

    private void UpdateBuyIns(int startStack)
    {
        if (!this.isHuman)
        {
            return;
        }

        if (!this.hasSeenFirstHand)
        {
            this.buyInCount = 1;
            this.hasSeenFirstHand = true;
            this.tableView.SetBuyIns(this.buyInCount);
            return;
        }

        if (this.lastEndStack.HasValue && this.lastEndStack.Value <= 0 && startStack > 0)
        {
            this.buyInCount++;
            this.tableView.SetBuyIns(this.buyInCount);
        }
    }

    private void UpdateAiRecommendation(IGetTurnContext context)
    {
        if (this.firstCard == null || this.secondCard == null)
        {
            return;
        }

        var dealerSeat = this.tableView.GetDealerSeat();
        var recommendation = RecommendationEngine.BuildRecommendation(
            this.firstCard,
            this.secondCard,
            this.communityCards,
            context,
            this.seatIndex,
            dealerSeat,
            8);
        this.tableView.SetAiRecommendation(recommendation);
    }

    private void LogCardsOnce()
    {
        if (this.hasLoggedCardsThisHand || this.firstCard == null || this.secondCard == null)
        {
            return;
        }

        var seatName = this.tableView.GetSeatName(this.seatIndex);
        var position = GetPositionLabel(this.seatIndex, this.tableView.GetDealerSeat(), 8);
        this.tableView.AddLog($"{seatName} ({position}, {this.handStartStack}) cards: {this.firstCard} {this.secondCard}");
        this.hasLoggedCardsThisHand = true;
    }

    private static string GetPositionLabel(int seatIndex, int dealerSeat, int seatCount)
    {
        if (dealerSeat <= 0 || seatIndex <= 0 || seatCount <= 0)
        {
            return "Pos ?";
        }

        var offset = (seatIndex - dealerSeat + seatCount) % seatCount;
        var labels = new[]
        {
            "BTN",
            "SB",
            "BB",
            "UTG",
            "UTG+1",
            "MP",
            "HJ",
            "CO",
        };

        if (offset >= 0 && offset < labels.Length)
        {
            return labels[offset];
        }

        return $"Pos {offset}";
    }

    private static string ActionStatus(PlayerAction action, IGetTurnContext context)
    {
        return action.Type switch
        {
            PlayerActionType.Fold => "Folded",
            PlayerActionType.Raise => context.MoneyToCall == 0 ? "Bet" : "Raised",
            PlayerActionType.CheckCall => context.MoneyToCall == 0 ? "Checked" : "Called",
            _ => "Acted",
        };
    }

    private static string ActionText(PlayerAction action, IGetTurnContext context)
    {
        return action.Type switch
        {
            PlayerActionType.Fold => "folds",
            PlayerActionType.CheckCall => context.MoneyToCall > 0
                ? $"calls {context.MoneyToCall}"
                : "checks",
            PlayerActionType.Raise => $"raises to {action.Money + context.MoneyToCall + context.MyMoneyInTheRound}",
            _ => action.Type.ToString(),
        };
    }

    private static string ActionSummary(PlayerAction action, IGetTurnContext context)
    {
        return action.Type switch
        {
            PlayerActionType.Fold => "Fold",
            PlayerActionType.CheckCall => context.MoneyToCall > 0
                ? $"Call {context.MoneyToCall}"
                : "Check",
            PlayerActionType.Raise => $"Raise {action.Money + context.MoneyToCall + context.MyMoneyInTheRound}",
            _ => action.Type.ToString(),
        };
    }

    private string GetBlindPrefix(int blindAmount)
    {
        if (blindAmount == this.smallBlindAmount)
        {
            return "SB";
        }

        if (blindAmount == this.smallBlindAmount * 2)
        {
            return "BB";
        }

        return "B";
    }

    private static string GetActionPrefix(PlayerAction action, IGetTurnContext context)
    {
        switch (action.Type)
        {
            case PlayerActionType.Raise:
                var priorRaises = context.PreviousRoundActions?.Count(entry => entry.Action.Type == PlayerActionType.Raise) ?? 0;
                if (context.MoneyToCall == 0)
                {
                    return "B";
                }

                return priorRaises >= 1 ? "RR" : "R";
            case PlayerActionType.CheckCall:
                return context.MoneyToCall > 0 ? "C" : "X";
            case PlayerActionType.Fold:
                return "F";
            default:
                return string.Empty;
        }
    }
}
