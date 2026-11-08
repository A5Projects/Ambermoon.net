using Ambermoon.Data.Enumerations;

namespace Ambermoon.Data
{
    // Base gains from the bundled Original 1.22 party data. Random 50-100%
    // gains and the existing intelligence bonus still apply in PartyMember.
    // Restrict this to known companions; Advanced-only characters keep their data.
    internal readonly record struct OriginalPartyGrowth(uint HP, uint SP, uint SLP, uint TP)
    {
        public static OriginalPartyGrowth? For(PartyMember member, Features? features)
        {
            if (features == null || !ImmersiveBalance.UsesRelaxedBalance(features.Value))
                return null;

            return (member.Index, member.Class) switch
            {
                (1, Class.Adventurer) => new(10, 8, 5, 6),
                (7, Class.Mage) => new(6, 16, 10, 10),
                (8, Class.Warrior) => new(16, 0, 0, 8),
                (9, Class.Thief) => new(8, 0, 0, 14),
                (10, Class.Mage) => new(6, 16, 10, 10),
                (11, Class.Healer) => new(6, 16, 10, 10),
                (12, Class.Ranger) => new(12, 6, 3, 12),
                (13, Class.Mystic) => new(6, 16, 10, 10),
                (14, Class.Alchemist) => new(6, 16, 10, 10),
                (15, Class.Paladin) => new(14, 6, 3, 10),
                _ => null
            };
        }
    }
}
