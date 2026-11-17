using Dalamud.Plugin.Ipc;
using System;

namespace CopeSeetheMeld;

// IPC wrapper for the Lifestream plugin. Channel names/signatures verified against
// Lifestream's own IPC implementation. Lifestream has no "nearest Summoning Bell" concept
// of its own (that's vnavmesh's job on the ground) - this is only used, opt-in, to
// teleport to a user-specified aetheryte when auto-gathering can't find a bell nearby, so
// it never has to guess a destination.
public static class Lifestream
{
    private static readonly ICallGateSubscriber<bool> isBusySub =
        Plugin.PluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");

    private static readonly ICallGateSubscriber<string, bool> aetheryteTeleportSub =
        Plugin.PluginInterface.GetIpcSubscriber<string, bool>("Lifestream.AetheryteTeleport");

    // True when Lifestream is installed and responding.
    public static bool IsAvailable()
    {
        try
        {
            isBusySub.InvokeFunc();
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.Debug(e, "Lifestream not available (not installed?)");
            return false;
        }
    }

    public static bool IsBusy()
    {
        try
        {
            return isBusySub.InvokeFunc();
        }
        catch (Exception e)
        {
            Plugin.Log.Debug(e, "Lifestream busy-check failed");
            return false;
        }
    }

    // Teleports to the named aetheryte via Lifestream. Returns true once accepted - not
    // once arrived; poll IsBusy() yourself.
    public static bool AetheryteTeleport(string aetheryteName)
    {
        try
        {
            return aetheryteTeleportSub.InvokeFunc(aetheryteName);
        }
        catch (Exception e)
        {
            Plugin.Log.Warning(e, $"Lifestream teleport to '{aetheryteName}' failed");
            return false;
        }
    }
}
