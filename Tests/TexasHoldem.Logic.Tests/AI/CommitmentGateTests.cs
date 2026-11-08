namespace TexasHoldem.Logic.Tests.AI
{
    using System.Collections.Generic;

    using TexasHoldem.AI.SmartPlayer.Decision;
    using TexasHoldem.AI.SmartPlayer.Models;
    using TexasHoldem.Logic.GameMechanics;
    using TexasHoldem.Logic.Players;
    using Xunit;

    public class CommitmentGateTests
    {
        [Fact]
        public void PreflopCommitGate_BlocksNonPremiumHighCommit()
        {
            var features = new SpotFeatures
            {
                Street = Street.Preflop,
                PreflopTier = PreflopTier.Medium,
                HeroStack = 200,
                EffectiveStack = 200,
                PotSize = 6,
                AmountToCall = 2,
            };
            var profile = PlayerProfile.CreateTag();
            var context = CreateContext(GameRoundType.PreFlop, moneyLeft: 200, currentPot: 6, currentMaxBet: 4, minRaise: 4);

            var allowed = HeuristicDecisionModel.PassesCommitmentGate(features, context, profile, bigBlind: 2, raiseBy: 140);

            Assert.False(allowed);
        }

        [Fact]
        public void PreflopCommitGate_AllowsPremiumHighCommit()
        {
            var features = new SpotFeatures
            {
                Street = Street.Preflop,
                PreflopTier = PreflopTier.Premium,
                HeroStack = 200,
                EffectiveStack = 200,
                PotSize = 6,
                AmountToCall = 2,
            };
            var profile = PlayerProfile.CreateReg();
            var context = CreateContext(GameRoundType.PreFlop, moneyLeft: 200, currentPot: 6, currentMaxBet: 4, minRaise: 4);

            var allowed = HeuristicDecisionModel.PassesCommitmentGate(features, context, profile, bigBlind: 2, raiseBy: 140);

            Assert.True(allowed);
        }

        [Fact]
        public void PostflopCommitGate_BlocksHighCommitWithoutMonsterOrLowSpr()
        {
            var features = new SpotFeatures
            {
                Street = Street.Turn,
                MadeHandClass = MadeHandClass.Strong,
                DrawClass = DrawClass.Combo,
                BoardTexture = BoardTexture.Wet,
                HeroStack = 200,
                EffectiveStack = 200,
                PotSize = 120,
                AmountToCall = 10,
            };
            var profile = PlayerProfile.CreateReg();
            var context = CreateContext(GameRoundType.Turn, moneyLeft: 200, currentPot: 120, currentMaxBet: 30, minRaise: 20);

            var allowed = HeuristicDecisionModel.PassesCommitmentGate(features, context, profile, bigBlind: 2, raiseBy: 170);

            Assert.False(allowed);
        }

        private static IGetTurnContext CreateContext(GameRoundType roundType, int moneyLeft, int currentPot, int currentMaxBet, int minRaise)
        {
            return new GetTurnContext(
                roundType,
                new List<PlayerActionAndName>(),
                smallBlind: 1,
                moneyLeft: moneyLeft,
                currentPot: currentPot,
                myMoneyInTheRound: 0,
                currentMaxBet: currentMaxBet,
                minRaise: minRaise,
                mainPot: new Pot(currentPot, new List<string>()),
                sidePots: new List<Pot>());
        }
    }
}
