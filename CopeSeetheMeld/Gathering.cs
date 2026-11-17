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

    // One retainer's worth of a gather plan: how much of each materia to withdraw from it.
    public sealed record RetainerBucket(ulong RetainerId, string RetainerName, Dictionary<Mat, int> Items);

    // Greedily assigns each missing materia to the retainers (that are in the local roster,
    // i.e. selectable by name from a live RetainerList) that hold it, biggest holding first,
    // filling one retainer before spilling to the next. Whatever's left uncovered (no known
    // retainer has it, Allagan Tools is unavailable, or a holder isn't in the local roster
    // yet) comes back as Shortage.
    public static (List<RetainerBucket> Buckets, Dictionary<Mat, int> Shortage) Plan(Dictionary<Mat, int> needed)
    {
        var shortage = new Dictionary<Mat, int>();

        if (!AllaganTools.IsAvailable())
        {
            foreach (var (mat, count) in needed)
                shortage[mat] = count;
            return ([], shortage);
        }

        var self = AllaganTools.CurrentCharacter();
        var buckets = new Dictionary<ulong, RetainerBucket>();

        foreach (var (mat, needCount) in needed)
        {
            var remaining = needCount;
            var counts = AllaganTools.GetItemCountsByCharacter(mat.Item.RowId);
            counts.Remove(self);

            foreach (var (cid, qty) in counts.OrderByDescending(kv => kv.Value))
            {
                if (remaining <= 0)
                    break;
                if (qty == 0)
                    continue;

                // can only automate a retainer we can pick by name from a live RetainerList
                var name = Game.RetainerName(cid);
                if (name == null)
                    continue;

                var take = (int)System.Math.Min(remaining, qty);
                if (!buckets.TryGetValue(cid, out var bucket))
                    buckets[cid] = bucket = new RetainerBucket(cid, name, []);

                bucket.Items[mat] = bucket.Items.GetValueOrDefault(mat) + take;
                remaining -= take;
            }

            if (remaining > 0)
                shortage[mat] = remaining;
        }

        return (buckets.Values.ToList(), shortage);
    }

    public static List<string> DescribeShortage(Dictionary<Mat, int> shortage)
    {
        var messages = new List<string>();
        foreach (var (mat, count) in shortage.OrderBy(kv => kv.Key.Item.Name.ToString()))
            messages.Add($"Shortage: still need {count}x {mat.Item.Name} - not found on any retainer selectable by name (or Allagan Tools is unavailable).");
        return messages;
    }
}
