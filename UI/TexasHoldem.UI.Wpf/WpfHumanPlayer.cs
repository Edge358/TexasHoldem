namespace TexasHoldem.UI.Wpf;

using TexasHoldem.Logic.Players;

public sealed class WpfHumanPlayer : BasePlayer
{
    private readonly string name;
    private readonly int buyIn;
    private readonly IHumanPlayerController controller;

    public WpfHumanPlayer(string name, int buyIn, IHumanPlayerController controller)
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
