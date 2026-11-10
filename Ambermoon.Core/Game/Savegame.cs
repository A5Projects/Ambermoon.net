using System;
using System.Collections.Generic;
using System.Linq;
using Ambermoon.Data;
using Ambermoon.Data.Serialization;
using Ambermoon.Render;

namespace Ambermoon;

partial class GameCore
{
    public const int NumBaseSavegameSlots = 10;
    internal Savegame? CurrentSavegame { get; private set; }
    internal ISavegameManager SavegameManager { get; }
    private protected readonly ISavegameSerializer savegameSerializer;
    internal IEnumerable<string> AdditionalSavegameNames => Provider_AdditionalSavegameNames();

    public Savegame? GetCurrentSavegame()
    {
        return CurrentSavegame;
    }

    internal void SetAdditionalSavegamesContinueSlot(int slot)
    {
        Provider_ContinueGameSlotUpdater()?.Invoke(slot);
    }

    const string QuickSaveName = "QuickSave F7";

    // Use an explicitly named quicksave, or claim an empty slot at the end of
    // the enabled list. Never silently replace a player's normal save.
    static int FindQuickSaveSlot(string[] names, Func<int, bool> slotExists)
    {
        int existing = Array.FindIndex(names, name => name == QuickSaveName);
        if (existing >= 0)
            return existing + 1;

        for (int index = names.Length - 1; index >= 0; --index)
        {
            if (string.IsNullOrWhiteSpace(names[index]) && !slotExists(index + 1))
                return index + 1;
        }
        return 0;
    }

    void QuickSave()
    {
        // Saving is only safe during normal exploration, including when its
        // non-modal action grid is visible. Do not save half-finished actions.
        if (!IdleOnMap || !mouseLookFocused || mouseLookFullscreenChanging ||
            pickingNewLeader || pickingTargetPlayer || pickingTargetInventory ||
            layout.IsDragging || layout.OptionMenuOpen || worldMapViewer != null || QuestLog?.Open == true ||
            nextClickHandler != null || GameOverButtonsVisible || UI.TextInput.FocusedInput != null)
            return;

        var names = SavegameManager.GetSavegameNames(renderView.GameData, out _, NumBaseSavegameSlots);
        if (CoreConfiguration.ExtendedSavegameSlots)
            names = names.Concat(AdditionalSavegameNames).ToArray();

        int slot = FindQuickSaveSlot(names, slot => SavegameManager.SaveSlotExists(renderView.GameData, slot));
        if (slot == 0)
        {
            string message = GameLanguage switch
            {
                GameLanguage.German => "Kein freier Speicherplatz für F7.",
                GameLanguage.French => "Aucun emplacement libre pour F7.",
                GameLanguage.Polish => "Brak wolnego miejsca zapisu dla F7.",
                GameLanguage.Czech => "Neni volne misto pro ulozeni F7.",
                _ => "No free save slot for F7."
            };
            ShowBriefMessagePopup(message, TimeSpan.FromMilliseconds(2000));
            return;
        }

        SaveGame(slot, QuickSaveName);
        SetAdditionalSavegamesContinueSlot(slot);
        if (CoreConfiguration.ShowSaveLoadMessage)
        {
            ShowBriefMessagePopup(
                string.Format(CustomTexts.GetText(GameLanguage, CustomTexts.Index.GameSaved), QuickSaveName),
                TimeSpan.FromMilliseconds(1500));
        }
    }

    private protected void FixSavegameValues(Savegame savegame)
    {
        foreach (var member in savegame.PartyMembers.Values)
        {
            uint weight = 0;

            // Add gold and food
            weight += member.Gold * Character.GoldWeight;
            weight += member.Food * Character.FoodWeight;

            // Add items
            foreach (var itemSlot in member.Inventory.Slots)
            {
                if (itemSlot == null || itemSlot.ItemIndex == 0)
                    continue;

                var item = ItemManager.GetItem(itemSlot.ItemIndex);

                weight += (uint)itemSlot.Amount * item.Weight;
            }

            foreach (var itemSlot in member.Equipment.Slots.Values)
            {
                if (itemSlot == null || itemSlot.ItemIndex == 0)
                    continue;

                var item = ItemManager.GetItem(itemSlot.ItemIndex);

                weight += (uint)itemSlot.Amount * item.Weight;
            }

            member.TotalWeight = weight;
        }
    }

    void RunSavegameTileChangeEvents(uint mapIndex)
    {
        if (CurrentSavegame!.TileChangeEvents.ContainsKey(mapIndex))
        {
            var tileChangeEvents = CurrentSavegame.TileChangeEvents[mapIndex];

            foreach (var tileChangeEvent in tileChangeEvents)
                UpdateMapTile(tileChangeEvent, null, null, false);
        }
    }

    public void LoadGame(int slot, bool showError = false, bool loadInitialOnError = false,
        Action<Action>? preLoadAction = null, bool exitWhenFailing = true, Action<int>? postAction = null,
        bool updateSlot = false)
    {
        void Failed()
        {
            if (exitWhenFailing)
                Exit();
            else
                ClosePopup();
        }

        int totalSavegames = Provider_NumSavegameSlots();

        var savegame = SavegameManager.Load(renderView.GameData, savegameSerializer, slot, totalSavegames);

        if (savegame == null)
        {
            if (showError)
            {
                if (loadInitialOnError && slot != 0)
                {
                    savegame = SavegameManager.Load(renderView.GameData, savegameSerializer, 0, totalSavegames);

                    if (savegame == null)
                    {
                        ShowMessagePopup(GetCustomText(CustomTexts.Index.FailedToLoadSavegame),
                            Failed, TextAlign.Center, 200);
                    }
                    else
                    {
                        void ProceedWithInitial()
                        {
                            ShowMessagePopup(GetCustomText(CustomTexts.Index.FailedToLoadSavegameUseInitial),
                                () => this.Start(savegame), TextAlign.Center, 200);
                        }
                        if (preLoadAction != null)
                            preLoadAction?.Invoke(ProceedWithInitial);
                        else
                            ProceedWithInitial();
                    }
                }
                else
                {
                    if (slot == 0)
                    {
                        ShowMessagePopup(GetCustomText(CustomTexts.Index.FailedToLoadInitialSavegame),
                            Failed, TextAlign.Center, 200);
                    }
                    else
                    {
                        ShowMessagePopup(GetCustomText(CustomTexts.Index.FailedToLoadSavegame),
                            Failed, TextAlign.Center, 200);
                    }
                }
                return;
            }
            else if (loadInitialOnError && slot != 0)
            {
                LoadGame(0, true, false, preLoadAction, exitWhenFailing);
                return;
            }
            Failed();
            return;
        }

        savegame = Hook_GameLoaded(savegame, slot, updateSlot);

        void Start() => this.Start(savegame, () => postAction?.Invoke(slot));

        if (preLoadAction != null)
            preLoadAction?.Invoke(Start);
        else
            Start();
    }

    void PrepareSaving(Action saveAction)
    {
        // Note: In 3D it is possible to walk partly on tiles that block the player. For example
        // small objects. But when you save and load you will get stuck with that position.
        // We could avoid this partial movement but this feels bad ingame as you have to move around
        // small objects in a larger way. So we will adjust the position only on saving. It won't
        // have the same position after reload but you won't get stuck.
        Position? restorePosition = null;

        try
        {
            if (Is3D && renderMap3D!.IsBlockingPlayer(CurrentSavegame!.CurrentMapX - 1, CurrentSavegame.CurrentMapY - 1))
            {
                var touchedPositions = player3D!.GetTouchedPositions(Global.DistancePerBlock);
                var availablePositions = touchedPositions.Skip(1).Where(position => !renderMap3D.IsBlockingPlayer(position)).ToList();

                if (availablePositions.Count != 0)
                {
                    float tileX = (-camera3D!.X - 0.5f * Global.DistancePerBlock) / Global.DistancePerBlock;
                    float tileY = Map!.Height - (camera3D.Z + 0.5f * Global.DistancePerBlock) / Global.DistancePerBlock;
                    var basePosition = new FloatPosition(tileX, tileY);
                    var savegamePosition = availablePositions.Count == 1 ? availablePositions[0] :
                        availablePositions.OrderBy(position => basePosition.Distance(position)).First();
                    restorePosition = new Position((int)CurrentSavegame.CurrentMapX, (int)CurrentSavegame.CurrentMapY);
                    CurrentSavegame.CurrentMapX = 1 + (uint)savegamePosition.X;
                    CurrentSavegame.CurrentMapY = 1 + (uint)savegamePosition.Y;
                }
            }
        }
        catch
        {
            // ignore
        }

        // If a crash save is stored and the game crashes inside a place (like merchants),
        // all the gold is at the place and none at the party.
        if (currentPlace != null && currentPlace.AvailableGold != 0)
        {
            DistributeGold(currentPlace.AvailableGold, true);
        }

        saveAction?.Invoke();

        try
        {
            if (restorePosition != null)
            {
                CurrentSavegame!.CurrentMapX = (uint)restorePosition.X;
                CurrentSavegame.CurrentMapY = (uint)restorePosition.Y;
            }
        }
        catch
        {
            // ignore
        }
    }

    public void SaveCrashedGame()
    {
        PrepareSaving(() => SavegameManager.SaveCrashedGame(savegameSerializer, CurrentSavegame));
    }

    public void SaveGame(int slot, string name)
    {
        PrepareSaving(() =>
        {
            SavegameManager.Save(renderView.GameData, savegameSerializer, slot, name, CurrentSavegame);
            Hook_GameSaved(slot, name);            
        });
    }
}
