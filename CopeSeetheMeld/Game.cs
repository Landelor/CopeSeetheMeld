using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using Lumina.Excel.Sheets;
using System;
using System.Numerics;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;
using GameObjectStruct = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace CopeSeetheMeld;

internal static unsafe class Game
{
    public static bool IsAttachAgentActive() => AgentMateriaAttach.Instance()->IsAgentActive();
    public static bool AgentLoading() => AgentMateriaAttach.Instance()->UpdateState != 0;
    public static AgentMateriaAttach.FilterCategory ActiveFilter() => AgentMateriaAttach.Instance()->Category;
    public static int SelectedItem() => AgentMateriaAttach.Instance()->SelectedItemIndex;

    public static AtkValue AgentReceiveEvent(AgentInterface* agent, ulong eventKind, int[] args) => WithArgs(args, (ptr, len) =>
        {
            var ret = new AtkValue();
            agent->ReceiveEvent(&ret, ptr, len, eventKind);
            return ret;
        });

    public static void FireCallback(AtkUnitBase* addon, int[] args, bool close = false)
        => WithArgs(args, (ptr, len) => addon->FireCallback(len, ptr.Value, close));

    public static void FireCallback(string addonName, int[] args, bool close = false)
    {
        var addon = GetActiveAddon(addonName);
        if (addon == null)
            throw new Exception($"Addon {addonName} was expected to be active, but isn't");

        FireCallback(addon, args, close);
    }

    public static T WithArgs<T>(int[] args, Func<Pointer<AtkValue>, uint, T> callback)
    {
        var values = stackalloc AtkValue[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            values[i].Type = ValueType.Int;
            values[i].Int = args[i];
        }
        return callback(values, (uint)args.Length);
    }

    public static void WithArgs(int[] args, Action<Pointer<AtkValue>, uint> callback) => WithArgs<object>(args, (a, b) => { callback(a, b); return null!; });

    public static bool IsAddonActive(string name) => GetActiveAddon(name) != null;

    private static AtkUnitBase* GetActiveAddon(string name)
    {
        var addon = RaptureAtkUnitManager.Instance()->GetAddonByName(name);
        return addon != null && addon->IsVisible && addon->IsReady ? addon : null;
    }

    public static bool PlayerIsBusy => !Plugin.Condition[ConditionFlag.NormalConditions] || Plugin.Condition[ConditionFlag.Occupied39] || Plugin.Condition[ConditionFlag.Jumping];

    public static bool PlayerIsMelding => Plugin.Condition[ConditionFlag.MeldingMateria];
    public static bool PlayerIsRetrieving => Plugin.Condition[ConditionFlag.Occupied39];

    private static readonly InventoryType[] PlayerBags = [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4];
    private static readonly InventoryType[] RetainerPages =
    [
        InventoryType.RetainerPage1, InventoryType.RetainerPage2, InventoryType.RetainerPage3, InventoryType.RetainerPage4,
        InventoryType.RetainerPage5, InventoryType.RetainerPage6, InventoryType.RetainerPage7
    ];

    // CID of the retainer currently summoned/open (0 if none).
    public static ulong ActiveRetainerId()
    {
        var r = RetainerManager.Instance()->GetActiveRetainer();
        return r == null ? 0 : r->RetainerId;
    }

    // Name of the retainer currently summoned/open (null if none).
    public static string? ActiveRetainerName()
    {
        var r = RetainerManager.Instance()->GetActiveRetainer();
        return r == null ? null : r->NameString;
    }

    // Looks up a retainer's name from the local roster (retainers the player has summoned at least
    // once this session); null if the id isn't known locally.
    public static string? RetainerName(ulong retainerId)
    {
        var mgr = RetainerManager.Instance();
        if (mgr == null)
            return null;

        for (byte i = 0; i < 10; i++)
        {
            var r = mgr->GetRetainerBySortedIndex(i);
            if (r != null && r->RetainerId == retainerId)
                return r->NameString;
        }

        return null;
    }

    private static bool TryGetFreeBagSlot(out InventoryType type, out ushort slot)
    {
        var im = InventoryManager.Instance();
        foreach (var ty in PlayerBags)
        {
            var container = im->GetInventoryContainer(ty);
            for (var i = 0; i < container->Size; i++)
            {
                var item = container->GetInventorySlot(i);
                if (item == null || item->ItemId == 0)
                {
                    type = ty;
                    slot = (ushort)i;
                    return true;
                }
            }
        }

        type = default;
        slot = 0;
        return false;
    }

    // Withdraws up to maxQuantity of itemId from the currently-open retainer's inventory into free
    // bag slots. Does nothing (returns 0) unless a retainer is actively open with its inventory
    // loaded. Withdrawals move whole stacks at a time, so the returned amount can exceed
    // maxQuantity if the stack that covered the remainder was bigger than needed.
    public static int WithdrawFromRetainer(uint itemId, int maxQuantity)
    {
        if (maxQuantity <= 0)
            return 0;

        var im = InventoryManager.Instance();
        var withdrawn = 0;

        foreach (var page in RetainerPages)
        {
            var container = im->GetInventoryContainer(page);
            if (container == null)
                continue;

            for (var i = 0; i < container->Size && withdrawn < maxQuantity; i++)
            {
                var item = container->GetInventorySlot(i);
                if (item == null || item->ItemId != itemId)
                    continue;

                if (!TryGetFreeBagSlot(out var dstType, out var dstSlot))
                    return withdrawn;

                var qty = item->Quantity;
                im->MoveItemSlot(page, (ushort)i, dstType, dstSlot);
                withdrawn += qty;
            }
        }

        return withdrawn;
    }

    private static string? cachedBellName;

    // Localized "Summoning Bell" object name (EObjName row 2000401) - the technique used by
    // AutoRetainer/RetainerReach for bell detection.
    private static string BellName => cachedBellName ??= Plugin.LuminaRow<EObjName>(2000401).Singular.ExtractText();

    private static bool IsBell(IGameObject obj)
        => obj.ObjectKind is ObjectKind.EventObj or ObjectKind.HousingEventObject
           && string.Equals(obj.Name.ToString(), BellName, StringComparison.OrdinalIgnoreCase);

    // The nearest Summoning Bell to the player in the current zone, or null if none is
    // loaded/visible nearby (Dalamud's object table only sees nearby-streamed objects).
    public static IGameObject? NearestBell()
    {
        var playerPos = Plugin.ObjectTable.LocalPlayer?.Position;
        if (playerPos == null)
            return null;

        IGameObject? nearest = null;
        var best = float.MaxValue;
        foreach (var obj in Plugin.ObjectTable)
        {
            if (!IsBell(obj))
                continue;

            var d = Vector3.Distance(playerPos.Value, obj.Position);
            if (d < best)
            {
                best = d;
                nearest = obj;
            }
        }

        return nearest;
    }

    // True once close enough to bell to interact with it.
    public static bool IsNearBell(IGameObject bell, float range = 4.6f)
    {
        var playerPos = Plugin.ObjectTable.LocalPlayer?.Position;
        return playerPos != null && bell.IsTargetable && Vector3.Distance(playerPos.Value, bell.Position) <= range;
    }

    public static void InteractWithBell(IGameObject bell)
        => TargetSystem.Instance()->InteractWithObject((GameObjectStruct*)bell.Address, false);

    // Selects the named retainer from the currently-open RetainerList. Returns true once the
    // selection was fired; false if RetainerList isn't open/active or the name isn't in it.
    public static bool SelectRetainerByName(string name)
    {
        var agent = AgentRetainerList.Instance();
        if (agent == null || !agent->IsAgentActive())
            return false;

        foreach (var r in agent->Retainers)
        {
            if (!string.Equals(r.Name.ToString(), name, StringComparison.OrdinalIgnoreCase))
                continue;

            FireCallback("RetainerList", [2, r.Index, 0, 0]);
            return true;
        }

        return false;
    }

    // Index of the first SelectString entry whose text contains any of the given keywords
    // (case-insensitive); -1 if the addon isn't open or none match. Matching on live display
    // text (rather than a hardcoded Addon-sheet row id) so this keeps working even if the
    // exact row id is wrong/changes - it only ever picks an entry whose text we recognize.
    public static int FindSelectStringEntry(params string[] keywords)
    {
        var addon = (AddonSelectString*)GetActiveAddon("SelectString");
        if (addon == null)
            return -1;

        for (var i = 0; i < addon->PopupMenu.EntryCount; i++)
        {
            var text = addon->PopupMenu.EntryNames[i].ToString();
            foreach (var kw in keywords)
                if (text.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    return i;
        }

        return -1;
    }

    // Name of whichever retainer-items addon is currently open ("InventoryRetainer" or the
    // "Large" variant used for higher-level retainers), or null if neither is open.
    public static string? ActiveRetainerInventoryAddon()
    {
        if (IsAddonActive("InventoryRetainerLarge"))
            return "InventoryRetainerLarge";
        if (IsAddonActive("InventoryRetainer"))
            return "InventoryRetainer";
        return null;
    }
}
