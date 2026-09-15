# Cope, Seethe, Meld

A [Dalamud](https://github.com/goatcorp/Dalamud) plugin for Final Fantasy XIV that auto-melds materia onto your gear.

Originally by [xanunderscore](https://github.com/xanunderscore/CopeSeetheMeld); this fork keeps it building against current Dalamud/game versions and continues bug fixes.

## Installing

This fork is distributed as a custom Dalamud plugin repository (it is not on the official Dalamud plugin list). To install:

1. In-game, open the Dalamud settings (`/xlsettings`) and go to the **Experimental** tab.
2. Under **Custom Plugin Repositories**, add:

   ```
   https://github.com/Landelor/CopeSeetheMeld/releases/latest/download/repo.json
   ```

3. Save, then find **Cope, Seethe, Meld** in the plugin installer and install it.

Releases are built automatically from tags via [`.github/workflows/publish.yml`](.github/workflows/publish.yml); each release ships a `repo.json` and `latest.zip` as release assets.

## Building locally

Requires the .NET SDK matching `CopeSeetheMeld/CopeSeetheMeld.csproj` and a `DALAMUD_HOME` environment variable pointing at an extracted Dalamud install (see the workflow for how CI fetches one).

```
dotnet restore
dotnet build --configuration Release CopeSeetheMeld/CopeSeetheMeld.csproj
```
