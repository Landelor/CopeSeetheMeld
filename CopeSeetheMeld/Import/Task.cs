using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CopeSeetheMeld.Import;

public partial class Import(string input) : AutoTask
{
    private readonly HttpClient client = new();
    private readonly JsonSerializerOptions jop = new() { IncludeFields = true };

    [GeneratedRegex(@"https?:\/\/etro\.gg\/gearset\/([^/]+)", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex PatternEtro();

    // xivgear now defaults to path-based share links (xivgear.app/sl/<id>, optionally
    // xivgear.app/embed/sl/<id>) but the old query-based links (?page=sl|<id>) are still
    // valid, as is hitting the shortlink API directly.
    [GeneratedRegex(@"https?:\/\/(?:xivgear\.app\/(?:\?page=sl\||(?:embed\/)?sl\/)|api\.xivgear\.app\/shortlink\/)([a-zA-Z0-9-]+)", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex PatternXIVG();

    // selectedIndex/onlySetIndex can appear anywhere in the query string (order isn't guaranteed),
    // so it's matched independently of the shortlink id instead of as a trailing group.
    [GeneratedRegex(@"[?&](?:selectedIndex|onlySetIndex)=(\d+)", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex PatternXIVGIndex();

    protected override async Task Execute()
    {
        using var _ = new OnDispose(Plugin.Config.Save);

        // teamcraft export is just markdown
        if (input.StartsWith("**"))
        {
            Status = "Importing from TC";
            ImportTeamcraft(input);
            return;
        }

        input = Uri.UnescapeDataString(input);

        var m1 = PatternEtro().Match(input);
        if (m1.Success)
        {
            Status = "Importing from Etro";
            await ImportEtro(m1.Groups[1].Value);
            return;
        }

        var m2 = PatternXIVG().Match(input);
        if (m2.Success)
        {
            Status = "Importing from xivgear";
            var ix = PatternXIVGIndex().Match(input);
            await ImportXIVG(m2.Groups[1].Value, ix.Success ? ix.Groups[1].Value : "");
            return;
        }

        Error("Unrecognized input");
    }
}
