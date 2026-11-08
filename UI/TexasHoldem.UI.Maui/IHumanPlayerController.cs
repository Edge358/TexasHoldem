namespace TexasHoldem.UI.Maui;

using TexasHoldem.Logic.Players;

public interface IHumanPlayerController
{
    PlayerAction RequestAction(IGetTurnContext context);
}
