namespace TexasHoldem.AI.SmartPlayer.Decision
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using TexasHoldem.AI.SmartPlayer.Models;
    using TexasHoldem.Logic;
    using TexasHoldem.Logic.Cards;
    using TexasHoldem.Logic.Helpers;

    public sealed class HandBoardClassifier
    {
        public BoardClassification Classify(IReadOnlyCollection<Card> holeCards, IReadOnlyCollection<Card> communityCards)
        {
            var board = communityCards?.ToList() ?? new List<Card>();
            var hole = holeCards?.ToList() ?? new List<Card>();

            if (board.Count < 3)
            {
                return new BoardClassification
                {
                    MadeHandClass = MadeHandClass.Air,
                    DrawClass = DrawClass.None,
                    BoardTexture = BoardTexture.Dry,
                    IsPairedBoard = false,
                    IsMonotone = false,
                    IsTwoTone = false,
                };
            }

            var allCards = hole.Concat(board).ToList();
            var isPairedBoard = HasPairedBoard(board);
            var suitCounts = board.GroupBy(card => card.Suit).ToDictionary(group => group.Key, group => group.Count());
            var isMonotone = suitCounts.Values.Any(count => count >= 3);
            var isTwoTone = !isMonotone && suitCounts.Values.Any(count => count == 2);
            var texture = ClassifyBoardTexture(board, isMonotone, isTwoTone);
            var madeHand = ClassifyMadeHand(allCards, board, hole);
            var drawClass = ClassifyDraw(allCards, board, hole, madeHand);

            return new BoardClassification
            {
                MadeHandClass = madeHand,
                DrawClass = drawClass,
                BoardTexture = texture,
                IsPairedBoard = isPairedBoard,
                IsMonotone = isMonotone,
                IsTwoTone = isTwoTone,
            };
        }

        private static bool HasPairedBoard(IReadOnlyCollection<Card> board)
        {
            return board
                .GroupBy(card => card.Type)
                .Any(group => group.Count() >= 2);
        }

        private static BoardTexture ClassifyBoardTexture(IReadOnlyCollection<Card> board, bool isMonotone, bool isTwoTone)
        {
            var ranks = board.Select(card => (int)card.Type).Distinct().OrderBy(rank => rank).ToList();
            var connected = false;
            for (var i = 0; i + 2 < ranks.Count; i++)
            {
                if (ranks[i + 2] - ranks[i] <= 4)
                {
                    connected = true;
                    break;
                }
            }

            if (isMonotone || (isTwoTone && connected))
            {
                return BoardTexture.Wet;
            }

            if (isTwoTone || connected)
            {
                return BoardTexture.SemiWet;
            }

            return BoardTexture.Dry;
        }

        private static MadeHandClass ClassifyMadeHand(IReadOnlyCollection<Card> allCards, IReadOnlyCollection<Card> board, IReadOnlyCollection<Card> hole)
        {
            var holeRanks = hole.Select(card => (int)card.Type).OrderBy(rank => rank).ToList();
            var boardRanks = board.Select(card => (int)card.Type).OrderBy(rank => rank).ToList();
            var maxBoardRank = boardRanks.Count > 0 ? boardRanks.Max() : 0;

            var hasPocketPair = holeRanks.Count == 2 && holeRanks[0] == holeRanks[1];
            var pocketPairRank = hasPocketPair ? holeRanks[0] : 0;
            var isSet = hasPocketPair && boardRanks.Contains(pocketPairRank);

            if (isSet)
            {
                return MadeHandClass.Monster;
            }

            var rankType = Helpers.GetHandRank(allCards.ToList());
            if (rankType == HandRankType.StraightFlush
                || rankType == HandRankType.FourOfAKind
                || rankType == HandRankType.FullHouse)
            {
                return MadeHandClass.Monster;
            }

            if (rankType == HandRankType.Straight)
            {
                if (IsBoardStraight(board) && !HasHigherStraight(hole, board))
                {
                    return MadeHandClass.Medium;
                }

                return MadeHandClass.Monster;
            }

            if (rankType == HandRankType.Flush)
            {
                return HasTopFlush(hole, board) ? MadeHandClass.Monster : MadeHandClass.Strong;
            }

            var rankCounts = allCards
                .GroupBy(card => (int)card.Type)
                .ToDictionary(group => group.Key, group => group.Count());

            if (rankType == HandRankType.ThreeOfAKind)
            {
                return MadeHandClass.Strong;
            }

            if (rankType == HandRankType.TwoPairs)
            {
                return MadeHandClass.Strong;
            }

            if (rankType == HandRankType.Pair)
            {
                var highestPairRank = rankCounts.Where(entry => entry.Value >= 2).Select(entry => entry.Key).DefaultIfEmpty(0).Max();
                var isPocketPair = holeRanks.Count == 2 && holeRanks[0] == holeRanks[1];
                if (isPocketPair && holeRanks[0] > maxBoardRank)
                {
                    return MadeHandClass.Strong;
                }

                if (boardRanks.Contains(highestPairRank))
                {
                    var isTopPair = highestPairRank == maxBoardRank;
                    var kicker = holeRanks.Where(rank => rank != highestPairRank).DefaultIfEmpty(0).Max();
                    if (isTopPair && kicker >= (int)CardType.Queen)
                    {
                        return MadeHandClass.Strong;
                    }

                    if (isTopPair)
                    {
                        return MadeHandClass.Medium;
                    }
                }

                if (boardRanks.Distinct().Count() > 1)
                {
                    var secondBoardRank = boardRanks.Distinct().OrderByDescending(rank => rank).Skip(1).FirstOrDefault();
                    if (highestPairRank == secondBoardRank)
                    {
                        return MadeHandClass.Medium;
                    }
                }

                return MadeHandClass.Weak;
            }

            if (holeRanks.Count == 2 && holeRanks[1] == (int)CardType.Ace)
            {
                return MadeHandClass.Weak;
            }

            return MadeHandClass.Air;
        }

        private static DrawClass ClassifyDraw(IReadOnlyCollection<Card> allCards, IReadOnlyCollection<Card> board, IReadOnlyCollection<Card> hole, MadeHandClass madeHand)
        {
            if (madeHand == MadeHandClass.Monster)
            {
                return DrawClass.None;
            }

            // No cards to come on the river, so no draw is possible.
            if (board.Count >= 5)
            {
                return DrawClass.None;
            }

            var suitCounts = allCards.GroupBy(card => card.Suit).ToDictionary(group => group.Key, group => group.Count());
            var hasFlush = suitCounts.Values.Any(count => count >= 5);

            // A made flush makes any straight draw irrelevant.
            if (hasFlush)
            {
                return DrawClass.None;
            }
            var hasFlushDraw = !hasFlush && suitCounts.Values.Any(count => count == 4);

            var ranks = allCards.Select(card => (int)card.Type).Distinct().ToList();
            if (ranks.Contains((int)CardType.Ace))
            {
                ranks.Add(1);
            }

            var hasStraight = HasStraight(ranks);
            var hasOesd = false;
            var hasGutshot = false;

            if (!hasStraight)
            {
                for (var start = 1; start <= 10; start++)
                {
                    var present = 0;
                    var missingRank = 0;
                    for (var rank = start; rank < start + 5; rank++)
                    {
                        if (ranks.Contains(rank))
                        {
                            present++;
                        }
                        else
                        {
                            missingRank = rank;
                        }
                    }

                    if (present == 4)
                    {
                        // Four consecutive cards are open-ended only when both ends can still be filled.
                        // J-Q-K-A and A-2-3-4 have one end off the deck, so they are gutshots.
                        var openLow = missingRank == start && start + 5 <= (int)CardType.Ace;
                        var openHigh = missingRank == start + 4 && start - 1 >= 1;
                        if (openLow || openHigh)
                        {
                            hasOesd = true;
                        }
                        else
                        {
                            hasGutshot = true;
                        }
                    }
                }
            }

            var boardRanks = board.Select(card => (int)card.Type).Distinct().ToList();
            var maxBoardRank = boardRanks.Count > 0 ? boardRanks.Max() : 0;
            var holeRanks = hole.Select(card => (int)card.Type).Distinct().ToList();
            var hasTwoOver = holeRanks.Count == 2
                && holeRanks.All(rank => rank > maxBoardRank)
                && !boardRanks.Any(rank => holeRanks.Contains(rank));

            if (hasFlushDraw && (hasOesd || hasGutshot))
            {
                return DrawClass.Combo;
            }

            if (hasFlushDraw)
            {
                return DrawClass.FD;
            }

            if (hasOesd)
            {
                return DrawClass.OESD;
            }

            if (hasGutshot)
            {
                return DrawClass.Gutshot;
            }

            if (hasTwoOver)
            {
                return DrawClass.TwoOver;
            }

            return DrawClass.None;
        }

        private static bool HasTopFlush(IReadOnlyCollection<Card> hole, IReadOnlyCollection<Card> board)
        {
            var suitedCards = hole.Concat(board)
                .GroupBy(card => card.Suit)
                .OrderByDescending(group => group.Count())
                .FirstOrDefault();

            if (suitedCards == null || suitedCards.Count() < 5)
            {
                return false;
            }

            var maxRank = suitedCards.Max(card => (int)card.Type);
            return hole.Any(card => card.Suit == suitedCards.Key && (int)card.Type == maxRank);
        }

        private static bool IsBoardStraight(IReadOnlyCollection<Card> board)
        {
            if (board.Count < 5)
            {
                return false;
            }

            var ranks = board.Select(card => (int)card.Type).Distinct().ToList();
            if (ranks.Contains((int)CardType.Ace))
            {
                ranks.Add(1);
            }

            return HasStraight(ranks);
        }

        private static bool HasHigherStraight(IReadOnlyCollection<Card> hole, IReadOnlyCollection<Card> board)
        {
            var boardHigh = GetStraightHighCard(board.Select(card => (int)card.Type));
            if (boardHigh == 0)
            {
                return false;
            }

            var allRanks = hole.Concat(board).Select(card => (int)card.Type).ToList();
            var heroHigh = GetStraightHighCard(allRanks);
            return heroHigh > boardHigh;
        }

        private static int GetStraightHighCard(IEnumerable<int> ranksInput)
        {
            var ranks = ranksInput.Distinct().ToList();
            if (ranks.Contains((int)CardType.Ace))
            {
                ranks.Add(1);
            }

            var bestHigh = 0;
            for (var start = 1; start <= 10; start++)
            {
                var present = true;
                for (var rank = start; rank < start + 5; rank++)
                {
                    if (!ranks.Contains(rank))
                    {
                        present = false;
                        break;
                    }
                }

                if (present)
                {
                    bestHigh = Math.Max(bestHigh, start + 4);
                }
            }

            return bestHigh;
        }

        private static bool HasStraight(IReadOnlyCollection<int> ranks)
        {
            for (var start = 1; start <= 10; start++)
            {
                var present = true;
                for (var rank = start; rank < start + 5; rank++)
                {
                    if (!ranks.Contains(rank))
                    {
                        present = false;
                        break;
                    }
                }

                if (present)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public sealed class BoardClassification
    {
        public MadeHandClass MadeHandClass { get; set; }

        public DrawClass DrawClass { get; set; }

        public BoardTexture BoardTexture { get; set; }

        public bool IsPairedBoard { get; set; }

        public bool IsMonotone { get; set; }

        public bool IsTwoTone { get; set; }
    }
}
