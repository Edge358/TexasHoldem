namespace TexasHoldem.Logic.Tests.AI
{
    using TexasHoldem.AI.SmartPlayer.Decision;
    using TexasHoldem.AI.SmartPlayer.Models;
    using Xunit;

    public class PotOddsFloorTests
    {
        [Fact]
        public void StrongHand_PotOddsGood_FoldRemoved()
        {
            var features = new SpotFeatures
            {
                MadeHandClass = MadeHandClass.Strong,
                PotSize = 40,
                AmountToCall = 12,
                EffectiveStack = 200,
            };
            var shouldRemove = HeuristicDecisionModel.ShouldRemoveFoldCandidate(features);

            Assert.True(shouldRemove);
        }

        [Fact]
        public void MediumHand_VeryCheap_FoldRemoved()
        {
            var features = new SpotFeatures
            {
                MadeHandClass = MadeHandClass.Medium,
                PotSize = 100,
                AmountToCall = 17,
                EffectiveStack = 200,
            };
            var shouldRemove = HeuristicDecisionModel.ShouldRemoveFoldCandidate(features);

            Assert.True(shouldRemove);
        }
    }
}
