using UnityEngine;

namespace EpochsUnbound.Simulation
{
    [CreateAssetMenu(menuName = "Epochs Unbound/Sim Settings")]
    public sealed class SimSettings : ScriptableObject
    {
        [Min(1)] public int TicksPerSecond = 20;
        [Min(1)] public int MaxStepsPerFrame = 8;
        [Tooltip("Speed multipliers bound to keys 1, 2, 3, ...")]
        public float[] Speeds = { 1f, 2f, 4f, 8f };
    }
}
