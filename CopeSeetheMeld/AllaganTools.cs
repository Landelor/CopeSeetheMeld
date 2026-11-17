using Dalamud.Plugin.Ipc;
using System;
using System.Collections.Generic;

namespace CopeSeetheMeld;

// Read-only IPC wrapper for the Allagan Tools plugin (internal name "InventoryTools"),
// used to find out which retainers hold a given materia and how much. Every call is a
// safe no-op (false/0/empty) if Allagan Tools isn't installed or hasn't finished its
// inventory scan yet, so callers never need to special-case "not installed" themselves.
public static class AllaganTools
{
    private static readonly ICallGateSubscriber<bool> isInitializedSub =
        Plugin.PluginInterface.GetIpcSubscriber<bool>("AllaganTools.IsInitialized");

    private static readonly ICallGateSubscriber<ulong> currentCharacterSub =
        Plugin.PluginInterface.GetIpcSubscriber<ulong>("AllaganTools.CurrentCharacter");

    // (itemId, currentCharacterOnly, inventoryCategories, includeSharedStorage) -> CID to quantity owned.
    // An empty inventoryCategories array means "every category".
    private static readonly ICallGateSubscriber<uint, bool, uint[], bool, Dictionary<ulong, uint>> itemCountsByCharacterSub =
        Plugin.PluginInterface.GetIpcSubscriber<uint, bool, uint[], bool, Dictionary<ulong, uint>>("AllaganTools.GetItemCountsByCharacter");

    // True once Allagan Tools is installed and has finished its own inventory scan.
    public static bool IsAvailable()
    {
        try
        {
            return isInitializedSub.InvokeFunc();
        }
        catch (Exception e)
        {
            Plugin.Log.Debug(e, "Allagan Tools not available (not installed?)");
            return false;
        }
    }

    // CID of the actively-logged-in character, as reported by Allagan Tools; 0 on failure.
    public static ulong CurrentCharacter()
    {
        try
        {
            return currentCharacterSub.InvokeFunc();
        }
        catch (Exception e)
        {
            Plugin.Log.Debug(e, "Failed to query current character from Allagan Tools");
            return 0;
        }
    }

    // How much of itemId is held by each character/retainer/free company belonging to the active
    // character (including the player's own character); empty when Allagan Tools is unavailable.
    public static Dictionary<ulong, uint> GetItemCountsByCharacter(uint itemId)
    {
        try
        {
            return itemCountsByCharacterSub.InvokeFunc(itemId, true, [], false) ?? [];
        }
        catch (Exception e)
        {
            Plugin.Log.Debug(e, $"Failed to query Allagan Tools for item {itemId}");
            return [];
        }
    }
}
