# Epochs Unbound

A real-time civilisation strategy game inspired by *Rise of Nations*. Lead a society from its first settlement through the space age to superintelligence, in a world that keeps expanding and has no fixed caps on cities or population.

- **Engine**: Unity 6 LTS, C#, URP
- **Simulation**: Unity Entities (DOTS/ECS) with Burst and Jobs

See [docs/design.md](docs/design.md) for the design document and milestone plan.

## Status

Milestone M0 (foundations): Unity 6000.5.7f1 project with chunked procedural terrain streamed around the camera, a free RTS camera and a fixed-step simulation clock.

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

Tuning lives in `Assets/_Project/Settings` (world seed, chunk size, noise, streaming radius, camera speeds, tick rate). `Epochs Unbound > Rebuild Main Scene` regenerates the scene.

## Layout

- `Assets/_Project/Scripts/Runtime/Simulation` - `SimClock` and `SimTickSystemGroup`: put deterministic ECS systems in this group; it runs once per fixed tick.
- `Assets/_Project/Scripts/Runtime/WorldGen` - chunk maths, Burst terrain jobs, chunk streaming.
- `Assets/_Project/Scripts/Runtime/CameraControl` - RTS camera.
- `Assets/_Project/Scripts/Tests` - EditMode and PlayMode tests.

Tests from the command line:

```
Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -testResults Logs/editmode.xml
```
