namespace TexasHoldem.AI.SmartPlayer.Decision
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using TexasHoldem.AI.SmartPlayer.Helpers;
    using TexasHoldem.AI.SmartPlayer.Models;
    using TexasHoldem.Logic;
    using TexasHoldem.Logic.Cards;
    using TexasHoldem.Logic.Extensions;
    using TexasHoldem.Logic.Helpers;
    using TexasHoldem.Logic.Players;

    public static class RecommendationEngine
    {
        private static readonly HandBoardClassifier Classifier = new HandBoardClassifier();

        public static string BuildRecommendation(
            Card firstCard,
            Card secondCard,
            IReadOnlyCollection<Card> communityCards,
            IGetTurnContext context,
            int seatIndex,
            int dealerSeat,
            int seatCount)
        {
            var street = MapStreet(context.RoundType);
            var hole = $"{firstCard} {secondCard}";
            var board = communityCards != null && communityCards.Count > 0
                ? communityCards.CardsToString()
                : "-";
            var position = GetPositionLabel(seatIndex, dealerSeat, seatCount);

            var features = BuildSpotFeatures(firstCard, secondCard, communityCards, context, street);
            var potOdds = ComputePotOdds(context);
            var equity = EstimateEquity(features);
            var mix = BuildMix(features, context, potOdds, position);
            var actionLine = BuildActionLine(features, context, mix, position);
            var rationaleLines = BuildRationaleLines(features, context, potOdds);

            var lines = new List<string>
            {
                $"{GetStreetLabel(street)} | {hole} | {position}",
                mix,
            };
            lines.AddRange(rationaleLines);
            lines.Add(FormatOddsEquity(context, potOdds, equity));
            lines.Add($"Action: {actionLine}");

            return string.Join(Environment.NewLine, lines);
        }

        public static string BuildEndOfHandSummary(
            Card firstCard,
            Card secondCard,
            IReadOnlyCollection<Card> communityCards,
            int seatIndex,
            int dealerSeat,
            int seatCount,
            bool isWinner,
            int potTotal,
            int contribution,
            int netDelta,
            string lastAction)
        {
            var hole = $"{firstCard} {secondCard}";
            var board = communityCards != null && communityCards.Count > 0
                ? communityCards.CardsToString()
                : "-";
            var position = GetPositionLabel(seatIndex, dealerSeat, seatCount);

            var classification = Classifier.Classify(
                new List<Card> { firstCard, secondCard },
                communityCards?.ToList() ?? new List<Card>());
            var handLine = BuildHandDetailLine(firstCard, secondCard, communityCards);
            var textureLine = $"{classification.MadeHandClass} on {classification.BoardTexture.ToString().ToLowerInvariant()} board";

            var status = isWinner ? "WINNER" : (netDelta == 0 ? "PUSH" : "LOSER");
            var netText = netDelta >= 0 ? $"+{netDelta}" : netDelta.ToString();
            var potText = potTotal > 0 ? potTotal.ToString() : "-";
            var contribText = contribution > 0 ? contribution.ToString() : "-";
            var lastActionText = string.IsNullOrWhiteSpace(lastAction) ? "-" : lastAction;

            var lines = new[]
            {
                $"{status} | {hole} | {position}",
                $"Board {board}",
                $"Net {netText} | Pot {potText} | Inv {contribText}",
                handLine,
                textureLine,
                $"Last Action: {lastActionText}",
            };

            return string.Join(Environment.NewLine, lines);
        }

        private static SpotFeatures BuildSpotFeatures(
            Card firstCard,
            Card secondCard,
            IReadOnlyCollection<Card> communityCards,
            IGetTurnContext context,
            Street street)
        {
            var features = new SpotFeatures
            {
                Street = street,
                NumPlayersActive = DetermineActivePlayers(context),
                PotSize = context.CurrentPot,
                AmountToCall = context.MoneyToCall,
                HeroStack = context.MoneyLeft,
                EffectiveStack = Math.Max(1, context.MoneyLeft),
            };

            FillPreflopDescriptors(features, firstCard, secondCard);
            FillPreflopStrength(features, firstCard, secondCard);

            if (street != Street.Preflop && communityCards != null && communityCards.Count >= 3)
            {
                var holeCards = new List<Card> { firstCard, secondCard };
                var classification = Classifier.Classify(holeCards, communityCards.ToList());
                features.MadeHandClass = classification.MadeHandClass;
                features.DrawClass = classification.DrawClass;
                features.BoardTexture = classification.BoardTexture;
            }
            else
            {
                features.MadeHandClass = MadeHandClass.Air;
                features.DrawClass = DrawClass.None;
                features.BoardTexture = BoardTexture.Dry;
            }

            if (street == Street.River)
            {
                features.DrawClass = DrawClass.None;
            }

            return features;
        }

        private static void FillPreflopDescriptors(SpotFeatures features, Card firstCard, Card secondCard)
        {
            var firstRank = (int)firstCard.Type;
            var secondRank = (int)secondCard.Type;
            features.HighRank = Math.Max(firstRank, secondRank);
            features.LowRank = Math.Min(firstRank, secondRank);
            features.Gap = Math.Max(0, features.HighRank - features.LowRank - 1);
            features.IsPair = firstCard.Type == secondCard.Type;
            features.IsSuited = firstCard.Suit == secondCard.Suit;
            features.IsBroadway = features.HighRank >= (int)CardType.Ten && features.LowRank >= (int)CardType.Ten;
            features.IsAceX = firstCard.Type == CardType.Ace || secondCard.Type == CardType.Ace;
        }

        private static void FillPreflopStrength(SpotFeatures features, Card firstCard, Card secondCard)
        {
            if (features.Street != Street.Preflop)
            {
                features.PreflopStrength = -1;
                features.PreflopTier = PreflopTier.Unknown;
                return;
            }

            var valuation = HandStrengthValuation.PreFlop(firstCard, secondCard);
            var strength = (int)valuation;
            features.PreflopStrength = strength;
            features.PreflopTier = strength >= 3000
                ? PreflopTier.Premium
                : strength >= 2000
                    ? PreflopTier.Strong
                    : strength >= 1000
                        ? PreflopTier.Medium
                        : PreflopTier.Weak;
        }

        private static string BuildMix(SpotFeatures features, IGetTurnContext context, double potOdds, string position)
        {
            if (features.Street == Street.Preflop)
            {
                var facingBet = context.MoneyToCall > 0;
                var mix = GetPreflopMix(features.PreflopTier);
                if (facingBet && potOdds > 0.25)
                {
                    mix = ShiftAggressionDown(mix, 10);
                }

                if (IsLatePosition(position))
                {
                    mix = ShiftAggressionUp(mix, 5);
                }
                else if (IsEarlyPosition(position))
                {
                    mix = ShiftAggressionDown(mix, 5);
                }

                return $"R{mix.Raise} C{mix.Call} F{mix.Fold}";
            }

            if (context.MoneyToCall == 0)
            {
            var betMix = GetPostflopBetMix(features);
            return $"B{betMix.Bet} X{betMix.Check}";
            }

            var actionMix = GetPostflopCallMix(features, potOdds);
            return $"R{actionMix.Raise} C{actionMix.Call} F{actionMix.Fold}";
        }

        private static (int Raise, int Call, int Fold) GetPreflopMix(PreflopTier tier)
        {
            switch (tier)
            {
                case PreflopTier.Premium:
                    return (70, 25, 5);
                case PreflopTier.Strong:
                    return (45, 45, 10);
                case PreflopTier.Medium:
                    return (20, 50, 30);
                default:
                    return (5, 25, 70);
            }
        }

        private static (int Bet, int Check) GetPostflopBetMix(SpotFeatures features)
        {
            int bet;
            switch (features.MadeHandClass)
            {
                case MadeHandClass.Monster:
                    bet = 80;
                    break;
                case MadeHandClass.Strong:
                    bet = features.BoardTexture == BoardTexture.Wet ? 55 : 65;
                    break;
                case MadeHandClass.Medium:
                    bet = features.BoardTexture == BoardTexture.Wet ? 25 : 40;
                    break;
                default:
                    if (features.DrawClass == DrawClass.None)
                    {
                        bet = features.BoardTexture == BoardTexture.Wet ? 5 : 15;
                    }
                    else
                    {
                        bet = features.BoardTexture == BoardTexture.Wet ? 20 : 35;
                    }
                    break;
            }

            if (features.NumPlayersActive >= 3 && bet > 10)
            {
                bet -= 10;
            }

            bet = Math.Max(0, Math.Min(100, bet));
            return (bet, 100 - bet);
        }

        private static (int Raise, int Call, int Fold) GetPostflopCallMix(SpotFeatures features, double potOdds)
        {
            (int Raise, int Call, int Fold) mix;
            switch (features.MadeHandClass)
            {
                case MadeHandClass.Monster:
                    mix = (65, 30, 5);
                    break;
                case MadeHandClass.Strong:
                    mix = (35, 55, 10);
                    break;
                case MadeHandClass.Medium:
                    mix = (15, 60, 25);
                    break;
                case MadeHandClass.Weak:
                    mix = features.DrawClass == DrawClass.None ? (5, 40, 55) : (10, 55, 35);
                    break;
                default:
                    mix = features.DrawClass == DrawClass.None ? (5, 20, 75) : (10, 50, 40);
                    break;
            }

            if (potOdds <= 0.20)
            {
                mix = (mix.Raise, Math.Min(100, mix.Call + 10), Math.Max(0, mix.Fold - 10));
            }
            else if (potOdds >= 0.40)
            {
                mix = (mix.Raise, Math.Max(0, mix.Call - 10), Math.Min(100, mix.Fold + 10));
            }

            return mix;
        }

        private static (int Raise, int Call, int Fold) ShiftAggressionDown((int Raise, int Call, int Fold) mix, int amount)
        {
            var raise = Math.Max(0, mix.Raise - amount);
            var fold = Math.Min(100, mix.Fold + amount);
            var call = Math.Max(0, 100 - raise - fold);
            return (raise, call, fold);
        }

        private static (int Raise, int Call, int Fold) ShiftAggressionUp((int Raise, int Call, int Fold) mix, int amount)
        {
            var raise = Math.Min(100, mix.Raise + amount);
            var fold = Math.Max(0, mix.Fold - amount);
            var call = Math.Max(0, 100 - raise - fold);
            return (raise, call, fold);
        }

        private static string BuildActionLine(SpotFeatures features, IGetTurnContext context, string mix, string position)
        {
            if (features.Street == Street.Preflop)
            {
                var action = PickHighestAction(mix);
                if (action == "R")
                {
                    var bb = Math.Max(2, context.SmallBlind * 2);
                    var openSize = IsLatePosition(position) ? 3.0 : 2.5;
                    var raiseTo = (int)Math.Round(bb * openSize, MidpointRounding.AwayFromZero);
                    if (context.MoneyToCall > 0)
                    {
                        raiseTo = context.CurrentMaxBet + Math.Max(context.MinRaise, 0);
                    }

                    return $"Raise to {raiseTo}";
                }

                return action == "C" ? "Call" : "Fold";
            }

            if (context.MoneyToCall == 0)
            {
                var action = PickHighestAction(mix);
                if (action == "B")
                {
                    var size = GetPostflopBetSize(features);
                    return $"Bet {size:0.00} pot";
                }

                return "Check";
            }

            var postflopAction = PickHighestAction(mix);
            if (postflopAction == "R")
            {
                var raiseTo = context.CurrentMaxBet + Math.Max(context.MinRaise, 0);
                return $"Raise to {raiseTo}";
            }

            return postflopAction == "C" ? "Call" : "Fold";
        }

        private static string PickHighestAction(string mix)
        {
            var parts = mix.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var bestKey = "C";
            var bestValue = -1;
            foreach (var part in parts)
            {
                var key = part.Substring(0, 1);
                var valueText = part.Substring(1);
                if (!int.TryParse(valueText, out var value))
                {
                    continue;
                }

                if (value > bestValue)
                {
                    bestValue = value;
                    bestKey = key;
                }
            }

            return bestKey;
        }

        private static double GetPostflopBetSize(SpotFeatures features)
        {
            switch (features.MadeHandClass)
            {
                case MadeHandClass.Monster:
                    return 0.75;
                case MadeHandClass.Strong:
                    return 0.66;
                case MadeHandClass.Medium:
                    return 0.33;
                default:
                    return 0.33;
            }
        }

        private static IEnumerable<string> BuildRationaleLines(SpotFeatures features, IGetTurnContext context, double potOdds)
        {
            if (features.Street == Street.Preflop)
            {
            var descriptors = new List<string>();
            if (features.IsBroadway)
            {
                descriptors.Add("Broadway");
            }

            if (features.IsSuited)
            {
                descriptors.Add("suited");
            }

            if (features.IsPair)
            {
                descriptors.Add("pair");
            }

                var descriptorLine = descriptors.Count == 0
                    ? "Preflop tier baseline"
                    : $"{features.PreflopTier} " + string.Join(" ", descriptors);
                return new[]
                {
                    descriptorLine,
                    "Position-adjusted opener mix",
                };
            }

            var handLine = features.DrawClass == DrawClass.None
                ? features.MadeHandClass.ToString()
                : $"{features.MadeHandClass} + {features.DrawClass}";
            var textureLine = $"{features.BoardTexture} board";
            return new[]
            {
                handLine,
                textureLine,
            };
        }

        private static string BuildHandDetailLine(Card firstCard, Card secondCard, IReadOnlyCollection<Card> communityCards)
        {
            if (communityCards == null || communityCards.Count == 0)
            {
                return "Hand: -";
            }

            var holeRanks = new[] { (int)firstCard.Type, (int)secondCard.Type };
            var isPocketPair = holeRanks[0] == holeRanks[1];
            var allCards = new List<Card>(communityCards.Count + 2) { firstCard, secondCard };
            allCards.AddRange(communityCards);
            var handRank = Helpers.GetHandRank(allCards);
            var rankLabel = ToRankLabel(handRank);

            if (handRank == HandRankType.Pair)
            {
                var pairLabel = GetPairLabel(allCards, communityCards, holeRanks, isPocketPair);
                return isPocketPair ? $"Pocket Pair | {pairLabel}" : pairLabel;
            }

            return isPocketPair ? $"Pocket Pair | {rankLabel}" : rankLabel;
        }

        private static string GetPairLabel(List<Card> allCards, IReadOnlyCollection<Card> board, int[] holeRanks, bool isPocketPair)
        {
            var boardRanks = board.Select(card => (int)card.Type).Distinct().OrderByDescending(rank => rank).ToList();
            var maxBoardRank = boardRanks.Count > 0 ? boardRanks[0] : 0;
            var secondBoardRank = boardRanks.Count > 1 ? boardRanks[1] : 0;

            var pairRanks = allCards
                .GroupBy(card => (int)card.Type)
                .Where(group => group.Count() >= 2)
                .Select(group => group.Key)
                .OrderByDescending(rank => rank)
                .ToList();
            var pairRank = pairRanks.Count > 0 ? pairRanks[0] : 0;

            if (isPocketPair && holeRanks[0] > maxBoardRank)
            {
                return "Overpair";
            }

            if (isPocketPair && holeRanks[0] <= maxBoardRank)
            {
                return "Underpair";
            }

            if (pairRank == maxBoardRank)
            {
                return "Top Pair";
            }

            if (pairRank == secondBoardRank)
            {
                return "2nd Pair";
            }

            return "Low Pair";
        }

        private static string ToRankLabel(HandRankType rankType)
        {
            switch (rankType)
            {
                case HandRankType.HighCard:
                    return "High Card";
                case HandRankType.Pair:
                    return "Pair";
                case HandRankType.TwoPairs:
                    return "Two Pair";
                case HandRankType.ThreeOfAKind:
                    return "Trips";
                case HandRankType.Straight:
                    return "Straight";
                case HandRankType.Flush:
                    return "Flush";
                case HandRankType.FullHouse:
                    return "Full House";
                case HandRankType.FourOfAKind:
                    return "Quads";
                case HandRankType.StraightFlush:
                    return "Straight Flush";
                default:
                    return rankType.ToString();
            }
        }

        private static string FormatOddsEquity(IGetTurnContext context, double potOdds, int equity)
        {
            var oddsText = context.MoneyToCall > 0 ? $"{potOdds:P0}" : "-";
            var equityText = equity > 0 ? $"{equity}%" : "-";
            return $"Odds {oddsText} | Eq {equityText}";
        }

        private static double ComputePotOdds(IGetTurnContext context)
        {
            if (context.MoneyToCall <= 0)
            {
                return 0;
            }

            var denominator = Math.Max(1, context.CurrentPot + context.MoneyToCall);
            return context.MoneyToCall / (double)denominator;
        }

        private static int EstimateEquity(SpotFeatures features)
        {
            int baseEquity;
            if (features.Street == Street.Preflop)
            {
                switch (features.PreflopTier)
                {
                    case PreflopTier.Premium:
                        baseEquity = 65;
                        break;
                    case PreflopTier.Strong:
                        baseEquity = 57;
                        break;
                    case PreflopTier.Medium:
                        baseEquity = 49;
                        break;
                    case PreflopTier.Weak:
                        baseEquity = 38;
                        break;
                    default:
                        baseEquity = 45;
                        break;
                }
            }
            else
            {
                switch (features.MadeHandClass)
                {
                    case MadeHandClass.Monster:
                        baseEquity = 88;
                        break;
                    case MadeHandClass.Strong:
                        baseEquity = 72;
                        break;
                    case MadeHandClass.Medium:
                        baseEquity = 56;
                        break;
                    case MadeHandClass.Weak:
                        baseEquity = 40;
                        break;
                    default:
                        baseEquity = 25;
                        break;
                }
            }

            int drawBoost;
            switch (features.DrawClass)
            {
                case DrawClass.Combo:
                    drawBoost = 10;
                    break;
                case DrawClass.FD:
                    drawBoost = 7;
                    break;
                case DrawClass.OESD:
                    drawBoost = 6;
                    break;
                case DrawClass.Gutshot:
                    drawBoost = 3;
                    break;
                case DrawClass.TwoOver:
                    drawBoost = 4;
                    break;
                default:
                    drawBoost = 0;
                    break;
            }

            var equity = baseEquity + drawBoost;
            return Math.Max(5, Math.Min(95, equity));
        }

        private static int DetermineActivePlayers(IGetTurnContext context)
        {
            var names = new HashSet<string>();
            if (context.MainPot.ActivePlayer != null)
            {
                foreach (var name in context.MainPot.ActivePlayer)
                {
                    names.Add(name);
                }
            }

            if (context.SidePots != null)
            {
                foreach (var pot in context.SidePots)
                {
                    foreach (var name in pot.ActivePlayer)
                    {
                        names.Add(name);
                    }
                }
            }

            if (names.Count == 0 && context.PreviousRoundActions != null)
            {
                foreach (var entry in context.PreviousRoundActions)
                {
                    names.Add(entry.PlayerName);
                }
            }

            return Math.Max(2, names.Count);
        }

        private static Street MapStreet(GameRoundType roundType)
        {
            switch (roundType)
            {
                case GameRoundType.PreFlop:
                    return Street.Preflop;
                case GameRoundType.Flop:
                    return Street.Flop;
                case GameRoundType.Turn:
                    return Street.Turn;
                case GameRoundType.River:
                    return Street.River;
                default:
                    return Street.Preflop;
            }
        }

        private static string GetStreetLabel(Street street)
        {
            switch (street)
            {
                case Street.Preflop:
                    return "Pre";
                case Street.Flop:
                    return "Flop";
                case Street.Turn:
                    return "Turn";
                case Street.River:
                    return "River";
                default:
                    return street.ToString();
            }
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

        private static bool IsLatePosition(string position)
        {
            return position == "BTN" || position == "CO" || position == "HJ";
        }

        private static bool IsEarlyPosition(string position)
        {
            return position == "UTG" || position == "UTG+1" || position == "SB" || position == "BB";
        }
    }
}
