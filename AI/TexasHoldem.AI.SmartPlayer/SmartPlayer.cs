namespace TexasHoldem.AI.SmartPlayer
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using TexasHoldem.AI.SmartPlayer.Decision;
    using TexasHoldem.AI.SmartPlayer.Helpers;
    using TexasHoldem.AI.SmartPlayer.Models;
    using TexasHoldem.Logic;
    using TexasHoldem.Logic.Cards;
    using TexasHoldem.Logic.Players;

    public class SmartPlayer : BasePlayer
    {
        private PlayerProfile profile;
        private readonly IDecisionModel decisionModel;
        private readonly HeuristicDecisionModel heuristicDecisionModel;
        private readonly HandBoardClassifier classifier;
        private bool wasPreflopAggressor;
        private bool flopWasCheckedThrough;

        public SmartPlayer()
            : this(PlayerProfile.CreateReg())
        {
        }

        public SmartPlayer(PlayerProfile profile)
        {
            this.profile = profile ?? PlayerProfile.CreateReg();
            this.classifier = new HandBoardClassifier();
            this.heuristicDecisionModel = new HeuristicDecisionModel(this.classifier);
            this.decisionModel = this.heuristicDecisionModel;
        }

        public override string Name { get; } = "SmartPlayer_" + Guid.NewGuid();

        public override int BuyIn { get; } = -1;

        public PlayerArchetype Archetype => this.profile.Archetype;

        public void UpdateProfile(PlayerProfile profile)
        {
            this.profile = profile ?? PlayerProfile.CreateReg();
        }

        public override PlayerAction PostingBlind(IPostingBlindContext context)
        {
            return context.BlindAction;
        }

        public override void StartHand(IStartHandContext context)
        {
            this.wasPreflopAggressor = false;
            this.flopWasCheckedThrough = false;
            base.StartHand(context);
        }

        public override PlayerAction GetTurn(IGetTurnContext context)
        {
            this.UpdateRoundState(context);
            var features = this.BuildSpotFeatures(context);
            var action = this.decisionModel.Decide(features, context, this.profile);
            this.LogDecision(features, action, context);
            return action;
        }

        private SpotFeatures BuildSpotFeatures(IGetTurnContext context)
        {
            var communityCards = this.CommunityCards?.ToList() ?? new List<Card>();
            var numPlayersActive = DetermineActivePlayers(context);
            var positionIndex = Math.Max(0, Math.Min(7, context.PreviousRoundActions?.Count ?? 0));
            var features = new SpotFeatures
            {
                Street = MapStreet(context.RoundType),
                NumPlayersActive = numPlayersActive,
                PotSize = context.CurrentPot,
                AmountToCall = context.MoneyToCall,
                HeroStack = context.MoneyLeft,
                EffectiveStack = Math.Max(1, context.MoneyLeft),
                PositionIndex = positionIndex,
                PositionBucket = DeterminePositionBucket(positionIndex, numPlayersActive),
                WasPreflopAggressor = this.wasPreflopAggressor,
                FlopWasCheckedThrough = this.flopWasCheckedThrough,
            };

            features.IsHeadsUp = features.NumPlayersActive <= 2;
            this.FillPreflopDescriptors(features);
            this.FillPreflopStrength(features);

            if (features.Street != Street.Preflop && communityCards.Count >= 3)
            {
                var holeCards = new List<Card> { this.FirstCard, this.SecondCard };
                var classification = this.classifier.Classify(holeCards, communityCards);
                features.MadeHandClass = classification.MadeHandClass;
                features.DrawClass = classification.DrawClass;
                features.BoardTexture = classification.BoardTexture;
                features.IsPairedBoard = classification.IsPairedBoard;
                features.IsMonotone = classification.IsMonotone;
                features.IsTwoTone = classification.IsTwoTone;
            }
            else
            {
                features.MadeHandClass = MadeHandClass.Air;
                features.DrawClass = DrawClass.None;
                features.BoardTexture = BoardTexture.Dry;
                features.IsPairedBoard = false;
                features.IsMonotone = false;
                features.IsTwoTone = false;
            }

            return features;
        }

        private void FillPreflopDescriptors(SpotFeatures features)
        {
            var firstRank = (int)this.FirstCard.Type;
            var secondRank = (int)this.SecondCard.Type;
            features.HighRank = Math.Max(firstRank, secondRank);
            features.LowRank = Math.Min(firstRank, secondRank);
            features.Gap = Math.Max(0, features.HighRank - features.LowRank - 1);
            features.IsPair = this.FirstCard.Type == this.SecondCard.Type;
            features.IsSuited = this.FirstCard.Suit == this.SecondCard.Suit;
            features.IsBroadway = features.HighRank >= (int)CardType.Ten && features.LowRank >= (int)CardType.Ten;
            features.IsAceX = this.FirstCard.Type == CardType.Ace || this.SecondCard.Type == CardType.Ace;
        }

        private void FillPreflopStrength(SpotFeatures features)
        {
            if (features.Street != Street.Preflop)
            {
                features.PreflopStrength = -1;
                features.PreflopTier = PreflopTier.Unknown;
                return;
            }

            var valuation = HandStrengthValuation.PreFlop(this.FirstCard, this.SecondCard);
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

        private static PositionBucket DeterminePositionBucket(int positionIndex, int numPlayersActive)
        {
            if (numPlayersActive <= 2)
            {
                return PositionBucket.Late;
            }

            var maxIndex = Math.Max(1, numPlayersActive - 1);
            var ratio = positionIndex / (double)maxIndex;
            if (ratio <= 0.3)
            {
                return PositionBucket.Early;
            }

            if (ratio <= 0.6)
            {
                return PositionBucket.Mid;
            }

            if (ratio <= 0.85)
            {
                return PositionBucket.Late;
            }

            return PositionBucket.Blinds;
        }

        private void LogDecision(SpotFeatures features, PlayerAction action, IGetTurnContext context)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var seatLabel = $"Seat{this.GetSeatLabel()}";
            var archetype = this.profile.Archetype.ToString();
            var skill = this.profile.SkillLevel.ToString();
            var street = features.Street.ToString();
            var pot = features.PotSize.ToString();
            var toCall = features.AmountToCall.ToString();
            var heroStack = features.HeroStack.ToString();
            var actionName = action.Type.ToString();
            var amount = action.Type == PlayerActionType.Raise ? action.Money.ToString() : "-";
            var bb = this.heuristicDecisionModel.LastBigBlind.ToString();
            var currentMaxBet = context.CurrentMaxBet.ToString();
            var minRaiseTo = context.MinRaise.ToString();
            var canRaise = context.CanRaise.ToString();
            var spr = (features.PotSize > 0 ? (features.HeroStack / (double)features.PotSize) : features.HeroStack).ToString("0.00");
            var isHeadsUp = features.IsHeadsUp.ToString();
            var numPlayersActive = features.NumPlayersActive.ToString();
            var boardTexture = features.BoardTexture.ToString();
            var madeHand = features.MadeHandClass.ToString();
            var draw = features.DrawClass.ToString();
            var raiseTo = "-";
            if (action.Type == PlayerActionType.Raise)
            {
                var baseTo = context.MoneyToCall > 0 ? context.CurrentMaxBet : 0;
                raiseTo = (baseTo + action.Money).ToString();
            }
            var preflopTier = features.Street == Street.Preflop ? features.PreflopTier.ToString() : "-";
            var preflopStrength = features.Street == Street.Preflop ? features.PreflopStrength.ToString() : "-";
            var raiseDepth = features.Street == Street.Preflop ? features.RaiseDepth.ToString() : "-";
            var commit = action.Type == PlayerActionType.Raise ? features.AmountToCall + action.Money : 0;
            var commitFrac = commit / (double)Math.Max(1, features.HeroStack);
            var thinValue = features.ThinValueApplied == 1 ? "1" : "0";
            var valueCBet = features.ValueCBetApplied == 1 ? "1" : "0";
            var delayedValueCBet = features.DelayedValueCBetApplied == 1 ? "1" : "0";

            DecisionLogger.Log(
                $"{timestamp} | {seatLabel} | {archetype} | {skill} | {street} | {pot} | {toCall} | {heroStack} | {actionName} | {amount} | {bb} | {currentMaxBet} | {minRaiseTo} | {canRaise} | {spr} | {isHeadsUp} | {numPlayersActive} | {boardTexture} | {madeHand} | {draw} | {raiseTo} | {preflopTier} | {preflopStrength} | {raiseDepth} | {commitFrac:0.00} | {commit} | {thinValue} | {valueCBet} | {delayedValueCBet}");
        }

        private void UpdateRoundState(IGetTurnContext context)
        {
            if (context.RoundType == GameRoundType.Flop)
            {
                this.wasPreflopAggressor = WasLastAggressor(context.PreviousRoundActions, this.Name);
            }
            else if (context.RoundType == GameRoundType.Turn)
            {
                this.flopWasCheckedThrough = WasRoundCheckedThrough(context.PreviousRoundActions);
            }
        }

        private static bool WasLastAggressor(IEnumerable<PlayerActionAndName> actions, string playerName)
        {
            if (actions == null)
            {
                return false;
            }

            var lastRaise = actions.LastOrDefault(action => action.Action.Type == PlayerActionType.Raise);
            return !string.IsNullOrWhiteSpace(lastRaise.PlayerName)
                && string.Equals(lastRaise.PlayerName, playerName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool WasRoundCheckedThrough(IEnumerable<PlayerActionAndName> actions)
        {
            if (actions == null)
            {
                return false;
            }

            return actions.All(action => action.Action.Type != PlayerActionType.Raise);
        }

        private string GetSeatLabel()
        {
            var name = this.Name ?? string.Empty;
            var prefix = "SmartPlayer_";
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return name.Substring(prefix.Length);
            }

            return name;
        }
    }
}
