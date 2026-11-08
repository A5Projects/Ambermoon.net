using Ambermoon.Data.Enumerations;

namespace Ambermoon.Data
{
    /// <summary>
    /// Runtime rules for the selected game-data edition. Like FoodWeight, these
    /// apply to the active game only and are never stored in a savegame.
    /// </summary>
    public static class ImmersiveBalance
    {
        public static bool Enabled { get; private set; }
        public static uint GoldWeight => Enabled ? 1u : 5u;
        public static uint CarryWeightPerStrength => Enabled ? 4000u : 1000u;

        public static bool UsesRelaxedBalance(Features features) =>
            features.HasFlag(Features.AdjustedEPFactors) && !features.HasFlag(Features.Mod);

        // Set this for every game, including Original after an Advanced session.
        public static void Configure(Features features) => Enabled = UsesRelaxedBalance(features);

        public static bool IsUnbreakableWeapon(Item item) => Enabled &&
            item.Index >= 181 && item.Index <= 201 &&
            (item.Type == ItemType.CloseRangeWeapon || item.Type == ItemType.LongRangeWeapon);
    }
}
