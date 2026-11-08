namespace TexasHoldem.UI.Wpf;

using TexasHoldem.Logic.Players;

public interface IHumanPlayerController
{
    PlayerAction RequestAction(IGetTurnContext context);
}
