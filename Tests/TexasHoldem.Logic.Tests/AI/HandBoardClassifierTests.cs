namespace TexasHoldem.Logic.Tests.AI
{
    using System.Collections.Generic;

    using TexasHoldem.AI.SmartPlayer.Decision;
    using TexasHoldem.AI.SmartPlayer.Models;
    using TexasHoldem.Logic.Cards;
    using Xunit;

    public class HandBoardClassifierTests
    {
        // Mapping note: Overpairs and top pair w/ good kicker are Strong. Straights/flushes/full houses/quads/sets are Monster.
        [Theory]
        [MemberData(nameof(Cases))]
        public void Classify_ReturnsExpectedValues(TestCase testCase)
        {
            var classifier = new HandBoardClassifier();
            var result = classifier.Classify(testCase.HoleCards, testCase.BoardCards);

            Assert.Equal(testCase.ExpectedMadeHand, result.MadeHandClass);
            Assert.Equal(testCase.ExpectedDraw, result.DrawClass);
            Assert.Equal(testCase.ExpectedTexture, result.BoardTexture);
        }

        public static IEnumerable<object[]> Cases()
        {
            yield return new object[]
            {
                new TestCase(
                    "Flop_Set_33_on_K38",
                    Hole(C(CardType.Three, CardSuit.Club), C(CardType.Three, CardSuit.Diamond)),
                    Board(C(CardType.King, CardSuit.Club), C(CardType.Three, CardSuit.Heart), C(CardType.Eight, CardSuit.Diamond)),
                    MadeHandClass.Monster,
                    DrawClass.None,
                    BoardTexture.Dry),
            };
            yield return new object[]
            {
                new TestCase(
                    "Turn_FullHouse_88_on_K38_3Q",
                    Hole(C(CardType.Eight, CardSuit.Club), C(CardType.Eight, CardSuit.Diamond)),
                    Board(C(CardType.King, CardSuit.Club), C(CardType.Three, CardSuit.Heart), C(CardType.Eight, CardSuit.Spade), C(CardType.Three, CardSuit.Diamond), C(CardType.Queen, CardSuit.Heart)),
                    MadeHandClass.Monster,
                    DrawClass.None,
                    BoardTexture.SemiWet),
            };
            yield return new object[]
            {
                new TestCase(
                    "Quads_OnBoard_KKKK2",
                    Hole(C(CardType.Ace, CardSuit.Club), C(CardType.Ace, CardSuit.Diamond)),
                    Board(C(CardType.King, CardSuit.Club), C(CardType.King, CardSuit.Heart), C(CardType.King, CardSuit.Diamond), C(CardType.King, CardSuit.Spade), C(CardType.Two, CardSuit.Diamond)),
                    MadeHandClass.Monster,
                    DrawClass.None,
                    BoardTexture.SemiWet),
            };
            yield return new object[]
            {
                new TestCase(
                    "Overpair_AA_on_K72",
                    Hole(C(CardType.Ace, CardSuit.Spade), C(CardType.Ace, CardSuit.Diamond)),
                    Board(C(CardType.King, CardSuit.Club), C(CardType.Seven, CardSuit.Heart), C(CardType.Two, CardSuit.Diamond)),
                    MadeHandClass.Strong,
                    DrawClass.None,
                    BoardTexture.Dry),
            };
            yield return new object[]
            {
                new TestCase(
                    "TopPairGoodKicker_AK_on_K72",
                    Hole(C(CardType.Ace, CardSuit.Spade), C(CardType.King, CardSuit.Diamond)),
                    Board(C(CardType.King, CardSuit.Club), C(CardType.Seven, CardSuit.Heart), C(CardType.Two, CardSuit.Diamond)),
                    MadeHandClass.Strong,
                    DrawClass.None,
                    BoardTexture.Dry),
            };
            yield return new object[]
            {
                new TestCase(
                    "TopPairWeakKicker_K9_on_K72",
                    Hole(C(CardType.King, CardSuit.Spade), C(CardType.Nine, CardSuit.Diamond)),
                    Board(C(CardType.King, CardSuit.Club), C(CardType.Seven, CardSuit.Heart), C(CardType.Two, CardSuit.Diamond)),
                    MadeHandClass.Medium,
                    DrawClass.None,
                    BoardTexture.Dry),
            };
            yield return new object[]
            {
                new TestCase(
                    "SecondPair_8x_on_K82",
                    Hole(C(CardType.Eight, CardSuit.Spade), C(CardType.Five, CardSuit.Diamond)),
                    Board(C(CardType.King, CardSuit.Club), C(CardType.Eight, CardSuit.Heart), C(CardType.Two, CardSuit.Diamond)),
                    MadeHandClass.Medium,
                    DrawClass.None,
                    BoardTexture.Dry),
            };
            yield return new object[]
            {
                new TestCase(
                    "AceHigh_Weak_As7_on_K92",
                    Hole(C(CardType.Ace, CardSuit.Spade), C(CardType.Seven, CardSuit.Diamond)),
                    Board(C(CardType.King, CardSuit.Club), C(CardType.Nine, CardSuit.Heart), C(CardType.Two, CardSuit.Diamond)),
                    MadeHandClass.Weak,
                    DrawClass.None,
                    BoardTexture.Dry),
            };
            yield return new object[]
            {
                new TestCase(
                    "Air_75_on_K92",
                    Hole(C(CardType.Seven, CardSuit.Spade), C(CardType.Five, CardSuit.Diamond)),
                    Board(C(CardType.King, CardSuit.Club), C(CardType.Nine, CardSuit.Heart), C(CardType.Two, CardSuit.Diamond)),
                    MadeHandClass.Air,
                    DrawClass.None,
                    BoardTexture.Dry),
            };
            yield return new object[]
            {
                new TestCase(
                    "TwoPair_K8_on_K82",
                    Hole(C(CardType.King, CardSuit.Diamond), C(CardType.Eight, CardSuit.Spade)),
                    Board(C(CardType.King, CardSuit.Club), C(CardType.Eight, CardSuit.Heart), C(CardType.Two, CardSuit.Diamond)),
                    MadeHandClass.Strong,
                    DrawClass.None,
                    BoardTexture.Dry),
            };
            yield return new object[]
            {
                new TestCase(
                    "Trips_FromBoardPair_AK_on_KK7",
                    Hole(C(CardType.Ace, CardSuit.Spade), C(CardType.King, CardSuit.Diamond)),
                    Board(C(CardType.King, CardSuit.Club), C(CardType.King, CardSuit.Heart), C(CardType.Seven, CardSuit.Diamond)),
                    MadeHandClass.Strong,
                    DrawClass.None,
                    BoardTexture.Dry),
            };
            yield return new object[]
            {
                new TestCase(
                    "Straight_98_on_765",
                    Hole(C(CardType.Nine, CardSuit.Spade), C(CardType.Eight, CardSuit.Diamond)),
                    Board(C(CardType.Seven, CardSuit.Club), C(CardType.Six, CardSuit.Heart), C(CardType.Five, CardSuit.Diamond)),
                    MadeHandClass.Monster,
                    DrawClass.None,
                    BoardTexture.SemiWet),
            };
            yield return new object[]
            {
                new TestCase(
                    "FlushDraw_A9c_on_Kc7c2d",
                    Hole(C(CardType.Ace, CardSuit.Club), C(CardType.Nine, CardSuit.Club)),
                    Board(C(CardType.King, CardSuit.Club), C(CardType.Seven, CardSuit.Club), C(CardType.Two, CardSuit.Diamond)),
                    MadeHandClass.Weak,
                    DrawClass.FD,
                    BoardTexture.SemiWet),
            };
            yield return new object[]
            {
                new TestCase(
                    "OESD_98_on_762",
                    Hole(C(CardType.Nine, CardSuit.Spade), C(CardType.Eight, CardSuit.Diamond)),
                    Board(C(CardType.Seven, CardSuit.Club), C(CardType.Six, CardSuit.Heart), C(CardType.Two, CardSuit.Diamond)),
                    MadeHandClass.Air,
                    DrawClass.OESD,
                    BoardTexture.Dry),
            };
            yield return new object[]
            {
                new TestCase(
                    "Gutshot_96_on_8T2",
                    Hole(C(CardType.Nine, CardSuit.Spade), C(CardType.Six, CardSuit.Diamond)),
                    Board(C(CardType.Eight, CardSuit.Club), C(CardType.Ten, CardSuit.Heart), C(CardType.Two, CardSuit.Diamond)),
                    MadeHandClass.Air,
                    DrawClass.Gutshot,
                    BoardTexture.Dry),
            };
            yield return new object[]
            {
                new TestCase(
                    "TwoOver_AK_on_Q72",
                    Hole(C(CardType.Ace, CardSuit.Spade), C(CardType.King, CardSuit.Diamond)),
                    Board(C(CardType.Queen, CardSuit.Club), C(CardType.Seven, CardSuit.Heart), C(CardType.Two, CardSuit.Diamond)),
                    MadeHandClass.Weak,
                    DrawClass.TwoOver,
                    BoardTexture.Dry),
            };
            yield return new object[]
            {
                new TestCase(
                    "Combo_AsKs_on_QsJs2d",
                    Hole(C(CardType.Ace, CardSuit.Spade), C(CardType.King, CardSuit.Spade)),
                    Board(C(CardType.Queen, CardSuit.Spade), C(CardType.Jack, CardSuit.Spade), C(CardType.Two, CardSuit.Diamond)),
                    MadeHandClass.Weak,
                    DrawClass.Combo,
                    BoardTexture.SemiWet),
            };
            yield return new object[]
            {
                new TestCase(
                    "NutFlush_AsKs_on_QsJs2s",
                    Hole(C(CardType.Ace, CardSuit.Spade), C(CardType.King, CardSuit.Spade)),
                    Board(C(CardType.Queen, CardSuit.Spade), C(CardType.Jack, CardSuit.Spade), C(CardType.Two, CardSuit.Spade)),
                    MadeHandClass.Monster,
                    DrawClass.None,
                    BoardTexture.Wet),
            };
            yield return new object[]
            {
                new TestCase(
                    "WeakFlush_98s_on_QsJs2s",
                    Hole(C(CardType.Nine, CardSuit.Spade), C(CardType.Eight, CardSuit.Spade)),
                    Board(C(CardType.Queen, CardSuit.Spade), C(CardType.Jack, CardSuit.Spade), C(CardType.Two, CardSuit.Spade)),
                    MadeHandClass.Strong,
                    DrawClass.None,
                    BoardTexture.Wet),
            };
            yield return new object[]
            {
                new TestCase(
                    "NutStraight_AK_on_9TJQK",
                    Hole(C(CardType.Ace, CardSuit.Spade), C(CardType.King, CardSuit.Diamond)),
                    Board(C(CardType.Nine, CardSuit.Club), C(CardType.Ten, CardSuit.Club), C(CardType.Jack, CardSuit.Diamond), C(CardType.Queen, CardSuit.Heart), C(CardType.King, CardSuit.Club)),
                    MadeHandClass.Monster,
                    DrawClass.None,
                    BoardTexture.Wet),
            };
            yield return new object[]
            {
                new TestCase(
                    "FullHouse_22_on_KK279",
                    Hole(C(CardType.Two, CardSuit.Club), C(CardType.Two, CardSuit.Diamond)),
                    Board(C(CardType.King, CardSuit.Club), C(CardType.King, CardSuit.Diamond), C(CardType.Two, CardSuit.Heart), C(CardType.Seven, CardSuit.Spade), C(CardType.Nine, CardSuit.Diamond)),
                    MadeHandClass.Monster,
                    DrawClass.None,
                    BoardTexture.SemiWet),
            };
            yield return new object[]
            {
                new TestCase(
                    "PocketPair_44_on_K72",
                    Hole(C(CardType.Four, CardSuit.Spade), C(CardType.Four, CardSuit.Diamond)),
                    Board(C(CardType.King, CardSuit.Club), C(CardType.Seven, CardSuit.Heart), C(CardType.Two, CardSuit.Diamond)),
                    MadeHandClass.Weak,
                    DrawClass.None,
                    BoardTexture.Dry),
            };
            yield return new object[]
            {
                new TestCase(
                    "TopPairGoodKicker_AJ_on_J72",
                    Hole(C(CardType.Ace, CardSuit.Spade), C(CardType.Jack, CardSuit.Diamond)),
                    Board(C(CardType.Jack, CardSuit.Club), C(CardType.Seven, CardSuit.Heart), C(CardType.Two, CardSuit.Diamond)),
                    MadeHandClass.Strong,
                    DrawClass.None,
                    BoardTexture.Dry),
            };
            yield return new object[]
            {
                new TestCase(
                    "TopPairWeakKicker_J9_on_J72",
                    Hole(C(CardType.Jack, CardSuit.Spade), C(CardType.Nine, CardSuit.Diamond)),
                    Board(C(CardType.Jack, CardSuit.Club), C(CardType.Seven, CardSuit.Heart), C(CardType.Two, CardSuit.Diamond)),
                    MadeHandClass.Medium,
                    DrawClass.None,
                    BoardTexture.Dry),
            };
            yield return new object[]
            {
                new TestCase(
                    "BoardTexture_SemiWet_KQ2_twoTone",
                    Hole(C(CardType.Ace, CardSuit.Spade), C(CardType.Nine, CardSuit.Diamond)),
                    Board(C(CardType.King, CardSuit.Club), C(CardType.Queen, CardSuit.Club), C(CardType.Two, CardSuit.Diamond)),
                    MadeHandClass.Weak,
                    DrawClass.None,
                    BoardTexture.SemiWet),
            };
            yield return new object[]
            {
                new TestCase(
                    "BoardTexture_Wet_987_twoTone",
                    Hole(C(CardType.Two, CardSuit.Club), C(CardType.Three, CardSuit.Diamond)),
                    Board(C(CardType.Nine, CardSuit.Club), C(CardType.Eight, CardSuit.Club), C(CardType.Seven, CardSuit.Diamond)),
                    MadeHandClass.Air,
                    DrawClass.None,
                    BoardTexture.Wet),
            };
            yield return new object[]
            {
                new TestCase(
                    "River_Air_75_on_K982Q",
                    Hole(C(CardType.Seven, CardSuit.Spade), C(CardType.Five, CardSuit.Diamond)),
                    Board(C(CardType.King, CardSuit.Club), C(CardType.Nine, CardSuit.Heart), C(CardType.Two, CardSuit.Diamond), C(CardType.Eight, CardSuit.Spade), C(CardType.Queen, CardSuit.Heart)),
                    MadeHandClass.Air,
                    DrawClass.None,
                    BoardTexture.Wet),
            };
            yield return new object[]
            {
                new TestCase(
                    "River_TripsFromBoardPair_AK_on_KK78Q",
                    Hole(C(CardType.Ace, CardSuit.Spade), C(CardType.King, CardSuit.Diamond)),
                    Board(C(CardType.King, CardSuit.Club), C(CardType.King, CardSuit.Heart), C(CardType.Seven, CardSuit.Diamond), C(CardType.Eight, CardSuit.Spade), C(CardType.Queen, CardSuit.Heart)),
                    MadeHandClass.Strong,
                    DrawClass.None,
                    BoardTexture.SemiWet),
            };
            yield return new object[]
            {
                new TestCase(
                    "River_WeakPair_9x_on_K982Q",
                    Hole(C(CardType.Nine, CardSuit.Spade), C(CardType.Five, CardSuit.Diamond)),
                    Board(C(CardType.King, CardSuit.Club), C(CardType.Nine, CardSuit.Heart), C(CardType.Two, CardSuit.Diamond), C(CardType.Eight, CardSuit.Spade), C(CardType.Queen, CardSuit.Heart)),
                    MadeHandClass.Weak,
                    DrawClass.None,
                    BoardTexture.Wet),
            };
            yield return new object[]
            {
                new TestCase(
                    "DryBoard_AK_on_K72",
                    Hole(C(CardType.Ace, CardSuit.Spade), C(CardType.King, CardSuit.Diamond)),
                    Board(C(CardType.King, CardSuit.Club), C(CardType.Seven, CardSuit.Heart), C(CardType.Two, CardSuit.Diamond)),
                    MadeHandClass.Strong,
                    DrawClass.None,
                    BoardTexture.Dry),
            };
            yield return new object[]
            {
                new TestCase(
                    "SemiWetBoard_AQ_on_Kc7c2d",
                    Hole(C(CardType.Ace, CardSuit.Spade), C(CardType.Queen, CardSuit.Diamond)),
                    Board(C(CardType.King, CardSuit.Club), C(CardType.Seven, CardSuit.Club), C(CardType.Two, CardSuit.Diamond)),
                    MadeHandClass.Weak,
                    DrawClass.None,
                    BoardTexture.SemiWet),
            };
            yield return new object[]
            {
                new TestCase(
                    "WetBoard_AQ_on_KQJ_twoTone",
                    Hole(C(CardType.Ace, CardSuit.Spade), C(CardType.Queen, CardSuit.Diamond)),
                    Board(C(CardType.King, CardSuit.Club), C(CardType.Queen, CardSuit.Club), C(CardType.Jack, CardSuit.Diamond)),
                    MadeHandClass.Medium,
                    DrawClass.Gutshot,
                    BoardTexture.Wet),
            };
        }

        private static IReadOnlyCollection<Card> Hole(Card first, Card second)
        {
            return new List<Card> { first, second };
        }

        private static IReadOnlyCollection<Card> Board(params Card[] cards)
        {
            return new List<Card>(cards);
        }

        private static Card C(CardType type, CardSuit suit)
        {
            return new Card(suit, type);
        }

        public sealed class TestCase
        {
            public TestCase(
                string name,
                IReadOnlyCollection<Card> holeCards,
                IReadOnlyCollection<Card> boardCards,
                MadeHandClass expectedMadeHand,
                DrawClass expectedDraw,
                BoardTexture expectedTexture)
            {
                this.Name = name;
                this.HoleCards = holeCards;
                this.BoardCards = boardCards;
                this.ExpectedMadeHand = expectedMadeHand;
                this.ExpectedDraw = expectedDraw;
                this.ExpectedTexture = expectedTexture;
            }

            public string Name { get; }

            public IReadOnlyCollection<Card> HoleCards { get; }

            public IReadOnlyCollection<Card> BoardCards { get; }

            public MadeHandClass ExpectedMadeHand { get; }

            public DrawClass ExpectedDraw { get; }

            public BoardTexture ExpectedTexture { get; }

            public override string ToString()
            {
                return this.Name;
            }
        }
    }
}
