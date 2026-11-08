namespace TexasHoldem.Presentation;

public interface IHandPauseController
{
    void WaitForNextHand();

    void RequestHandPause();
}
