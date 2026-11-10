using Ambermoon.Data.Serialization;

namespace Ambermoon.Data
{
    public interface ISavegameManager
    {
        Savegame LoadInitial(IGameData gameData, ISavegameSerializer savegameSerializer);
        Savegame Load(IGameData gameData, ISavegameSerializer savegameSerializer, int saveSlot, int totalSavegames);
        // Slot names can be missing after saves are copied between installations.
        // Unknown storage implementations must conservatively protect their data.
        bool SaveSlotExists(IGameData gameData, int saveSlot) => true;
        bool HasCrashSavegame();
        bool RemoveCrashedSavegame();
        void SaveCrashedGame(ISavegameSerializer savegameSerializer, Savegame savegame);
        void Save(IGameData gameData, ISavegameSerializer savegameSerializer, int saveSlot, string name, Savegame savegame);
        string[] GetSavegameNames(IGameData gameData, out int current, int totalSavegames);
        void WriteSavegameName(IGameData gameData, int slot, ref string name, string externalSavesPath);
        void SetActiveSavegame(IGameData gameData, int slot);
    }
}
