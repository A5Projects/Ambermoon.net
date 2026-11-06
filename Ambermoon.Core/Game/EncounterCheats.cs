namespace Ambermoon;

partial class GameCore
{
    /// <summary>
    /// Suppresses random movement encounters for this game session. Scripted
    /// battles and monsters already present on the map retain their behavior.
    /// </summary>
    public bool DisableRandomEncounters { get; set; } = false;
}
