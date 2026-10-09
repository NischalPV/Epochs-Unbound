# Epochs Unbound

A real-time civilisation strategy game inspired by *Rise of Nations*. Lead a society from its first settlement through the space age to superintelligence, in a world that keeps expanding and has no fixed caps on cities or population.

- **Engine**: Unity 6 LTS, C#, URP
- **Simulation**: Unity Entities (DOTS/ECS) with Burst and Jobs

See [docs/design.md](docs/design.md) for the design document and milestone plan.

## Status

Milestone M1 (first settlement) on top of M0 (foundations: streamed procedural world with continents, oceans and biomes, RTS camera, fixed-step simulation clock).

## Running

Open the repo folder in Unity 6000.5.7f1, open `Assets/_Project/Scenes/Main.unity` and press Play.

| Input | Action |
|-------|--------|
| WASD / arrows / screen edge | Pan (Shift = fast) |
| Q / E | Rotate |
| Mouse wheel | Zoom (15 m to 6 km) |
| Middle mouse drag | Tilt and rotate |
| Space | Pause simulation |
| 1-4 | Simulation speed x1, x2, x4, x8 |
| T / H / F / L or HUD buttons | Place Town Centre / House / Farm / Lumber Camp (Shift keeps placing, right click cancels) |
| Left click | Select a citizen (Shift adds) |
| Right click with citizens selected | On a construction site: build it. On a farm or lumber camp: work there. On the ground: walk there and stay |
| Task buttons (citizens selected) | Build, Farm, Chop wood (nearest place with a free slot), Stay idle, Auto (back to automatic jobs) |
| Esc | Cancel placement and clear selection |
| F9 | Developer Mode only: stress test with 10,000 walking citizens |

The game starts in Town Centre placement: pick flat, dry land. Placed buildings are construction sites: they give nothing until builders finish them. Ten citizens arrive and build the Town Centre; then build farms on fertile land (grassland is best) and lumber camps near forest. Idle adults fill job slots automatically; births need free housing and a food reserve; citizens die of old age or after half a year of starvation.

Tuning lives in `Assets/_Project/Settings` (world seed, chunk size, noise, streaming radius, camera speeds, tick rate). `Epochs Unbound > Rebuild Main Scene` regenerates the scene.

## Art

Third-party art is free CC0 and lives in `Assets/_Project/ThirdParty` (credits in `CREDITS.md` there), committed as ordinary git files. To fetch or refresh it:

1. From the repo root, run `powershell -ExecutionPolicy Bypass -File tools\fetch-art.ps1`. It downloads Poly Haven ground textures and a sky, plus Kenney building, prop and character packs, keeping only the FBX models and their textures.
2. For Quaternius packs, download the zips from quaternius.com by hand and put them in an `ArtDrop` folder at the repo root (git ignores it), then run the script again to unpack them.
3. Open Unity, let it import, then run `Epochs Unbound > Rebuild Main Scene` to put the textures on the terrain and the sky in the scene.

The terrain shader (`Assets/_Project/Shaders/Terrain.shader`) blends grass, dirt, sand, rock and snow by weights from `TerrainSampler.GroundLayers`, over the biome colours. Without the textures it shows the biome colours only.

## Layout

- `Assets/_Project/Scripts/Runtime/Simulation` - `SimClock` and `SimTickSystemGroup`: put deterministic ECS systems in this group; it runs once per fixed tick.
- `Assets/_Project/Scripts/Runtime/WorldGen` - chunk maths, Burst terrain jobs, chunk streaming.
- `Assets/_Project/Scripts/Runtime/CameraControl` - RTS camera.
- `Assets/_Project/Scripts/Runtime/Settlement` - citizens and buildings as ECS entities, Burst systems on the sim tick (life, census, assignment, motion, economy, births), instanced citizen rendering, placement/selection UI. Tuning in `SettlementSettings.asset`.
- `Assets/_Project/Scripts/Editor` - scene setup (`ProjectSetup`) and import settings for third-party art (`ArtImport`).
- `Assets/_Project/Scripts/Tests` - EditMode and PlayMode tests.
- `tools/fetch-art.ps1` - downloads the third-party art.

Tests from the command line:

```
Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -testResults Logs/editmode.xml
```
