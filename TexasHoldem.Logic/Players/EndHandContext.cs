namespace TexasHoldem.Logic.Players
{
    using System.Collections.Generic;

    using TexasHoldem.Logic.Cards;

    public class EndHandContext : IEndHandContext
    {
        public EndHandContext(
            IReadOnlyCollection<Card> communityCards,
            Dictionary<string, ICollection<Card>> showdownCards,
            IReadOnlyCollection<string> winners,
            IReadOnlyDictionary<string, int> startStacks,
            IReadOnlyDictionary<string, int> endStacks,
            IReadOnlyDictionary<string, int> contributions,
            int potTotal)
        {
            this.CommunityCards = communityCards;
            this.ShowdownCards = showdownCards;
            this.Winners = winners;
            this.StartStacks = startStacks;
            this.EndStacks = endStacks;
            this.Contributions = contributions;
            this.PotTotal = potTotal;
        }

        public IReadOnlyCollection<Card> CommunityCards { get; private set; }

        public Dictionary<string, ICollection<Card>> ShowdownCards { get; private set; }

        public IReadOnlyCollection<string> Winners { get; private set; }

        public IReadOnlyDictionary<string, int> StartStacks { get; private set; }

        public IReadOnlyDictionary<string, int> EndStacks { get; private set; }

        public IReadOnlyDictionary<string, int> Contributions { get; private set; }

        public int PotTotal { get; private set; }
    }
}
