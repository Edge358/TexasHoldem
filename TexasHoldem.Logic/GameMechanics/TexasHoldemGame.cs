namespace TexasHoldem.Logic.GameMechanics
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using TexasHoldem.Logic.Players;

    public class TexasHoldemGame : ITexasHoldemGame
    {
        private const int DefaultCashGameStackCap = 200;
        private const int DefaultSmallBlind = 1;

        private readonly ICollection<InternalPlayer> allPlayers;

        private int initialMoney;
        private int smallBlind;
        private int cashGameStackCap;

        public TexasHoldemGame(IPlayer firstPlayer, IPlayer secondPlayer, int initialMoney = 1000, int smallBlind = DefaultSmallBlind, int cashGameStackCap = DefaultCashGameStackCap)
            : this(new[] { firstPlayer, secondPlayer }, initialMoney, smallBlind, cashGameStackCap)
        {
            if (firstPlayer == null)
            {
                throw new ArgumentNullException(nameof(firstPlayer));
            }

            if (secondPlayer == null)
            {
                throw new ArgumentNullException(nameof(secondPlayer));
            }

            // Ensure the players have unique names
            if (firstPlayer.Name == secondPlayer.Name)
            {
                throw new ArgumentException($"Both players have the same name: \"{firstPlayer.Name}\"");
            }
        }

        public TexasHoldemGame(IList<IPlayer> players, int initialMoney = 200, int smallBlind = DefaultSmallBlind, int cashGameStackCap = DefaultCashGameStackCap)
            : this((ICollection<IPlayer>)players, initialMoney, smallBlind, cashGameStackCap)
        {
            // Ensure the players have unique names
            var duplicateNames = players.GroupBy(x => x)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key.Name);

            if (duplicateNames.Count() > 0)
            {
                throw new ArgumentException($"Players have the same name: \"{string.Join(" ", duplicateNames.ToArray())}\"");
            }
        }

        private TexasHoldemGame(ICollection<IPlayer> players, int initialMoney = 1000, int smallBlind = DefaultSmallBlind, int cashGameStackCap = DefaultCashGameStackCap)
        {
            if (players == null)
            {
                throw new ArgumentNullException(nameof(players));
            }

            if (players.Count < 2 || players.Count > 10)
            {
                throw new ArgumentOutOfRangeException(nameof(players), "The number of players must be from 2 to 10");
            }

            if (initialMoney <= 0 || initialMoney > 200000)
            {
                throw new ArgumentOutOfRangeException(nameof(initialMoney), "Initial money should be greater than 0 and less than 200000");
            }

            if (smallBlind <= 0 || smallBlind > 200000)
            {
                throw new ArgumentOutOfRangeException(nameof(smallBlind), "Small blind should be greater than 0 and less than 200000");
            }

            if (cashGameStackCap <= 0 || cashGameStackCap > 200000)
            {
                throw new ArgumentOutOfRangeException(nameof(cashGameStackCap), "Stack cap should be greater than 0 and less than 200000");
            }

            this.allPlayers = new List<InternalPlayer>(players.Count);
            foreach (var item in players)
            {
                this.allPlayers.Add(new InternalPlayer(item));
            }

            this.initialMoney = initialMoney;
            this.smallBlind = smallBlind;
            this.cashGameStackCap = cashGameStackCap;
            this.HandsPlayed = 0;
        }

        public int HandsPlayed { get; private set; }

        public IPlayer Start()
        {
            var playerNames = this.allPlayers.Select(x => x.Name).ToList().AsReadOnly();
            foreach (var player in this.allPlayers)
            {
                player.StartGame(new StartGameContext(playerNames, player.BuyIn == -1 ? this.initialMoney : player.BuyIn));
            }

            this.PlayGame();

            var winner = this.allPlayers.WithMoney().FirstOrDefault();
            foreach (var player in this.allPlayers)
            {
                player.EndGame(new EndGameContext(winner.Name));
            }

            return winner;
        }

        private void Rebuy()
        {
            var playerNames = this.allPlayers.Select(x => x.Name).ToList().AsReadOnly();
            foreach (var player in this.allPlayers)
            {
                if (player.PlayerMoney.Money <= 0)
                {
                    player.StartGame(new StartGameContext(playerNames, player.BuyIn == -1 ? this.initialMoney : player.BuyIn));
                }
            }
        }

        private void PlayGame()
        {
            var shifted = this.allPlayers.ToList();

            // While at least two players have money
            while (this.allPlayers.WithMoney().Count() > 1)
            {
                this.HandsPlayed++;

                var smallBlind = this.smallBlind;

                // Players are shifted in order of priority to make a move
                shifted = shifted.WithMoney().ToList();
                shifted.Add(shifted.First());
                shifted.RemoveAt(0);

                // Rotate players
                IHandLogic hand = new HandLogic(shifted, this.HandsPlayed, smallBlind);

                this.ApplyCashGameStackCap();

                hand.Play();

                this.Rebuy();
            }
        }

        private void ApplyCashGameStackCap()
        {
            foreach (var player in this.allPlayers)
            {
                if (this.IsHumanPlayer(player))
                {
                    continue;
                }

                if (player.PlayerMoney.Money > this.cashGameStackCap)
                {
                    player.PlayerMoney.Money = this.cashGameStackCap;
                }
            }
        }

        private bool IsHumanPlayer(InternalPlayer player)
        {
            return string.Equals(player.Name, "You", StringComparison.OrdinalIgnoreCase);
        }
    }
}
