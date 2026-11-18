using UnityEngine;
using UnityEngine.InputSystem;

namespace Karman.VAB
{
    /// <summary>Assembly camera: orbits the vehicle axis; right-drag orbit, scroll zoom, middle-drag or Shift+scroll height, F to frame.</summary>
    public sealed class VabCamera : MonoBehaviour
    {
        public VabController vab;
        public float yaw = 35, pitch = 8, distance = 70, height = 25;
        public float minDistance = 8, maxDistance = 220;
        float sYaw, sPitch, sDist, sHeight;

        void Start() { Frame(); Snap(); }

        public void Snap() { sYaw = yaw; sPitch = pitch; sDist = distance; sHeight = height; }

        public void Frame()
        {
            var b = vab.CraftBounds();
            height = b.center.y;
            distance = Mathf.Clamp(Mathf.Max(b.size.y, b.size.x * 2) * 1.25f, minDistance, maxDistance);
        }

        void Update()
        {
            var mouse = Mouse.current; var kb = Keyboard.current;
            if (mouse == null) return;
            bool overUi = vab.IsPointerOverUi(mouse.position.ReadValue());
            if (mouse.rightButton.isPressed)
            {
                var d = mouse.delta.ReadValue();
                yaw += d.x * 0.3f; pitch = Mathf.Clamp(pitch - d.y * 0.3f, -20, 80);
            }
            if (mouse.middleButton.isPressed) height += mouse.delta.ReadValue().y * -0.05f * distance / 30f;
            float scroll = overUi ? 0 : mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                if (kb != null && kb.shiftKey.isPressed) height += Mathf.Sign(scroll) * distance * 0.05f;
                else distance *= Mathf.Pow(1.12f, -Mathf.Sign(scroll));
            }
            if (kb != null && kb.fKey.wasPressedThisFrame) Frame();
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
            height = Mathf.Clamp(height, 1, 120);
        }

        void LateUpdate()
        {
            float k = 1 - Mathf.Exp(-10 * Time.unscaledDeltaTime);
            sYaw = Mathf.LerpAngle(sYaw, yaw, k); sPitch = Mathf.Lerp(sPitch, pitch, k);
            sDist = Mathf.Lerp(sDist, distance, k); sHeight = Mathf.Lerp(sHeight, height, k);
            var rot = Quaternion.Euler(sPitch, sYaw, 0);
            var target = new Vector3(vab.craftRoot.position.x, sHeight, vab.craftRoot.position.z);
            transform.SetPositionAndRotation(target + rot * Vector3.back * sDist, rot);
        }
    }
}
