namespace TexasHoldem.UI.Maui;

using TexasHoldem.Logic.Players;
using TexasHoldem.Presentation;

public sealed class MauiHumanPlayer : BasePlayer, IHumanPlayer
{
    private readonly string name;
    private readonly int buyIn;
    private readonly IHumanPlayerController controller;

    public MauiHumanPlayer(string name, int buyIn, IHumanPlayerController controller)
    {
        this.name = name;
        this.buyIn = buyIn;
        this.controller = controller;
    }

    public override string Name => this.name;

    public override int BuyIn => this.buyIn;

    public override PlayerAction PostingBlind(IPostingBlindContext context)
    {
        return context.BlindAction;
    }

    public override PlayerAction GetTurn(IGetTurnContext context)
    {
        return this.controller.RequestAction(context);
    }
}
