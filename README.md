# PV Local

Local, offline utility-scale solar PV layout tool for AutoCAD-based CAD. It covers the same kind of workflow as commercial AutoCAD solar add-ons: site boundaries and exclusions, trackers or fixed tables, piles on terrain, stringing, cable routing and sizing, a single-line diagram, energy yield and a bill of materials.

It is an independent clean-room implementation built only from publicly described functionality. It contains no code, data or assets from any commercial product, has no licence server, and needs no internet connection at runtime.

## What it does

| Area | Features |
|---|---|
| Layout | Single-axis trackers (N-S axis, backtracking) or fixed-tilt tables (1P/2P); GCR-driven pitch; setbacks from boundary and exclusions; partial trackers to fill edges; DC capacity cap (`MaxDcKwp`); multiple boundaries |
| Terrain and piles | DEM import (ESRI ASCII grid `.asc`) or built from the drawing's 3D faces, meshes, points or contours; slope check map; best-fit straight structure per unit; pile reveal per pile; piles over the max reveal flagged (red layer `PV_PILES_FLAGGED`); pile-length schedule in bins; slope warning per unit; 3D posts and torque tubes |
| Electrical | Auto string length from cold Voc against 1000/1500 V; string-inverter grouping (snake order); transformer stations and MV feeders to the substation; DC/AC/MV Manhattan routing that detours around exclusions; inverters and stations kept out of exclusions; DC, AC and MV cable sizing by voltage drop (IEC-style resistive); trench length with shared runs merged; single-line diagram |
| Yield | Hourly model: NOAA sun position, tracker backtracking, row-to-row shading, isotropic sky, IAM, NOCT temperature, soiling, mismatch, wiring, inverter efficiency and clipping; P50/P90, specific yield, PR, loss breakdown; GCR scenario comparison. Weather: PVGIS TMY CSV (download once) or a clear-sky estimate |
| Output | Drawing on `PV_*` layers, BOM table plus CSV (Excel-ready, uses the local list separator), SLD |

## Which version to use

| CAD | Use | Why |
|---|---|---|
| AutoCAD 2021-2024 | Plugin, `bin\net48` | .NET Framework 4.8 |
| AutoCAD 2025-2026 | Plugin, `bin\net8` | .NET 8 |
| CADPILLAR 11 | Plugin, `bin\net8` | CADPILLAR ignores `[CommandMethod]` commands, so the plugin registers them at runtime and adds a **PV Local** ribbon tab (verified in CADPILLAR 11) |
| Any CAD where plugins cannot load | `bin\cli\pvlocal.exe` + `SCRIPT` | Fallback: no code is loaded into the CAD; it only runs a standard command script |

### AutoCAD: plugin

```powershell
powershell -ExecutionPolicy Bypass -File install.ps1          # per user, no admin
powershell -ExecutionPolicy Bypass -File install.ps1 -Uninstall
```

The installer finds every AutoCAD 2021-2026 and CADPILLAR profile for the current user. For each one it copies the matching build to `%APPDATA%\PVLocal\<net48|net8>`, registers it to load at startup, and adds that folder to the profile's `TRUSTEDPATHS`. Existing trusted paths are kept. Start AutoCAD once before installing, then restart it afterwards.

A **PV Local** ribbon tab is added on startup. Its panels follow the design workflow:

| Panel | Button | Command | What it does |
|---|---|---|---|
| Site | Site area | `PVAREA` | Select the closed polylines that bound the plant; they move to layer `PV_BOUNDARY` |
| Site | Exclusion zones | `PVEXCLUDE` | Select roads, wetlands, buildings and buffers; they move to layer `PV_EXCLUSION` |
| Site | Fetch mesh | `PVMESH` | Builds the terrain from the drawing: 3D faces, polyface and sub-D meshes, points, survey blocks with Z, 3D polylines, contours with elevation. Saved as `<dwg>_terrain.asc` and set as `TerrainFile` |
| Site | Terrain check | `PVTERRAIN` | Slope map along the tracker or table axis, in 5 m cells: green up to half the limit, yellow up to `MaxUnitSlopePct`, red above. Shows the share of the area in each class |
| Design | Settings | `PVSETTINGS` | Settings dialog. Settings are saved inside the DWG, so each project keeps its own |
| Design | Generate layout | `PVLAYOUT` | Trackers or tables and piles inside the site area, on the terrain if one is loaded. Piles above the max reveal go on `PV_PILES_FLAGGED` |
| Design | Compare GCR | `PVCOMPARE` | Compares several GCR values: kWp, MWh/yr, kWh/kWp |
| Electrical | DC cables | `PVDC` | String inverters and DC string cables, sized for voltage drop |
| Electrical | AC cables | `PVAC` | Transformer stations (`InvertersPerStation`) and LV AC cables from inverter to station |
| Electrical | MV cables | `PVMV` | Pick the substation (Enter keeps the current one, or chooses automatically); MV feeders from station to substation; trench |
| Electrical | BOM + SLD | `PVBOM` | BOM table plus `<dwg>_bom.csv`, single-line diagram (substation, MV bus, stations, inverters, strings), energy yield |
| Plant | Generate all | `PVPLANT` | Layout, DC, AC, MV and BOM/SLD in one go |
| Plant | Clear | `PVCLEAR` | Erases everything generated; the site area and exclusions are kept |

The electrical steps read the trackers that are already in the drawing. If you delete or move trackers by hand, the cabling follows your edits.

A step that is re-run replaces its own output and everything after it. A missing earlier step is drawn automatically: for example, `PVMV` with no DC cables drawn yet will draw DC and AC as well.

### Fallback for CADs that cannot load plugins: pvlocal.exe and SCRIPT

1. Draw closed polylines on `PV_BOUNDARY` (site) and `PV_EXCLUSION` (no-build), then `DXFOUT` to `site.dxf`.
2. Run `bin\cli\pvlocal.exe site.dxf`, or drag `site.dxf` onto `pvlocal.exe`. This writes:
   - `site_pvlayout.scr`, the drawing commands
   - `site_bom.csv`
   - `pvlocal_settings.xml`, created with defaults on the first run. Edit it and rerun to change the design.
3. In CADPILLAR, run `SCRIPT` and pick `site_pvlayout.scr`.

Rerunning the script replaces the previous layout (only the generated `PV_*` layers are deleted and recreated). It is one undo step. Needs the .NET 8 runtime, which CADPILLAR already installs.

## Settings (main ones)

| Group | Settings |
|---|---|
| Mounting | `Mount` (Tracker/Fixed), `StringsPerUnit`, `MinStringsPerUnit`, `RowsOfModules`, `TiltDeg`, `MaxTrackerAngle`, `Backtracking` |
| Module | dimensions, `ModulePowerW`, `ModuleVoc`, `ModuleVmp`, `ModuleImp`, temperature coefficients, `ModulesPerString` (capped automatically by the Voc check) |
| Layout | `Gcr`, `Setback`, `UnitGap`, `MaxDcKwp` |
| Terrain and piles | `TerrainFile` (.asc), `PileSpacing`, `PileMinReveal`, `PileMaxReveal`, `PileBin`, `MaxUnitSlopePct` |
| Electrical | `StringsPerInverter`, `InverterKw`, `InverterAcVoltage`, `MaxSystemVoltage`, `SiteMinTempC`, `DcDropMaxPct`, `AcDropMaxPct`, `CableResistivity` (Cu/Al) |
| Yield | `Latitude`, `Longitude`, `WeatherFile` (PVGIS TMY CSV), `Albedo`, `SoilingPct`, `YieldUncertaintyPct` |

Relative paths in `pvlocal_settings.xml` are resolved from the settings file's folder.

## Repository layout

```
plugin/      engine + AutoCAD plugin (C#, multi-target net48 / net8.0-windows)
  Engine.cs    layout, piles, stringing, routing, cable sizing, BOM   (no CAD dependency)
  Terrain.cs   DEM (.asc) load/sample
  Yield.cs     sun position, tracking, shading, energy model
  Drawing.cs   what gets drawn: ICanvas -> AutoCAD database or command script
  Commands.cs  AutoCAD commands (PVLAYOUT, PVSETTINGS, PVCOMPARE, PVCLEAR)
  Ribbon.cs    startup: PV Local ribbon tab, runtime command registration (CADPILLAR), log in %APPDATA%\PVLocal
  Polyfills.cs small .NET Framework 4.8 shims
cli/         pvlocal.exe (DXF in -> .scr + CSV out)
bin/         ready-to-install builds (net48, net8, cli)
install.ps1  per-user installer
test/        EngineCheck acceptance test, CADPILLAR test scripts
pvlayout.py  original Python prototype (stdlib only, DXF in/out); not kept in sync with the C# engine
```

## Build and test

Requires the .NET 8 SDK. The AutoCAD reference assemblies come from Autodesk's `AutoCAD.NET` NuGet packages; they are used at compile time only and are not shipped.

```powershell
dotnet build plugin -c Release        # -> plugin\bin\Release\{net48,net8.0-windows}\PVLocal.dll
dotnet build cli -c Release -o bin\cli
dotnet run --project test\EngineCheck -f net48     # AutoCAD 2024 runtime
dotnet run --project test\EngineCheck -f net8.0    # AutoCAD 2025+/CADPILLAR runtime
```

`EngineCheck` runs a 5 MW sloped-terrain acceptance test (27 checks):
1. DEM and wetland import
2. 1500 V trackers at 0.35 GCR
3. Piles above 2.5 m flagged, with a pile-length distribution
4. Stringing, routes that avoid the wetland, worst DC+AC voltage drop under 2%, SLD topology
5. Shading and yield
6. CSV totals matching the model

The test passes on both runtimes. The CADPILLAR script route was also verified headless with `accoreconsole`: the script was run twice and the layout drawn exactly once, with the boundary and exclusions untouched.

## Known limits

- **Terrain:** rigid structures only; no terrain-following (articulated) trackers or grading. Arc segments in boundary polylines are treated as straight chords.
- **Rotation:** layouts are N-S or E-W only; no rotated azimuth.
- **Routing:** simple detours. Complex or nested exclusions can still produce a "reroute by hand" warning.
- **Cable sizing:** by voltage drop only; current-carrying capacity is not checked.
- **Yield:** a simplified model (no 3D horizon, bifacial gain or electrical shading mismatch). Use a PVGIS TMY file for P50; clear-sky results are an upper bound only. Cross-check bankable numbers in PVsyst.
- **Not yet built:** KML/SHP/GeoJSON import and export, `.PAN`/`.OND`, PVsyst/Civil 3D export, roof mount.
- **CADPILLAR:** AutoLISP is disabled and `[CommandMethod]` commands are ignored; the plugin works through runtime-registered commands and its ribbon tab. Startup diagnostics are written to `%APPDATA%\PVLocal\pvlocal.log`.
