namespace TexasHoldem.AI.SmartPlayer.Decision
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using TexasHoldem.AI.SmartPlayer.Models;
    using TexasHoldem.Logic.Extensions;
    using TexasHoldem.Logic.Players;

    public sealed class HeuristicDecisionModel : IDecisionModel
    {
        private readonly HandBoardClassifier classifier;

        public HeuristicDecisionModel(HandBoardClassifier classifier)
        {
            this.classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        }

        public int LastBigBlind { get; private set; } = 2;

        public PlayerAction Decide(SpotFeatures features, IGetTurnContext context, PlayerProfile profile)
        {
            var bigBlind = this.EstimateBigBlind(features, context);
            this.LastBigBlind = bigBlind;
            if (features.Street == Street.Preflop)
            {
                features.RaiseDepth = this.GetPreflopRaiseDepth(context, bigBlind);
            }
            else
            {
                features.RaiseDepth = 0;
            }
            var streetRaiseDepth = this.GetStreetRaiseDepth(features, context, bigBlind);
            var gated = this.GetPreflopGateAction(features, context, profile, bigBlind);
            if (gated != null)
            {
                return gated;
            }
            var allowPreflopRaise = this.ShouldAllowPreflopRaises(features, context, profile, bigBlind);
            var allowPreflopJam = allowPreflopRaise && this.ShouldIncludePreflopAllIn(features, context, profile, bigBlind);
            var candidates = this.BuildCandidates(features, context, profile, bigBlind, allowPreflopJam, allowPreflopRaise, streetRaiseDepth);
            if (candidates.Count == 0)
            {
                return PlayerAction.CheckOrCall();
            }

            candidates = this.ApplyThinValueBetFilter(features, context, profile, candidates);
            if (candidates.Count == 0)
            {
                return PlayerAction.CheckOrCall();
            }

            candidates = candidates
                .Where(candidate => candidate.Type != CandidateType.Raise
                    || PassesCommitmentGate(features, context, profile, bigBlind, candidate.RaiseAmount))
                .ToList();
            if (candidates.Count == 0)
            {
                return PlayerAction.CheckOrCall();
            }

            candidates = this.ApplyPotOddsFloor(features, candidates);
            if (candidates.Count == 0)
            {
                return PlayerAction.CheckOrCall();
            }

            var strength = EvaluateStrength(features);
            var scores = candidates.Select(candidate => this.ScoreCandidate(candidate, features, context, profile, strength, bigBlind, allowPreflopJam)).ToList();
            var mistakeChance = MistakeChance(profile.SkillLevel);
            if (NextDouble() < mistakeChance && candidates.Count > 1)
            {
                var randomIndex = RandomProvider.Next(0, candidates.Count);
                return this.NormalizeFinalAction(features, context, profile, candidates[randomIndex].Action);
            }

            var temperature = GetTemperature(profile.SkillLevel);
            var bestScore = scores.Max();
            var weights = scores.Select(score => Math.Exp((score - bestScore) / temperature)).ToList();
            var total = weights.Sum();
            var pick = NextDouble() * total;
            var cumulative = 0.0;
            for (var i = 0; i < candidates.Count; i++)
            {
                cumulative += weights[i];
                if (pick <= cumulative)
                {
                    return this.NormalizeFinalAction(features, context, profile, candidates[i].Action);
                }
            }

            return this.NormalizeFinalAction(features, context, profile, candidates.Last().Action);
        }

        private List<CandidateAction> BuildCandidates(SpotFeatures features, IGetTurnContext context, PlayerProfile profile, int bigBlind, bool allowPreflopJam, bool allowPreflopRaise, int streetRaiseDepth)
        {
            var candidates = new List<CandidateAction>();

            if (context.MoneyToCall > 0)
            {
                candidates.Add(CandidateAction.Fold());
            }

            if (context.CanCheck || context.MoneyToCall == 0)
            {
                candidates.Add(CandidateAction.CheckCall(PlayerAction.CheckOrCall(), 0));
            }
            else
            {
                candidates.Add(CandidateAction.CheckCall(PlayerAction.CheckOrCall(), context.MoneyToCall));
            }

            if (context.CanRaise
                && context.MoneyLeft > context.MoneyToCall
                && allowPreflopRaise
                && this.ShouldAllowPostflopRaises(features, profile, context, streetRaiseDepth))
            {
                foreach (var raiseAmount in this.BuildRaiseSizes(features, context, profile, bigBlind, allowPreflopJam))
                {
                    candidates.Add(CandidateAction.Raise(PlayerAction.Raise(raiseAmount), raiseAmount));
                }
            }

            return candidates;
        }

        private IEnumerable<int> BuildRaiseSizes(SpotFeatures features, IGetTurnContext context, PlayerProfile profile, int bigBlind, bool allowPreflopJam)
        {
            var sizes = new HashSet<int>();
            var maxRaise = Math.Max(0, context.MoneyLeft - context.MoneyToCall);
            if (maxRaise <= 0)
            {
                return sizes;
            }

            if (features.Street == Street.Preflop)
            {
                var isUnopened = context.CurrentMaxBet == bigBlind;
                if (isUnopened)
                {
                    foreach (var raiseTo in this.GetPreflopOpenTargets(profile, bigBlind))
                    {
                        var raiseBy = raiseTo - context.CurrentMaxBet;
                        if (raiseBy > 0)
                        {
                            sizes.Add(raiseBy);
                        }
                    }
                }
                else if (context.CurrentMaxBet > bigBlind)
                {
                    var multiplier = this.GetPreflopThreeBetMultiplier(profile);
                    var raiseTo = (int)Math.Round(context.CurrentMaxBet * multiplier);
                    var raiseBy = raiseTo - context.CurrentMaxBet;
                    if (raiseBy > 0)
                    {
                        sizes.Add(raiseBy);
                    }
                }
            }
            else
            {
                if (context.MoneyToCall == 0)
                {
                    if (this.TryAddValueCBetCandidate(features, context, profile, sizes))
                    {
                        return sizes;
                    }

                    if (this.TryAddDelayedValueCBetCandidate(features, context, profile, sizes))
                    {
                        return sizes;
                    }

                    if (!this.ShouldAllowMultiwayBluffBet(features, profile))
                    {
                        return sizes;
                    }

                    var pot = Math.Max(1, context.CurrentPot);
                    var capFactor = this.GetPostflopBetCapFactor(profile);
                    foreach (var fraction in this.GetPostflopBetFractions(profile))
                    {
                        var betSize = (int)Math.Round(pot * fraction);
                        var capped = this.CapBetSize(betSize, pot, capFactor, context);
                        sizes.Add(ComputeRaiseAmountFromBet(capped, context));
                    }
                }
                else
                {
                    var pot = Math.Max(1, context.CurrentPot);
                    if (profile.Archetype == PlayerArchetype.Maniac)
                    {
                        var capFactor = this.GetPostflopBetCapFactor(profile);
                        var potCap = this.CapBetSize(int.MaxValue, pot, capFactor, context);
                        if (potCap >= context.MinRaise)
                        {
                            var baseRaise = Math.Max(context.MinRaise, (int)Math.Round(context.MoneyToCall * 2.5));
                            sizes.Add(Math.Min(baseRaise, potCap));
                        }
                    }
                    else
                    {
                        var capFactor = this.GetPostflopBetCapFactor(profile);
                        var potCap = this.CapBetSize(int.MaxValue, pot, capFactor, context);
                        if (potCap >= context.MinRaise)
                        {
                            sizes.Add(context.MinRaise);
                            sizes.Add(Math.Min((int)Math.Round(potCap * 0.75), potCap));
                        }
                    }
                }
            }

            if (features.Street == Street.Preflop)
            {
                if (allowPreflopJam)
                {
                    sizes.Add(maxRaise);
                }
            }
            else if (this.ShouldIncludePostflopAllIn(features, profile))
            {
                sizes.Add(maxRaise);
            }

            var sanitized = new HashSet<int>();
            foreach (var size in sizes)
            {
                var normalized = NormalizeRaise(size, context);
                if (normalized > 0)
                {
                    if (features.Street == Street.Preflop && normalized >= maxRaise && !allowPreflopJam)
                    {
                        continue;
                    }
                    else if (features.Street == Street.Preflop && context.MoneyToCall <= bigBlind && !allowPreflopJam)
                    {
                        var raiseTo = context.CurrentMaxBet + normalized;
                        var openTargets = this.GetPreflopOpenTargets(profile, bigBlind);
                        if (!openTargets.Contains(raiseTo))
                        {
                            continue;
                        }
                    }
                    else if (features.Street != Street.Preflop && normalized >= maxRaise)
                    {
                        if (!this.ShouldIncludePostflopAllIn(features, profile))
                        {
                            continue;
                        }
                    }

                    sanitized.Add(normalized);
                }
            }

            return sanitized;
        }

        private static int ComputeRaiseAmountFromTotal(int totalTarget, IGetTurnContext context)
        {
            var raiseAmount = totalTarget - context.MyMoneyInTheRound - context.MoneyToCall;
            return Math.Max(context.MinRaise, raiseAmount);
        }

        private static int ComputeRaiseAmountFromBet(int betSize, IGetTurnContext context)
        {
            return Math.Max(context.MinRaise, betSize);
        }

        private static int NormalizeRaise(int raiseAmount, IGetTurnContext context)
        {
            var maxRaise = Math.Max(0, context.MoneyLeft - context.MoneyToCall);
            if (maxRaise <= 0)
            {
                return 0;
            }

            if (raiseAmount >= maxRaise)
            {
                return maxRaise;
            }

            if (raiseAmount < context.MinRaise && maxRaise < context.MinRaise)
            {
                return maxRaise;
            }

            return raiseAmount;
        }

        private int EstimateBigBlind(SpotFeatures features, IGetTurnContext context)
        {
            var bb = context.SmallBlind > 0 ? context.SmallBlind * 2 : 2;
            if (features.Street == Street.Preflop)
            {
                if (context.MoneyToCall > 0 && context.MoneyToCall <= bb * 2)
                {
                    return Math.Max(2, context.MoneyToCall);
                }

                if (context.CurrentMaxBet > 0 && context.CurrentMaxBet <= bb * 2)
                {
                    return Math.Max(2, context.CurrentMaxBet);
                }
            }

            return Math.Max(2, bb);
        }

        private int GetPreflopOpenTo(PlayerProfile profile, int bigBlind)
        {
            switch (profile.Archetype)
            {
                case PlayerArchetype.Rock:
                case PlayerArchetype.Nit:
                case PlayerArchetype.WeakTight:
                    return (int)Math.Round(bigBlind * 2.5);
                case PlayerArchetype.LAG:
                    return bigBlind * 4;
                case PlayerArchetype.Maniac:
                    return bigBlind * 6;
                default:
                    return bigBlind * 3;
            }
        }

        private HashSet<int> GetPreflopOpenTargets(PlayerProfile profile, int bigBlind)
        {
            switch (profile.Archetype)
            {
                case PlayerArchetype.Rock:
                case PlayerArchetype.Nit:
                case PlayerArchetype.WeakTight:
                    return new HashSet<int> { (int)Math.Round(bigBlind * 2.5) };
                case PlayerArchetype.LAG:
                    return new HashSet<int> { bigBlind * 3, bigBlind * 4 };
                case PlayerArchetype.Maniac:
                    return new HashSet<int> { bigBlind * 4, bigBlind * 6 };
                default:
                    return new HashSet<int> { bigBlind * 3 };
            }
        }

        private double GetPreflopThreeBetMultiplier(PlayerProfile profile)
        {
            switch (profile.Archetype)
            {
                case PlayerArchetype.Rock:
                case PlayerArchetype.Nit:
                case PlayerArchetype.WeakTight:
                    return 3.0;
                case PlayerArchetype.TAG:
                case PlayerArchetype.Reg:
                    return 3.5;
                case PlayerArchetype.LAG:
                    return 4.0;
                case PlayerArchetype.Maniac:
                    return 5.0;
                default:
                    return 3.0;
            }
        }

        private IReadOnlyCollection<double> GetPostflopBetFractions(PlayerProfile profile)
        {
            switch (profile.Archetype)
            {
                case PlayerArchetype.Rock:
                case PlayerArchetype.Nit:
                case PlayerArchetype.WeakTight:
                    return new[] { 0.33, 0.5 };
                case PlayerArchetype.TAG:
                case PlayerArchetype.Reg:
                    return new[] { 0.33, 0.66 };
                case PlayerArchetype.CallingStation:
                case PlayerArchetype.AggroFish:
                    return new[] { 0.33, 0.5 };
                case PlayerArchetype.LAG:
                    return new[] { 0.5, 0.75 };
                case PlayerArchetype.Maniac:
                default:
                    return new[] { 0.33, 0.5, 0.75, 1.0 };
            }
        }
        private int GetPreflopThreeBetCap(PlayerProfile profile, int bigBlind)
        {
            switch (profile.Archetype)
            {
                case PlayerArchetype.Rock:
                case PlayerArchetype.Nit:
                case PlayerArchetype.WeakTight:
                    return bigBlind * 6;
                case PlayerArchetype.LAG:
                    return bigBlind * 10;
                case PlayerArchetype.Maniac:
                    return bigBlind * 14;
                default:
                    return bigBlind * 8;
            }
        }

        private bool ShouldIncludePreflopAllIn(SpotFeatures features, IGetTurnContext context, PlayerProfile profile, int bigBlind)
        {
            if (features.Street != Street.Preflop || features.PreflopTier == PreflopTier.Unknown || bigBlind <= 0)
            {
                return false;
            }

            var heroStackBb = features.HeroStack / (double)bigBlind;
            var toCallBb = context.MoneyToCall / (double)bigBlind;
            var tier = features.PreflopTier;

            if (heroStackBb <= 25 && (tier == PreflopTier.Strong || tier == PreflopTier.Premium))
            {
                return true;
            }

            if (toCallBb >= 12 && tier == PreflopTier.Premium)
            {
                return true;
            }

            if (profile.Archetype == PlayerArchetype.Maniac
                && (toCallBb >= 6 || heroStackBb <= 40)
                && (tier == PreflopTier.Strong || tier == PreflopTier.Premium))
            {
                return true;
            }

            return false;
        }

        private bool ShouldAllowPreflopRaises(SpotFeatures features, IGetTurnContext context, PlayerProfile profile, int bigBlind)
        {
            if (features.Street != Street.Preflop || features.PreflopTier == PreflopTier.Unknown)
            {
                return true;
            }

            if (bigBlind <= 0)
            {
                return true;
            }

            if (features.RaiseDepth >= 3 && profile.Archetype != PlayerArchetype.Maniac)
            {
                return features.PreflopTier == PreflopTier.Premium;
            }

            var isUnopened = context.CurrentMaxBet <= bigBlind;
            var facingOpen = context.CurrentMaxBet > bigBlind;
            var toCallBb = context.MoneyToCall / (double)bigBlind;

            if (features.PreflopTier == PreflopTier.Weak)
            {
                if (facingOpen)
                {
                    return profile.Archetype == PlayerArchetype.Maniac;
                }

                if (isUnopened)
                {
                    switch (profile.Archetype)
                    {
                        case PlayerArchetype.LAG:
                            return context.MoneyToCall == 0 && RandomProvider.Next(0, 100) < 10;
                        case PlayerArchetype.Maniac:
                            return true;
                        default:
                            return false;
                    }
                }
            }

            if (features.PreflopTier == PreflopTier.Medium)
            {
                if (facingOpen)
                {
                    if (profile.Archetype == PlayerArchetype.Maniac)
                    {
                        return true;
                    }

                    if (profile.Archetype == PlayerArchetype.LAG)
                    {
                        return toCallBb <= 4;
                    }

                    return false;
                }

                return true;
            }

            return true;
        }

        private int GetPreflopRaiseDepth(IGetTurnContext context, int bigBlind)
        {
            var bb = Math.Max(1, bigBlind);
            var maxBetBb = context.CurrentMaxBet / (double)bb;

            if (maxBetBb <= 2.0)
            {
                return 0;
            }

            if (maxBetBb <= 6.0)
            {
                return 1;
            }

            if (maxBetBb <= 15.0)
            {
                return 2;
            }

            if (maxBetBb <= 35.0)
            {
                return 3;
            }

            return 4;
        }

        private bool ShouldAllowPostflopRaises(SpotFeatures features, PlayerProfile profile, IGetTurnContext context, int streetRaiseDepth)
        {
            if (features.Street == Street.Preflop)
            {
                return true;
            }

            if (features.Street == Street.River)
            {
                if (features.MadeHandClass == MadeHandClass.Air)
                {
                    switch (profile.Archetype)
                    {
                        case PlayerArchetype.LAG:
                            return RandomProvider.Next(0, 100) < 15;
                        case PlayerArchetype.Maniac:
                            return true;
                        default:
                            return false;
                    }
                }

                if ((features.MadeHandClass == MadeHandClass.Weak || features.MadeHandClass == MadeHandClass.Medium)
                    && profile.Archetype == PlayerArchetype.AggroFish)
                {
                    return features.MadeHandClass == MadeHandClass.Strong
                        || features.MadeHandClass == MadeHandClass.Monster
                        || features.DrawClass == DrawClass.Combo;
                }
            }

            if (profile.Archetype == PlayerArchetype.LAG || profile.Archetype == PlayerArchetype.Maniac)
            {
                return this.ApplyPostflopEscalationGates(features, profile, context, streetRaiseDepth);
            }

            if (profile.Archetype == PlayerArchetype.CallingStation)
            {
                return this.ApplyPostflopEscalationGates(
                    features,
                    profile,
                    context,
                    streetRaiseDepth,
                    features.MadeHandClass == MadeHandClass.Strong
                    || features.MadeHandClass == MadeHandClass.Monster
                    || features.DrawClass == DrawClass.Combo);
            }

            if (profile.Archetype == PlayerArchetype.Rock
                || profile.Archetype == PlayerArchetype.Nit
                || profile.Archetype == PlayerArchetype.WeakTight)
            {
                return this.ApplyPostflopEscalationGates(
                    features,
                    profile,
                    context,
                    streetRaiseDepth,
                    features.MadeHandClass == MadeHandClass.Monster
                    || (features.DrawClass == DrawClass.Combo && features.BoardTexture != BoardTexture.Dry));
            }

            if (profile.Archetype == PlayerArchetype.TAG || profile.Archetype == PlayerArchetype.Reg)
            {
                var baseAllowed = features.MadeHandClass == MadeHandClass.Strong
                    || features.MadeHandClass == MadeHandClass.Monster
                    || (features.DrawClass == DrawClass.Combo && features.BoardTexture != BoardTexture.Dry);
                return this.ApplyPostflopEscalationGates(features, profile, context, streetRaiseDepth, baseAllowed);
            }

            return this.ApplyPostflopEscalationGates(features, profile, context, streetRaiseDepth, true);
        }

        private bool ApplyPostflopEscalationGates(
            SpotFeatures features,
            PlayerProfile profile,
            IGetTurnContext context,
            int streetRaiseDepth,
            bool baseAllowed = true)
        {
            if (!baseAllowed)
            {
                return false;
            }

            if (features.Street == Street.Preflop)
            {
                return true;
            }

            var responding = context.MoneyToCall > 0;
            if (!responding)
            {
                return true;
            }

            if (streetRaiseDepth >= 3)
            {
                var allowed = features.MadeHandClass == MadeHandClass.Monster
                    || (features.MadeHandClass == MadeHandClass.Strong
                        && features.DrawClass == DrawClass.Combo
                        && features.BoardTexture != BoardTexture.Dry);

                if (!allowed)
                {
                    return false;
                }

                if (profile.Archetype == PlayerArchetype.Rock
                    || profile.Archetype == PlayerArchetype.Nit
                    || profile.Archetype == PlayerArchetype.WeakTight
                    || profile.Archetype == PlayerArchetype.CallingStation)
                {
                    return features.MadeHandClass == MadeHandClass.Monster;
                }

                if (profile.Archetype == PlayerArchetype.TAG || profile.Archetype == PlayerArchetype.Reg)
                {
                    return this.RollChance(10);
                }

                if (profile.Archetype == PlayerArchetype.LAG || profile.Archetype == PlayerArchetype.AggroFish)
                {
                    return this.RollChance(20);
                }

                if (profile.Archetype == PlayerArchetype.Maniac)
                {
                    return this.RollChance(30);
                }

                return false;
            }

            if (streetRaiseDepth == 2)
            {
                if (features.DrawClass == DrawClass.Gutshot || features.DrawClass == DrawClass.TwoOver)
                {
                    return false;
                }

                if (features.MadeHandClass == MadeHandClass.Monster || features.MadeHandClass == MadeHandClass.Strong)
                {
                    return true;
                }

                if (features.DrawClass == DrawClass.Combo)
                {
                    if (profile.Archetype == PlayerArchetype.LAG)
                    {
                        return this.RollChance(25);
                    }

                    if (profile.Archetype == PlayerArchetype.Maniac)
                    {
                        return this.RollChance(35);
                    }

                    if (profile.Archetype == PlayerArchetype.TAG || profile.Archetype == PlayerArchetype.Reg)
                    {
                        return this.RollChance(15);
                    }

                    return false;
                }

                if (features.DrawClass == DrawClass.OESD || features.DrawClass == DrawClass.FD)
                {
                    if (features.BoardTexture == BoardTexture.Dry)
                    {
                        return false;
                    }

                    if (profile.Archetype == PlayerArchetype.LAG)
                    {
                        return this.RollChance(12);
                    }

                    if (profile.Archetype == PlayerArchetype.Maniac)
                    {
                        return this.RollChance(15);
                    }

                    return false;
                }

                return false;
            }

            return true;
        }

        private int GetStreetRaiseDepth(SpotFeatures features, IGetTurnContext context, int bigBlind)
        {
            if (features.Street == Street.Preflop)
            {
                return 0;
            }

            var pot = Math.Max(1, features.PotSize);
            if (context.MoneyToCall == 0 && context.CurrentMaxBet == 0)
            {
                return 0;
            }

            var depth = 1;
            if (context.MoneyToCall > 0)
            {
                if (context.MoneyToCall < context.CurrentMaxBet)
                {
                    depth = 2;
                }
            }

            if (depth == 2)
            {
                if (context.MinRaise >= (int)Math.Round(context.CurrentMaxBet * 1.5))
                {
                    return 3;
                }
            }

            return depth;
        }

        private bool RollChance(int percent)
        {
            return RandomProvider.Next(0, 100) < percent;
        }

        private List<CandidateAction> ApplyThinValueBetFilter(
            SpotFeatures features,
            IGetTurnContext context,
            PlayerProfile profile,
            List<CandidateAction> candidates)
        {
            if (features.ValueCBetApplied == 1 || features.DelayedValueCBetApplied == 1)
            {
                return candidates;
            }

            features.ThinValueApplied = 0;
            if (!IsThinValueEligible(features, context, profile))
            {
                return candidates;
            }

            var frequency = GetThinValueFrequency(profile, features.BoardTexture);
            if (frequency <= 0 || !this.RollChance(frequency))
            {
                return candidates.Where(candidate => candidate.Type != CandidateType.Raise).ToList();
            }

            var pot = Math.Max(1, features.PotSize);
            var raiseBy = ComputeRaiseAmountFromBet((int)Math.Round(pot * 0.33), context);
            var filtered = candidates.Where(candidate =>
                candidate.Type != CandidateType.Raise || candidate.RaiseAmount == raiseBy).ToList();

            features.ThinValueApplied = 1;
            return filtered;
        }

        internal static bool IsThinValueEligible(SpotFeatures features, IGetTurnContext context, PlayerProfile profile)
        {
            if (features.Street != Street.Flop && features.Street != Street.Turn)
            {
                return false;
            }

            if (context.MoneyToCall != 0 || !context.CanRaise)
            {
                return false;
            }

            if (features.MadeHandClass != MadeHandClass.Medium || features.DrawClass != DrawClass.None)
            {
                return false;
            }

            if (features.NumPlayersActive > 3)
            {
                return false;
            }

            if (features.BoardTexture == BoardTexture.Wet)
            {
                return false;
            }

            return profile.Archetype == PlayerArchetype.LAG
                || profile.Archetype == PlayerArchetype.AggroFish
                || profile.Archetype == PlayerArchetype.Reg
                || profile.Archetype == PlayerArchetype.TAG;
        }

        private static int GetThinValueFrequency(PlayerProfile profile, BoardTexture texture)
        {
            var baseRate = 0;
            switch (profile.Archetype)
            {
                case PlayerArchetype.LAG:
                    baseRate = 55;
                    break;
                case PlayerArchetype.AggroFish:
                    baseRate = 45;
                    break;
                case PlayerArchetype.Reg:
                    baseRate = 40;
                    break;
                case PlayerArchetype.TAG:
                    baseRate = 35;
                    break;
                default:
                    baseRate = 0;
                    break;
            }

            if (texture == BoardTexture.SemiWet)
            {
                baseRate = Math.Max(0, baseRate - 15);
            }

            return baseRate;
        }

        private bool TryAddValueCBetCandidate(
            SpotFeatures features,
            IGetTurnContext context,
            PlayerProfile profile,
            HashSet<int> sizes)
        {
            features.ValueCBetApplied = 0;
            if (!IsValueCBetEligible(features, context, profile))
            {
                return false;
            }

            var frequency = GetValueCBetFrequency(profile, features, isDelayed: false);
            if (frequency <= 0 || !this.RollChance(frequency))
            {
                return false;
            }

            var pot = Math.Max(1, features.PotSize);
            var raiseBy = ComputeRaiseAmountFromBet((int)Math.Round(pot * 0.33), context);
            sizes.Add(raiseBy);
            features.ValueCBetApplied = 1;
            return true;
        }

        private bool TryAddDelayedValueCBetCandidate(
            SpotFeatures features,
            IGetTurnContext context,
            PlayerProfile profile,
            HashSet<int> sizes)
        {
            features.DelayedValueCBetApplied = 0;
            if (!IsDelayedValueCBetEligible(features, context, profile))
            {
                return false;
            }

            var frequency = GetValueCBetFrequency(profile, features, isDelayed: true);
            if (frequency <= 0 || !this.RollChance(frequency))
            {
                return false;
            }

            var pot = Math.Max(1, features.PotSize);
            var raiseBy = ComputeRaiseAmountFromBet((int)Math.Round(pot * 0.45), context);
            sizes.Add(raiseBy);
            features.DelayedValueCBetApplied = 1;
            return true;
        }

        internal static bool IsValueCBetEligible(SpotFeatures features, IGetTurnContext context, PlayerProfile profile)
        {
            if (features.Street != Street.Flop)
            {
                return false;
            }

            if (context.MoneyToCall != 0 || !context.CanRaise)
            {
                return false;
            }

            if (!features.WasPreflopAggressor)
            {
                return false;
            }

            if (features.MadeHandClass != MadeHandClass.Medium
                && features.MadeHandClass != MadeHandClass.Strong
                && features.MadeHandClass != MadeHandClass.Monster)
            {
                return false;
            }

            if (features.NumPlayersActive > 3)
            {
                return false;
            }

            if (features.NumPlayersActive >= 3
                && features.BoardTexture == BoardTexture.Wet
                && features.MadeHandClass != MadeHandClass.Strong
                && features.MadeHandClass != MadeHandClass.Monster)
            {
                return false;
            }

            return true;
        }

        internal static bool IsDelayedValueCBetEligible(SpotFeatures features, IGetTurnContext context, PlayerProfile profile)
        {
            if (features.Street != Street.Turn)
            {
                return false;
            }

            if (context.MoneyToCall != 0 || !context.CanRaise)
            {
                return false;
            }

            if (!features.WasPreflopAggressor || !features.FlopWasCheckedThrough)
            {
                return false;
            }

            if (features.MadeHandClass != MadeHandClass.Medium
                && features.MadeHandClass != MadeHandClass.Strong
                && features.MadeHandClass != MadeHandClass.Monster)
            {
                return false;
            }

            if (features.NumPlayersActive > 3)
            {
                return false;
            }

            if (features.NumPlayersActive >= 3
                && features.BoardTexture == BoardTexture.Wet
                && features.MadeHandClass != MadeHandClass.Strong
                && features.MadeHandClass != MadeHandClass.Monster)
            {
                return false;
            }

            return true;
        }

        private static int GetValueCBetFrequency(PlayerProfile profile, SpotFeatures features, bool isDelayed)
        {
            var baseRate = 0;
            switch (profile.Archetype)
            {
                case PlayerArchetype.LAG:
                    baseRate = isDelayed ? 70 : 80;
                    break;
                case PlayerArchetype.AggroFish:
                    baseRate = isDelayed ? 55 : 65;
                    break;
                case PlayerArchetype.Reg:
                    baseRate = isDelayed ? 55 : 60;
                    break;
                case PlayerArchetype.TAG:
                    baseRate = isDelayed ? 45 : 55;
                    break;
                default:
                    baseRate = isDelayed ? 0 : 30;
                    break;
            }

            if (features.NumPlayersActive >= 3 && features.BoardTexture == BoardTexture.Wet)
            {
                baseRate = Math.Max(0, baseRate - 20);
            }

            return baseRate;
        }

        private bool ShouldAllowMultiwayBluffBet(SpotFeatures features, PlayerProfile profile)
        {
            if (features.Street == Street.Preflop)
            {
                return true;
            }

            if (features.IsHeadsUp || features.NumPlayersActive < 3)
            {
                return true;
            }

            if (features.MadeHandClass != MadeHandClass.Air && features.MadeHandClass != MadeHandClass.Weak)
            {
                return true;
            }

            if (features.DrawClass != DrawClass.None)
            {
                return true;
            }

            if (profile.Archetype == PlayerArchetype.Maniac)
            {
                return true;
            }

            if (profile.Archetype == PlayerArchetype.LAG)
            {
                return this.RollChance(12);
            }

            if (profile.Archetype == PlayerArchetype.AggroFish)
            {
                return this.RollChance(15);
            }

            return this.RollChance(4);
        }

        internal static bool PassesCommitmentGate(
            SpotFeatures features,
            IGetTurnContext context,
            PlayerProfile profile,
            int bigBlind,
            int raiseBy)
        {
            var heroStack = Math.Max(1, features.HeroStack);
            var commit = Math.Max(0, features.AmountToCall + raiseBy);
            var commitFrac = commit / (double)heroStack;
            if (commitFrac < 0.70)
            {
                return true;
            }

            if (features.Street == Street.Preflop)
            {
                if (features.PreflopTier == PreflopTier.Premium)
                {
                    return true;
                }

                if (profile.Archetype == PlayerArchetype.Maniac
                    && (features.PreflopTier == PreflopTier.Strong || features.PreflopTier == PreflopTier.Premium)
                    && bigBlind > 0
                    && (features.HeroStack / (double)bigBlind) <= 30)
                {
                    return true;
                }

                return false;
            }

            var spr = (double)features.EffectiveStack / Math.Max(1, features.PotSize);
            if (profile.Archetype != PlayerArchetype.Maniac && commitFrac >= 0.85)
            {
                return features.MadeHandClass == MadeHandClass.Monster || spr <= 1.0;
            }

            if (features.MadeHandClass == MadeHandClass.Monster)
            {
                return true;
            }

            if (features.MadeHandClass == MadeHandClass.Strong
                && features.DrawClass == DrawClass.Combo
                && features.BoardTexture != BoardTexture.Dry
                && (profile.Archetype == PlayerArchetype.LAG || profile.Archetype == PlayerArchetype.Maniac))
            {
                return true;
            }

            if (spr <= 1.0)
            {
                return true;
            }

            return false;
        }

        private List<CandidateAction> ApplyPotOddsFloor(SpotFeatures features, List<CandidateAction> candidates)
        {
            if (features.AmountToCall <= 0 || features.PotSize <= 0)
            {
                return candidates;
            }

            if (ShouldRemoveFoldCandidate(features))
            {
                return candidates.Where(candidate => candidate.Type != CandidateType.Fold).ToList();
            }

            return candidates;
        }

        internal static bool ShouldRemoveFoldCandidate(SpotFeatures features)
        {
            if (features.AmountToCall <= 0 || features.PotSize <= 0)
            {
                return false;
            }

            var spr = (double)features.EffectiveStack / Math.Max(1, features.PotSize);
            if (spr <= 0.5)
            {
                return false;
            }

            var ratio = features.AmountToCall / (double)Math.Max(1, features.PotSize + features.AmountToCall);
            var threshold = 0.0;
            if (features.MadeHandClass == MadeHandClass.Monster || features.MadeHandClass == MadeHandClass.Strong)
            {
                threshold = 0.30;
            }
            else if (features.MadeHandClass == MadeHandClass.Medium)
            {
                threshold = 0.15;
            }

            return threshold > 0 && ratio <= threshold;
        }

        private bool ShouldIncludePostflopAllIn(SpotFeatures features, PlayerProfile profile)
        {
            if (features.Street == Street.Preflop)
            {
                return false;
            }

            var spr = (double)features.EffectiveStack / Math.Max(1, features.PotSize);
            if (profile.Archetype == PlayerArchetype.Maniac)
            {
                return true;
            }

            if (features.MadeHandClass == MadeHandClass.Monster)
            {
                return true;
            }

            if (spr <= 3.0
                && (profile.Archetype == PlayerArchetype.LAG || profile.Archetype == PlayerArchetype.Maniac)
                && (features.MadeHandClass == MadeHandClass.Strong
                    || features.MadeHandClass == MadeHandClass.Monster
                    || features.DrawClass == DrawClass.Combo))
            {
                return true;
            }

            if (spr <= 2.0
                && profile.Archetype == PlayerArchetype.AggroFish
                && features.DrawClass == DrawClass.Combo
                && features.BoardTexture != BoardTexture.Dry)
            {
                return true;
            }

            return false;
        }
        private double GetPostflopBetCapFactor(PlayerProfile profile)
        {
            switch (profile.Archetype)
            {
                case PlayerArchetype.Rock:
                case PlayerArchetype.Nit:
                case PlayerArchetype.WeakTight:
                    return 0.75;
                case PlayerArchetype.LAG:
                    return 1.25;
                case PlayerArchetype.Maniac:
                    return 1.75;
                default:
                    return 1.0;
            }
        }

        private int CapBetSize(int betSize, int pot, double capFactor, IGetTurnContext context)
        {
            var cap = (int)Math.Round(pot * capFactor);
            var maxBet = Math.Min(context.MoneyLeft, cap);
            return Math.Min(betSize, Math.Max(1, maxBet));
        }

        private PlayerAction NormalizeFinalAction(SpotFeatures features, IGetTurnContext context, PlayerProfile profile, PlayerAction action)
        {
            if (action.Type != PlayerActionType.Raise)
            {
                return action;
            }

            var bigBlind = this.LastBigBlind > 0 ? this.LastBigBlind : this.EstimateBigBlind(features, context);
            var maxRaiseBy = Math.Max(0, context.MoneyLeft - context.MoneyToCall);
            if (maxRaiseBy <= 0)
            {
                return PlayerAction.CheckOrCall();
            }

            if (action.Money >= maxRaiseBy)
            {
                return PlayerAction.Raise(maxRaiseBy);
            }

            var allowedMaxRaiseBy = maxRaiseBy;
            if (features.Street == Street.Preflop)
            {
                var isUnopened = context.MoneyToCall == 0 && context.CurrentMaxBet <= bigBlind;
                var capRaiseTo = isUnopened
                    ? this.GetPreflopOpenTo(profile, bigBlind)
                    : context.CurrentMaxBet + this.GetPreflopThreeBetCap(profile, bigBlind);
                var capRaiseBy = Math.Max(0, capRaiseTo - context.CurrentMaxBet);
                allowedMaxRaiseBy = Math.Min(allowedMaxRaiseBy, capRaiseBy);
            }
            else
            {
                if (context.MoneyToCall == 0)
                {
                    var pot = Math.Max(1, context.CurrentPot);
                    var capFactor = this.GetPostflopBetCapFactor(profile);
                    var capBet = this.CapBetSize(int.MaxValue, pot, capFactor, context);
                    allowedMaxRaiseBy = Math.Min(allowedMaxRaiseBy, capBet);
                }
                else
                {
                    var pot = Math.Max(1, context.CurrentPot);
                    var capFactor = this.GetPostflopBetCapFactor(profile);
                    var capBet = this.CapBetSize(int.MaxValue, pot, capFactor, context);
                    allowedMaxRaiseBy = Math.Min(allowedMaxRaiseBy, capBet);
                }
            }

            if (allowedMaxRaiseBy < context.MinRaise)
            {
                return PlayerAction.CheckOrCall();
            }

            var clamped = Math.Min(action.Money, allowedMaxRaiseBy);
            if (clamped < context.MinRaise)
            {
                clamped = context.MinRaise;
            }

            clamped = Math.Min(clamped, allowedMaxRaiseBy);
            if (clamped <= 0)
            {
                return PlayerAction.CheckOrCall();
            }

            return PlayerAction.Raise(clamped);
        }

        private PlayerAction GetPreflopGateAction(SpotFeatures features, IGetTurnContext context, PlayerProfile profile, int bigBlind)
        {
            if (features.Street != Street.Preflop || features.PreflopTier == PreflopTier.Unknown)
            {
                return null;
            }

            var toCallBb = bigBlind > 0 ? context.MoneyToCall / (double)bigBlind : 0;
            var tier = features.PreflopTier;

            if (IsTightProfile(profile))
            {
                if (tier == PreflopTier.Weak)
                {
                    return this.FoldOrCheck(context);
                }

                if (toCallBb >= 10 && tier != PreflopTier.Premium)
                {
                    return this.FoldOrCheck(context);
                }

                if (toCallBb >= 6 && tier != PreflopTier.Strong && tier != PreflopTier.Premium)
                {
                    return this.FoldOrCheck(context);
                }
            }
            else if (profile.Archetype == PlayerArchetype.TAG || profile.Archetype == PlayerArchetype.Reg)
            {
                if (toCallBb >= 10 && tier == PreflopTier.Weak)
                {
                    return this.FoldOrCheck(context);
                }
            }
            else
            {
                if (toCallBb >= 20 && tier == PreflopTier.Weak)
                {
                    return this.FoldOrCheck(context);
                }
            }

            return null;
        }

        private static bool IsTightProfile(PlayerProfile profile)
        {
            return profile.Archetype == PlayerArchetype.Rock
                || profile.Archetype == PlayerArchetype.Nit
                || profile.Archetype == PlayerArchetype.WeakTight;
        }

        private PlayerAction FoldOrCheck(IGetTurnContext context)
        {
            return context.MoneyToCall == 0 ? PlayerAction.CheckOrCall() : PlayerAction.Fold();
        }

        private double ScoreCandidate(
            CandidateAction candidate,
            SpotFeatures features,
            IGetTurnContext context,
            PlayerProfile profile,
            double strength,
            int bigBlind,
            bool allowPreflopJam)
        {
            var pot = Math.Max(1, features.PotSize);
            var amountToCall = features.AmountToCall;
            var investment = candidate.Type == CandidateType.Raise
                ? amountToCall + candidate.RaiseAmount
                : amountToCall;
            var riskRatio = (double)investment / Math.Max(1, features.EffectiveStack);
            var potOdds = amountToCall > 0 ? (double)amountToCall / (pot + amountToCall) : 0;
            var positionBonus = features.PositionBucket == PositionBucket.Late
                ? 0.05 * profile.PositionSkill
                : features.PositionBucket == PositionBucket.Blinds
                    ? -0.03 * profile.PositionSkill
                    : 0;

            switch (candidate.Type)
            {
                case CandidateType.Fold:
                    return (profile.Tightness * 0.4) - (strength * 0.8) - (potOdds * 0.2);
                case CandidateType.CheckCall:
                    if (amountToCall == 0)
                    {
                        return strength + ((1 - profile.Aggression) * 0.1) + positionBonus;
                    }
                    else
                    {
                        return strength
                            - potOdds
                            - (profile.Tightness * 0.15)
                            - ((1 - profile.RiskTolerance) * riskRatio)
                            + positionBonus;
                    }
                case CandidateType.Raise:
                default:
                    var betSize = candidate.RaiseAmount + (amountToCall > 0 ? amountToCall : 0);
                    var foldEquity = EstimateFoldEquity(features, betSize, pot);
                    var score = strength
                        + (profile.Aggression * 0.2)
                        + (foldEquity * 0.4)
                        + positionBonus;

                    if (strength < 0.45)
                    {
                        var bluffBonus = profile.BluffRate * (features.IsHeadsUp ? 0.25 : 0.1);
                        if (features.BoardTexture == BoardTexture.Dry)
                        {
                            bluffBonus *= 1.1;
                        }
                        else if (features.BoardTexture == BoardTexture.Wet)
                        {
                            bluffBonus *= 0.6;
                        }

                        score += bluffBonus;
                    }

                    if (strength >= 0.8)
                    {
                        score -= profile.TrapRate * 0.1;
                    }

                    score -= (1 - profile.RiskTolerance) * riskRatio * 0.7;
                    if (strength < 0.5)
                    {
                    score -= profile.Tightness * 0.2;
                    }

                    var maxRaise = Math.Max(0, context.MoneyLeft - context.MoneyToCall);
                    var isAllIn = candidate.RaiseAmount >= maxRaise && maxRaise > 0;
                    if (isAllIn)
                    {
                        if (features.Street == Street.Preflop && !allowPreflopJam)
                        {
                            score -= GetPreflopAllInPenalty(profile);
                            return score;
                        }

                        var penalty = GetAllInPenalty(profile);
                        if (features.MadeHandClass == MadeHandClass.Monster)
                        {
                            penalty = 0;
                        }
                        else if (features.DrawClass == DrawClass.Combo && features.BoardTexture != BoardTexture.Dry)
                        {
                            penalty *= 0.35;
                        }

                        var spr = (double)features.EffectiveStack / Math.Max(1, features.PotSize);
                        if (spr <= 3.0)
                        {
                            penalty *= 0.5;
                        }

                        score -= penalty;
                    }

                    return score;
            }
        }

        private double GetPreflopAllInPenalty(PlayerProfile profile)
        {
            switch (profile.Archetype)
            {
                case PlayerArchetype.Rock:
                case PlayerArchetype.Nit:
                case PlayerArchetype.WeakTight:
                    return 3.0;
                case PlayerArchetype.TAG:
                case PlayerArchetype.Reg:
                    return 2.2;
                case PlayerArchetype.LAG:
                    return 1.4;
                case PlayerArchetype.Maniac:
                    return 0.9;
                default:
                    return 2.0;
            }
        }

        private static double GetAllInPenalty(PlayerProfile profile)
        {
            switch (profile.Archetype)
            {
                case PlayerArchetype.Rock:
                case PlayerArchetype.Nit:
                case PlayerArchetype.WeakTight:
                    return 1.2;
                case PlayerArchetype.TAG:
                case PlayerArchetype.Reg:
                    return 0.8;
                case PlayerArchetype.LAG:
                    return 0.45;
                case PlayerArchetype.Maniac:
                    return 0.15;
                default:
                    return 0.7;
            }
        }

        private static double EstimateFoldEquity(SpotFeatures features, int betSize, int pot)
        {
            var sizeRatio = Math.Min(1.0, (double)betSize / pot);
            var multiwayPenalty = features.NumPlayersActive >= 3 ? 0.6 : 1.0;
            var texturePenalty = features.BoardTexture == BoardTexture.Wet
                ? 0.75
                : features.BoardTexture == BoardTexture.SemiWet
                    ? 0.9
                    : 1.1;

            return Math.Max(0, Math.Min(1.0, sizeRatio * multiwayPenalty * texturePenalty));
        }

        private static double EvaluateStrength(SpotFeatures features)
        {
            if (features.Street == Street.Preflop)
            {
                return EvaluatePreflopStrength(features);
            }

            var baseStrength = 0.15;
            if (features.MadeHandClass == MadeHandClass.Monster)
            {
                baseStrength = 0.95;
            }
            else if (features.MadeHandClass == MadeHandClass.Strong)
            {
                baseStrength = 0.8;
            }
            else if (features.MadeHandClass == MadeHandClass.Medium)
            {
                baseStrength = 0.6;
            }
            else if (features.MadeHandClass == MadeHandClass.Weak)
            {
                baseStrength = 0.4;
            }

            var drawBoost = 0.0;
            if (features.DrawClass == DrawClass.Combo)
            {
                drawBoost = 0.2;
            }
            else if (features.DrawClass == DrawClass.FD)
            {
                drawBoost = 0.15;
            }
            else if (features.DrawClass == DrawClass.OESD)
            {
                drawBoost = 0.12;
            }
            else if (features.DrawClass == DrawClass.Gutshot)
            {
                drawBoost = 0.07;
            }
            else if (features.DrawClass == DrawClass.TwoOver)
            {
                drawBoost = 0.05;
            }

            return Math.Min(0.95, baseStrength + drawBoost);
        }

        private static double EvaluatePreflopStrength(SpotFeatures features)
        {
            var high = Math.Max(2, Math.Min(14, features.HighRank));
            var low = Math.Max(2, Math.Min(14, features.LowRank));
            var baseStrength = ((high - 2) / 12.0) * 0.6 + ((low - 2) / 12.0) * 0.2;
            if (features.IsPair)
            {
                baseStrength += 0.2 + ((high - 2) / 12.0) * 0.2;
            }

            if (features.IsSuited)
            {
                baseStrength += 0.05;
            }

            if (features.Gap <= 1)
            {
                baseStrength += 0.05;
            }
            else if (features.Gap >= 4)
            {
                baseStrength -= 0.05;
            }

            if (features.IsBroadway)
            {
                baseStrength += 0.05;
            }

            if (features.IsAceX)
            {
                baseStrength += 0.05;
            }

            return Math.Max(0.05, Math.Min(0.95, baseStrength));
        }

        private static double GetTemperature(int skillLevel)
        {
            var clamped = Math.Max(1, Math.Min(10, skillLevel));
            var temperature = 0.95 - ((clamped - 1) * 0.07);
            return Math.Max(0.25, temperature);
        }

        private static double MistakeChance(int skillLevel)
        {
            if (skillLevel <= 2)
            {
                return 0.06;
            }

            if (skillLevel <= 4)
            {
                return 0.03;
            }

            return 0.01;
        }

        private static double NextDouble()
        {
            return RandomProvider.Next(0, 10000) / 10000.0;
        }

        private sealed class CandidateAction
        {
            private CandidateAction(CandidateType type, PlayerAction action, int raiseAmount)
            {
                this.Type = type;
                this.Action = action;
                this.RaiseAmount = raiseAmount;
            }

            public CandidateType Type { get; }

            public PlayerAction Action { get; }

            public int RaiseAmount { get; }

            public static CandidateAction Fold()
            {
                return new CandidateAction(CandidateType.Fold, PlayerAction.Fold(), 0);
            }

            public static CandidateAction CheckCall(PlayerAction action, int amountToCall)
            {
                return new CandidateAction(CandidateType.CheckCall, action, amountToCall);
            }

            public static CandidateAction Raise(PlayerAction action, int raiseAmount)
            {
                return new CandidateAction(CandidateType.Raise, action, raiseAmount);
            }
        }

        private enum CandidateType
        {
            Fold,
            CheckCall,
            Raise,
        }
    }
}
