using EpochsUnbound.WorldGen;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EpochsUnbound.Simulation
{
    /// <summary>Configures the sim clock from settings, binds pause/speed keys and draws a debug HUD.</summary>
    public sealed class SimClockController : MonoBehaviour
    {
        public SimSettings Settings;
        public ChunkStreamer Streamer;

        SimTickSystemGroup _group;

        void Start()
        {
            _group = Unity.Entities.World.DefaultGameObjectInjectionWorld?.GetExistingSystemManaged<SimTickSystemGroup>();
            if (_group != null && Settings != null)
                _group.Clock = new SimClock(Settings.TicksPerSecond, Settings.MaxStepsPerFrame);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (_group == null || kb == null) return;
            var clock = _group.Clock;

            if (kb.spaceKey.wasPressedThisFrame) clock.Paused = !clock.Paused;
            for (int i = 0; i < Settings.Speeds.Length && i < 9; i++)
            {
                if (kb[Key.Digit1 + i].wasPressedThisFrame)
                {
                    clock.Speed = Settings.Speeds[i];
                    clock.Paused = false;
                }
            }
        }

        void OnGUI()
        {
            if (_group == null) return;
            var c = _group.Clock;
            var state = c.Paused ? "PAUSED" : $"x{c.Speed:0.#}";
            var chunks = Streamer != null ? Streamer.LoadedCount : 0;
            GUI.Label(new Rect(10, 10, 600, 60),
                $"Tick {c.Tick}  ({c.SimSeconds:0.0}s sim, {1.0 / c.TickSeconds:0} Hz)  {state}  |  Chunks {chunks}\n" +
                "WASD/edge pan  Q/E rotate  Wheel zoom  MMB drag tilt/rotate  Shift fast  |  Space pause  1-4 speed");
        }
    }
}
