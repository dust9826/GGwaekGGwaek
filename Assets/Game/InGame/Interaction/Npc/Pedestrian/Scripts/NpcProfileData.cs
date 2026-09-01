using System;

namespace PPack
{
    public enum ENpcAppearanceSlot
    {
        Body,
        Face,
        Hair,
        Top,
        Coat,
        Pants,
        Shoes,
        Hat,
    }

    [Serializable]
    public struct NpcAppearanceData : IEquatable<NpcAppearanceData>
    {
        public int BodyId;
        public int FaceId;
        public int HairId;
        public int TopId;
        public int CoatId;
        public int PantsId;
        public int ShoesId;
        public int HatId;

        public int GetId(ENpcAppearanceSlot slot)
        {
            return slot switch {
                ENpcAppearanceSlot.Body => BodyId,
                ENpcAppearanceSlot.Face => FaceId,
                ENpcAppearanceSlot.Hair => HairId,
                ENpcAppearanceSlot.Top => TopId,
                ENpcAppearanceSlot.Coat => CoatId,
                ENpcAppearanceSlot.Pants => PantsId,
                ENpcAppearanceSlot.Shoes => ShoesId,
                ENpcAppearanceSlot.Hat => HatId,
                _ => 0,
            };
        }

        public void SetId(ENpcAppearanceSlot slot, int id)
        {
            switch (slot) {
                case ENpcAppearanceSlot.Body: BodyId = id; break;
                case ENpcAppearanceSlot.Face: FaceId = id; break;
                case ENpcAppearanceSlot.Hair: HairId = id; break;
                case ENpcAppearanceSlot.Top: TopId = id; break;
                case ENpcAppearanceSlot.Coat: CoatId = id; break;
                case ENpcAppearanceSlot.Pants: PantsId = id; break;
                case ENpcAppearanceSlot.Shoes: ShoesId = id; break;
                case ENpcAppearanceSlot.Hat: HatId = id; break;
            }
        }

        public bool Equals(NpcAppearanceData other)
        {
            foreach (ENpcAppearanceSlot slot in Enum.GetValues(typeof(ENpcAppearanceSlot))) {
                if (GetId(slot) != other.GetId(slot)) return false;
            }
            return true;
        }

        public override bool Equals(object obj) => obj is NpcAppearanceData other && Equals(other);

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            foreach (ENpcAppearanceSlot slot in Enum.GetValues(typeof(ENpcAppearanceSlot))) {
                hash.Add(GetId(slot));
            }
            return hash.ToHashCode();
        }
    }

    [Serializable]
    public struct NpcProfileData
    {
        public int NpcId;
        public int GenerationSeed;
        public ENpcTemperament Temperament;
        public NpcAppearanceData Appearance;

        public NpcProfileData(int npcId, int generationSeed, ENpcTemperament temperament,
            NpcAppearanceData appearance)
        {
            NpcId = npcId;
            GenerationSeed = generationSeed;
            Temperament = temperament;
            Appearance = appearance;
        }
    }
}
