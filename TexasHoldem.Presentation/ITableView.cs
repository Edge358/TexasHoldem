namespace TexasHoldem.Presentation;

public interface ITableView
{
    void SetSeatName(int seatIndex, string name);

    void RegisterPlayerName(int seatIndex, string playerName);

    string GetSeatName(int seatIndex);

    string GetSeatNameByPlayer(string playerName);

    string GetPlayerNameBySeat(int seatIndex);

    int GetSeatContributionTotal(int seatIndex);

    void SetSeatStack(int seatIndex, int stack);

    void SetSeatCards(int seatIndex, string cards);

    void SetSeatIsHuman(int seatIndex, bool isHuman);

    void SetSeatTurnActive(int seatIndex, bool isActive);

    void SetSeatBet(int seatIndex, int total, int lastContribution, string prefix = "");

    void SetSeatStatus(int seatIndex, string status);

    void SetSeatFolded(int seatIndex, bool isFolded);

    void SetSeatHover(int seatIndex, bool isHovered);

    void SetCardVisibility(CardVisibilityMode mode, EndOfHandReveal reveal);

    void RevealSeatAtHandEnd(int seatIndex, bool wasAtShowdown);

    void SetSeatWinner(int seatIndex, bool isWinner);

    void SetCommunityCards(string cards);

    void SetPot(int pot);

    void SetTurnInfo(string text);

    void SetHandNumber(int handNumber);

    void SetBuyIns(int buyIns);

    void ClearLog();

    void AddLog(string message);

    void SetAiRecommendation(string text);

    int GetDealerSeat();

    void SetDealerSeat(int seatIndex);
}
