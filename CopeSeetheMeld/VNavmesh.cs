using Dalamud.Plugin.Ipc;
using System;
using System.Numerics;

namespace CopeSeetheMeld;

// IPC wrapper for the vnavmesh plugin, used to walk to a Summoning Bell automatically.
// Channel names/signatures verified against vnavmesh's own IPCProvider.cs. Every call is a
// safe no-op (false/0) if vnavmesh isn't installed or the current zone has no navmesh loaded.
public static class VNavmesh
{
    private static readonly ICallGateSubscriber<bool> navIsReadySub =
        Plugin.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");

    private static readonly ICallGateSubscriber<Vector3, bool, bool> pathfindAndMoveToSub =
        Plugin.PluginInterface.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo");

    private static readonly ICallGateSubscriber<bool> pathfindInProgressSub =
        Plugin.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");

    private static readonly ICallGateSubscriber<object> pathStopSub =
        Plugin.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");

    // True when vnavmesh is installed and has a navmesh loaded for the current zone.
    public static bool IsAvailable()
    {
        try
        {
            return navIsReadySub.InvokeFunc();
        }
        catch (Exception e)
        {
            Plugin.Log.Debug(e, "vnavmesh not available (not installed, or no navmesh for this zone)");
            return false;
        }
    }

    // Queues an async pathfind + walk to dest. Returns true once the request was accepted -
    // that does NOT mean it arrived; poll InProgress()/distance yourself.
    public static bool MoveTo(Vector3 dest, bool fly = false)
    {
        try
        {
            return pathfindAndMoveToSub.InvokeFunc(dest, fly);
        }
        catch (Exception e)
        {
            Plugin.Log.Warning(e, "vnavmesh move request failed");
            return false;
        }
    }

    public static bool InProgress()
    {
        try
        {
            return pathfindInProgressSub.InvokeFunc();
        }
        catch (Exception e)
        {
            Plugin.Log.Debug(e, "vnavmesh progress check failed");
            return false;
        }
    }

    // Cancels any in-progress move. Best effort - used on cancellation/cleanup.
    public static void Stop()
    {
        try
        {
            pathStopSub.InvokeAction();
        }
        catch (Exception e)
        {
            Plugin.Log.Debug(e, "vnavmesh stop request failed");
        }
    }
}
