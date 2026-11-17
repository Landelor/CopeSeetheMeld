using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.Interop;
using System;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;

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
}
