namespace TexasHoldem.AI.SmartPlayer.Models
{
    public sealed class SpotFeatures
    {
        public Street Street { get; set; }

        public int NumPlayersActive { get; set; }

        public int PotSize { get; set; }

        public int AmountToCall { get; set; }

        public int HeroStack { get; set; }

        public int EffectiveStack { get; set; }

        public bool IsHeadsUp { get; set; }

        public int PositionIndex { get; set; }

        public PositionBucket PositionBucket { get; set; }

        public bool IsPair { get; set; }

        public bool IsSuited { get; set; }

        public int HighRank { get; set; }

        public int LowRank { get; set; }

        public int Gap { get; set; }

        public bool IsBroadway { get; set; }

        public bool IsAceX { get; set; }

        public MadeHandClass MadeHandClass { get; set; }

        public DrawClass DrawClass { get; set; }

        public BoardTexture BoardTexture { get; set; }

        public bool IsPairedBoard { get; set; }

        public bool IsMonotone { get; set; }

        public bool IsTwoTone { get; set; }

        public int PreflopStrength { get; set; }

        public PreflopTier PreflopTier { get; set; }

        public int RaiseDepth { get; set; }

        public int ThinValueApplied { get; set; }

        public int ValueCBetApplied { get; set; }

        public int DelayedValueCBetApplied { get; set; }

        public bool WasPreflopAggressor { get; set; }

        public bool FlopWasCheckedThrough { get; set; }
    }

    public enum Street
    {
        Preflop,
        Flop,
        Turn,
        River,
    }

    public enum PositionBucket
    {
        Early,
        Mid,
        Late,
        Blinds,
    }

    public enum MadeHandClass
    {
        Monster,
        Strong,
        Medium,
        Weak,
        Air,
    }

    public enum DrawClass
    {
        None,
        FD,
        OESD,
        Gutshot,
        TwoOver,
        Combo,
    }

    public enum BoardTexture
    {
        Dry,
        SemiWet,
        Wet,
    }

    public enum PreflopTier
    {
        Unknown,
        Premium,
        Strong,
        Medium,
        Weak,
    }
}
