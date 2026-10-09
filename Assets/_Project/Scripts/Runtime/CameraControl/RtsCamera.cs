using EpochsUnbound.WorldGen;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EpochsUnbound.CameraControl
{
    /// <summary>
    /// Free RTS camera orbiting a focus point on the ground: WASD/edge pan, Q/E rotate,
    /// wheel zoom from street level to strategic height, middle-mouse drag to tilt and rotate.
    /// Uses unscaled time so it keeps working while the simulation is paused.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class RtsCamera : MonoBehaviour
    {
        public CameraSettings Settings;
        public WorldSettings World;

        public Camera Camera { get; private set; }
        public float3 FocusPoint => _focus;
        public float Distance => _distance;

        float3 _focus;
        float _yaw, _pitch, _distance, _targetDistance;
        TerrainParams _terrain;

        void Awake()
        {
            Camera = GetComponent<Camera>();
            _terrain = World.ToParams();
            _focus = new float3(transform.position.x, 0, transform.position.z);
            _yaw = transform.eulerAngles.y;
            _pitch = Settings.StartPitch;
            _distance = _targetDistance = Settings.StartDistance;
        }

        void LateUpdate()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            float dt = Time.unscaledDeltaTime;

            float2 move = 0;
            float turn = 0;
            bool fast = false;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1;
                if (kb.qKey.isPressed) turn -= 1;
                if (kb.eKey.isPressed) turn += 1;
                fast = kb.shiftKey.isPressed;
            }

            if (mouse != null)
            {
                var p = mouse.position.ReadValue();
                if (Settings.EdgePan && Application.isFocused && p.x >= 0 && p.y >= 0 && p.x <= Screen.width && p.y <= Screen.height)
                {
                    int e = Settings.EdgePixels;
                    if (p.x < e) move.x -= 1;
                    else if (p.x > Screen.width - e) move.x += 1;
                    if (p.y < e) move.y -= 1;
                    else if (p.y > Screen.height - e) move.y += 1;
                }

                float scroll = mouse.scroll.ReadValue().y;
                if (scroll != 0)
                    _targetDistance *= math.pow(1f - Settings.ZoomStep, math.sign(scroll));

                if (mouse.middleButton.isPressed)
                {
                    var d = mouse.delta.ReadValue();
                    _yaw += d.x * Settings.DragDegreesPerPixel;
                    _pitch -= d.y * Settings.DragDegreesPerPixel;
                }
            }

            _yaw += turn * Settings.RotateSpeed * dt;
            _pitch = math.clamp(_pitch, Settings.MinPitch, Settings.MaxPitch);
            _targetDistance = math.clamp(_targetDistance, Settings.MinDistance, Settings.MaxDistance);
            float k = 1f - math.exp(-Settings.Smoothing * dt);
            _distance = math.lerp(_distance, _targetDistance, k);

            float yawRad = math.radians(_yaw);
            float3 forward = new float3(math.sin(yawRad), 0, math.cos(yawRad));
            float3 right = new float3(forward.z, 0, -forward.x);
            if (math.any(move != 0))
            {
                float speed = Settings.PanSpeedPerMetre * _distance * (fast ? Settings.FastMultiplier : 1f);
                _focus += (right * move.x + forward * move.y) / math.max(1f, math.length(move)) * speed * dt;
            }

            // Focus rides on the terrain surface so zoom distance is always measured from the ground.
            float ground = TerrainSampler.SurfaceHeight(_focus.xz, _terrain);
            _focus.y = math.lerp(_focus.y, ground, k);

            var rot = Quaternion.Euler(_pitch, _yaw, 0);
            Vector3 pos = (Vector3)_focus - rot * Vector3.forward * _distance;
            pos.y = math.max(pos.y, TerrainSampler.SurfaceHeight(new float2(pos.x, pos.z), _terrain) + 2f);
            transform.SetPositionAndRotation(pos, rot);
            Camera.nearClipPlane = math.clamp(_distance * 0.002f, 0.3f, 10f);
        }
    }
}
