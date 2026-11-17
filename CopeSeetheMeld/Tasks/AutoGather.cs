using System.Collections.Generic;
using System.Threading.Tasks;
using static CopeSeetheMeld.Data;

namespace CopeSeetheMeld.Tasks;

// Fully automated "gather missing materia" flow: walks to the nearest Summoning Bell
// (vnavmesh - optionally preceded by a Lifestream teleport to a user-configured home
// aetheryte if no bell is nearby), opens each retainer Allagan Tools says holds some of
// the missing materia, withdraws it, and reports any shortfall. Never guesses its way
// past a step it can't verify (e.g. unrecognized menu text) - it errors out instead of
// risking clicking the wrong thing.
public class AutoGather(Gearset goal, MeldOptions baseOpts) : AutoTask
{
    public readonly List<string> Messages = [];
    public bool Done { get; private set; }

    protected override async Task Execute()
    {
        using var stopMove = new OnDispose(() => VNavmesh.Stop());

        var needed = await ComputeNeeded();
        if (needed.Count == 0)
        {
            Report("Nothing to gather - you already have enough materia in your inventory.");
            Done = true;
            return;
        }

        ErrorIf(!VNavmesh.IsAvailable(), "vnavmesh isn't installed (or has no navmesh loaded here) - can't walk to a bell automatically.");

        await EnsureAtBell();
        await OpenRetainerMenu();

        var (buckets, shortage) = Gathering.Plan(needed);

        foreach (var bucket in buckets)
        {
            Status = $"Visiting {bucket.RetainerName}";
            Report(Status);
            await VisitRetainer(bucket);
        }

        await CloseRetainerMenu();

        foreach (var msg in Gathering.DescribeShortage(shortage))
            Report(msg);

        Report("All done!");
        Done = true;
    }

    private void Report(string msg)
    {
        Messages.Add(msg);
        Plugin.Log.Debug($"[AutoGather] {msg}");
    }

    private async Task<Dictionary<Mat, int>> ComputeNeeded()
    {
        var opts = new MeldOptions
        {
            DryRun = true,
            Overmeld = baseOpts.Overmeld,
            MeldConfidence = baseOpts.MeldConfidence,
            StopOnMissingItem = MeldOptions.StopBehavior.Skip,
            StopOnMissingMateria = MeldOptions.StopBehavior.Skip,
        };

        var log = new MeldLog();
        await new Meld(goal, opts, log).RunDirect();
        log.Count();
        return log.MateriaNeeded;
    }

    private async Task EnsureAtBell()
    {
        var bell = Game.NearestBell();

        if (bell == null && !string.IsNullOrWhiteSpace(Plugin.Config.GatherHomeAetheryte) && Lifestream.IsAvailable())
        {
            Status = $"No bell nearby - teleporting to '{Plugin.Config.GatherHomeAetheryte}' via Lifestream";
            Report(Status);
            ErrorIf(!Lifestream.AetheryteTeleport(Plugin.Config.GatherHomeAetheryte), $"Lifestream couldn't teleport to '{Plugin.Config.GatherHomeAetheryte}' - check the aetheryte name in Options.");
            await WaitWhile(Lifestream.IsBusy, "LifestreamTeleport", 10);
            await NextFrame(30); // let the new zone's objects stream in before scanning for a bell
            bell = Game.NearestBell();
        }

        ErrorIf(bell == null, "No Summoning Bell nearby. Move to a town/your house (any bell works for every retainer), or set a home aetheryte in Options, and try again.");

        if (!Game.IsNearBell(bell!))
        {
            Status = "Walking to the nearest Summoning Bell";
            Report(Status);
            ErrorIf(!VNavmesh.MoveTo(bell!.Position), "vnavmesh refused the move request.");
            await WaitWhile(VNavmesh.InProgress, "WalkToBell", 5);
            await WaitWhile(() => !Game.IsNearBell(bell!), "ApproachBell", 5);
        }
    }

    private async Task OpenRetainerMenu()
    {
        if (Game.IsAddonActive("RetainerList") || Game.IsAddonActive("SelectString"))
            return;

        var bell = Game.NearestBell();
        ErrorIf(bell == null, "No Summoning Bell nearby to interact with.");

        Status = "Opening the Summoning Bell";
        Report(Status);

        Game.InteractWithBell(bell!);
        await WaitWhile(() => !Game.IsAddonActive("RetainerList") && !Game.IsAddonActive("SelectString"), "OpenBell");
    }

    private async Task VisitRetainer(Gathering.RetainerBucket bucket)
    {
        // accounts with a single retainer skip straight to its menu instead of showing a list
        if (Game.IsAddonActive("RetainerList"))
        {
            ErrorIf(!Game.SelectRetainerByName(bucket.RetainerName), $"Couldn't find '{bucket.RetainerName}' in the retainer list.");
            await WaitWhile(() => !Game.IsAddonActive("SelectString") && !Game.IsAddonActive("Talk"), "SelectRetainer");
        }

        // click through the retainer's greeting line(s), if any
        while (Game.IsAddonActive("Talk"))
        {
            Game.FireCallback("Talk", [0]);
            await NextFrame(5);
        }

        await WaitWhile(() => !Game.IsAddonActive("SelectString"), "RetainerMenu");

        var entrustIdx = Game.FindSelectStringEntry("entrust", "withdraw");
        ErrorIf(entrustIdx < 0, $"Couldn't find 'Entrust or withdraw items' in {bucket.RetainerName}'s menu - the text may differ on this client.");
        Game.FireCallback("SelectString", [entrustIdx], true);

        await WaitWhile(() => Game.ActiveRetainerInventoryAddon() == null, "OpenRetainerItems");

        foreach (var (mat, count) in bucket.Items)
        {
            var withdrawn = Game.WithdrawFromRetainer(mat.Item.RowId, count);
            Report(withdrawn > 0
                ? $"Withdrew {withdrawn}x {mat.Item.Name} from {bucket.RetainerName}."
                : $"{bucket.RetainerName} no longer has {mat.Item.Name} (already gone?).");
            await NextFrame();
        }

        if (Game.ActiveRetainerInventoryAddon() is { } invAddon)
            Game.FireCallback(invAddon, [-1], true);

        await WaitWhile(() => !Game.IsAddonActive("SelectString"), "CloseRetainerItems");

        var quitIdx = Game.FindSelectStringEntry("quit");
        ErrorIf(quitIdx < 0, $"Couldn't find 'Quit' in {bucket.RetainerName}'s menu.");
        Game.FireCallback("SelectString", [quitIdx], true);

        await WaitWhile(() => !Game.IsAddonActive("RetainerList"), "BackToList");
    }

    private async Task CloseRetainerMenu()
    {
        if (Game.IsAddonActive("RetainerList"))
            Game.FireCallback("RetainerList", [-1], true);

        await NextFrame(5);
    }
}
