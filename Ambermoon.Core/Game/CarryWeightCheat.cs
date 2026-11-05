using System;
using Ambermoon.Data;
using Ambermoon.UI;

namespace Ambermoon;

partial class GameCore
{
    uint extraCarryWeightKg;

    /// <summary>
    /// Flat capacity bonus for every companion in this game session. Survives
    /// loading a save, but does not change strength, item weights or saved data.
    /// </summary>
    public uint ExtraCarryWeightKg
    {
        get => extraCarryWeightKg;
        set
        {
            if (value > PartyMember.MaxExtraCarryWeightKg)
                throw new ArgumentOutOfRangeException(nameof(value));

            extraCarryWeightKg = value;
            if (CurrentSavegame == null)
                return;

            ApplyExtraCarryWeight(CurrentSavegame);
            if (!Ingame)
                return;

            // Preserve temporary portrait overlays used by conversations,
            // combat and item dragging; their normal refresh restores status.
            if (!layout.IsDragging && (currentWindow.Window == Window.MapView ||
                currentWindow.Window == Window.Inventory || currentWindow.Window == Window.Stats ||
                currentWindow.Window == Window.Chest))
            {
                foreach (var member in PartyMembers)
                    layout.UpdateCharacterStatus(member);
            }

            if (currentWindow.Window == Window.Inventory && CurrentInventory != null)
                SetInventoryWeightDisplay(CurrentInventory);
            UpdateCharacterInfo();
        }
    }

    void ApplyExtraCarryWeight(Savegame savegame)
    {
        foreach (var member in savegame.PartyMembers.Values)
            member.ExtraCarryWeightKg = extraCarryWeightKg;
    }
}
