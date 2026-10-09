using UnityEngine;

namespace EpochsUnbound.CameraControl
{
    [CreateAssetMenu(menuName = "Epochs Unbound/Camera Settings")]
    public sealed class CameraSettings : ScriptableObject
    {
        [Header("Pan")]
        [Tooltip("Pan speed per metre of camera distance, so panning feels the same at every zoom.")]
        public float PanSpeedPerMetre = 1.2f;
        public float FastMultiplier = 3f;
        public bool EdgePan = true;
        [Min(1)] public int EdgePixels = 8;

        [Header("Rotate / tilt")]
        public float RotateSpeed = 90f;
        public float DragDegreesPerPixel = 0.25f;
        [Range(5f, 89f)] public float MinPitch = 20f;
        [Range(5f, 89f)] public float MaxPitch = 88f;
        [Range(5f, 89f)] public float StartPitch = 50f;

        [Header("Zoom")]
        public float MinDistance = 15f;
        public float MaxDistance = 6000f;
        public float StartDistance = 90f;
        [Tooltip("Fraction of the current distance moved per wheel notch.")]
        [Range(0.05f, 0.5f)] public float ZoomStep = 0.15f;
        [Tooltip("Higher is snappier.")]
        public float Smoothing = 12f;
    }
}
