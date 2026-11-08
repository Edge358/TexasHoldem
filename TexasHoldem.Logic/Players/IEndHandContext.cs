namespace TexasHoldem.Logic.Players
{
    using System.Collections.Generic;

    using TexasHoldem.Logic.Cards;

    public interface IEndHandContext
    {
        IReadOnlyCollection<Card> CommunityCards { get; }

        Dictionary<string, ICollection<Card>> ShowdownCards { get; }

        IReadOnlyCollection<string> Winners { get; }

        IReadOnlyDictionary<string, int> StartStacks { get; }

        IReadOnlyDictionary<string, int> EndStacks { get; }

        IReadOnlyDictionary<string, int> Contributions { get; }

        int PotTotal { get; }
    }
}
