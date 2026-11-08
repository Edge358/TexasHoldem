namespace TexasHoldem.AI.SmartPlayer.Models
{
    using System;

    public sealed class PlayerProfile
    {
        public PlayerProfile(
            PlayerArchetype archetype,
            int skillLevel,
            double tightness,
            double aggression,
            double bluffRate,
            double discipline,
            double positionSkill,
            double trapRate,
            double riskTolerance)
        {
            this.Archetype = archetype;
            this.SkillLevel = ClampSkill(skillLevel);
            this.Tightness = Clamp01(tightness);
            this.Aggression = Clamp01(aggression);
            this.BluffRate = Clamp01(bluffRate);
            this.Discipline = Clamp01(discipline);
            this.PositionSkill = Clamp01(positionSkill);
            this.TrapRate = Clamp01(trapRate);
            this.RiskTolerance = Clamp01(riskTolerance);
        }

        public PlayerArchetype Archetype { get; }

        public int SkillLevel { get; }

        public double Tightness { get; }

        public double Aggression { get; }

        public double BluffRate { get; }

        public double Discipline { get; }

        public double PositionSkill { get; }

        public double TrapRate { get; }

        public double RiskTolerance { get; }

        public static PlayerProfile CreateNit(int skillLevel = 6)
        {
            return new PlayerProfile(
                PlayerArchetype.Nit,
                skillLevel,
                tightness: 0.85,
                aggression: 0.2,
                bluffRate: 0.05,
                discipline: 0.8,
                positionSkill: 0.6,
                trapRate: 0.2,
                riskTolerance: 0.25);
        }

        public static PlayerProfile CreateRock(int skillLevel = 6)
        {
            return new PlayerProfile(
                PlayerArchetype.Rock,
                skillLevel,
                tightness: 0.92,
                aggression: 0.1,
                bluffRate: 0.03,
                discipline: 0.9,
                positionSkill: 0.65,
                trapRate: 0.25,
                riskTolerance: 0.15);
        }

        public static PlayerProfile CreateCallingStation(int skillLevel = 5)
        {
            return new PlayerProfile(
                PlayerArchetype.CallingStation,
                skillLevel,
                tightness: 0.3,
                aggression: 0.15,
                bluffRate: 0.05,
                discipline: 0.3,
                positionSkill: 0.4,
                trapRate: 0.1,
                riskTolerance: 0.6);
        }

        public static PlayerProfile CreateLag(int skillLevel = 6)
        {
            return new PlayerProfile(
                PlayerArchetype.LAG,
                skillLevel,
                tightness: 0.3,
                aggression: 0.8,
                bluffRate: 0.35,
                discipline: 0.4,
                positionSkill: 0.6,
                trapRate: 0.15,
                riskTolerance: 0.7);
        }

        public static PlayerProfile CreateManiac(int skillLevel = 5)
        {
            return new PlayerProfile(
                PlayerArchetype.Maniac,
                skillLevel,
                tightness: 0.15,
                aggression: 0.95,
                bluffRate: 0.5,
                discipline: 0.2,
                positionSkill: 0.45,
                trapRate: 0.05,
                riskTolerance: 0.85);
        }

        public static PlayerProfile CreateTag(int skillLevel = 7)
        {
            return new PlayerProfile(
                PlayerArchetype.TAG,
                skillLevel,
                tightness: 0.6,
                aggression: 0.6,
                bluffRate: 0.2,
                discipline: 0.7,
                positionSkill: 0.7,
                trapRate: 0.2,
                riskTolerance: 0.5);
        }

        public static PlayerProfile CreateReg(int skillLevel = 7)
        {
            return new PlayerProfile(
                PlayerArchetype.Reg,
                skillLevel,
                tightness: 0.5,
                aggression: 0.5,
                bluffRate: 0.2,
                discipline: 0.65,
                positionSkill: 0.7,
                trapRate: 0.2,
                riskTolerance: 0.5);
        }

        public static PlayerProfile CreateWeakTight(int skillLevel = 4)
        {
            return new PlayerProfile(
                PlayerArchetype.WeakTight,
                skillLevel,
                tightness: 0.75,
                aggression: 0.2,
                bluffRate: 0.05,
                discipline: 0.4,
                positionSkill: 0.4,
                trapRate: 0.05,
                riskTolerance: 0.2);
        }

        public static PlayerProfile CreateAggroFish(int skillLevel = 4)
        {
            return new PlayerProfile(
                PlayerArchetype.AggroFish,
                skillLevel,
                tightness: 0.25,
                aggression: 0.7,
                bluffRate: 0.25,
                discipline: 0.2,
                positionSkill: 0.35,
                trapRate: 0.05,
                riskTolerance: 0.75);
        }

        private static int ClampSkill(int skillLevel)
        {
            return Math.Max(1, Math.Min(10, skillLevel));
        }

        private static double Clamp01(double value)
        {
            if (value < 0)
            {
                return 0;
            }

            if (value > 1)
            {
                return 1;
            }

            return value;
        }
    }
}
