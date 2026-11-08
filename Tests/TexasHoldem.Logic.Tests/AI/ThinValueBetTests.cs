namespace TexasHoldem.Logic.Tests.AI
{
    using System.Collections.Generic;

    using TexasHoldem.AI.SmartPlayer.Decision;
    using TexasHoldem.AI.SmartPlayer.Models;
    using TexasHoldem.Logic.GameMechanics;
    using TexasHoldem.Logic.Players;
    using Xunit;

    public class ThinValueBetTests
    {
        [Fact]
        public void MediumTopPair_ThinValueRollEligible_Dry_Flop()
        {
            var features = new SpotFeatures
            {
                Street = Street.Flop,
                MadeHandClass = MadeHandClass.Medium,
                DrawClass = DrawClass.None,
                BoardTexture = BoardTexture.Dry,
                NumPlayersActive = 2,
            };
            var profile = PlayerProfile.CreateTag();
            var context = CreateContext(GameRoundType.Flop, moneyLeft: 200, currentPot: 20, currentMaxBet: 0, minRaise: 2);

            var eligible = HeuristicDecisionModel.IsThinValueEligible(features, context, profile);

            Assert.True(eligible);
        }

        [Fact]
        public void MediumTopPair_ThinValueNotEligible_Wet_Flop()
        {
            var features = new SpotFeatures
            {
                Street = Street.Flop,
                MadeHandClass = MadeHandClass.Medium,
                DrawClass = DrawClass.None,
                BoardTexture = BoardTexture.Wet,
                NumPlayersActive = 2,
            };
            var profile = PlayerProfile.CreateTag();
            var context = CreateContext(GameRoundType.Flop, moneyLeft: 200, currentPot: 20, currentMaxBet: 0, minRaise: 2);

            var eligible = HeuristicDecisionModel.IsThinValueEligible(features, context, profile);

            Assert.False(eligible);
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
