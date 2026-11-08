namespace TexasHoldem.AI.SmartPlayer.Decision
{
    using TexasHoldem.AI.SmartPlayer.Models;
    using TexasHoldem.Logic.Players;

    public interface IDecisionModel
    {
        PlayerAction Decide(SpotFeatures features, IGetTurnContext context, PlayerProfile profile);
    }
}
