namespace TexasHoldem.UI.Wpf;

public interface IHandPauseController
{
    void WaitForNextHand();

    void RequestHandPause();
}
