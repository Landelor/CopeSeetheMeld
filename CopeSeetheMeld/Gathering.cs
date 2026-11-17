using System.Collections.Generic;
using System.Linq;
using static CopeSeetheMeld.Data;

namespace CopeSeetheMeld;

// Figures out where to get materia that's missing for a gearset (per MeldLog.MateriaNeeded),
// using Allagan Tools to see which retainers hold it, withdrawing it automatically from whichever
// retainer is currently open, and reporting shortages/where-to-look for the rest.
public static class Gathering
{
    public sealed record Report(List<string> Messages, bool AnyShortage);

    public static Report Run(Dictionary<Mat, int> needed)
    {
        var messages = new List<string>();

        if (needed.Count == 0)
        {
            messages.Add("Nothing to gather - you already have enough materia in your inventory.");
            return new(messages, false);
        }

        if (!AllaganTools.IsAvailable())
        {
            messages.Add("Allagan Tools isn't installed (or hasn't finished scanning yet) - can't check your retainers for materia.");
            return new(messages, true);
        }

        var self = AllaganTools.CurrentCharacter();
        var activeRetainerId = Game.ActiveRetainerId();
        var activeRetainerName = Game.ActiveRetainerName();
        var anyShortage = false;

        foreach (var (mat, needCount) in needed.OrderBy(kv => kv.Key.Item.Name.ToString()))
        {
            var remaining = needCount;
            var counts = AllaganTools.GetItemCountsByCharacter(mat.Item.RowId);
            counts.Remove(self);

            // Pull whatever we can from the retainer that's currently open, if it's one of the holders.
            if (activeRetainerId != 0 && counts.TryGetValue(activeRetainerId, out var activeCount) && activeCount > 0)
            {
                var withdrawn = Game.WithdrawFromRetainer(mat.Item.RowId, remaining);
                if (withdrawn > 0)
                {
                    messages.Add($"Withdrew {withdrawn}x {mat.Item.Name} from {activeRetainerName ?? "the current retainer"}.");
                    remaining -= withdrawn;
                    counts[activeRetainerId] = (uint)System.Math.Max(0, (int)activeCount - withdrawn);
                }
            }

            if (remaining <= 0)
                continue;

            var holders = counts.Where(kv => kv.Value > 0).ToList();
            var available = (int)holders.Sum(kv => (long)kv.Value);

            if (available <= 0)
            {
                messages.Add($"Shortage: need {remaining}x {mat.Item.Name}, but none was found on any retainer.");
                anyShortage = true;
                continue;
            }

            var byRetainer = holders.Select(kv => $"{RetainerLabel(kv.Key)} ({kv.Value})");

            if (available < remaining)
            {
                messages.Add($"Shortage: need {remaining}x {mat.Item.Name}, only {available} available across retainers [{string.Join(", ", byRetainer)}].");
                anyShortage = true;
            }
            else
            {
                messages.Add($"Need {remaining}x {mat.Item.Name} - visit {string.Join(", ", byRetainer)} to withdraw it.");
            }
        }

        return new(messages, anyShortage);
    }

    private static string RetainerLabel(ulong id) => Game.RetainerName(id) ?? $"retainer {id}";
}
