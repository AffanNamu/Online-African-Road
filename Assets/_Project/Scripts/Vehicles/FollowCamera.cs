using UnityEngine;
using UnityEngine.InputSystem;

namespace ARO.Vehicles
{
    /// <summary>
    /// Chase / cockpit camera. Restrained by design: yaw-follow, speed-based FOV and distance, low-amplitude
    /// shake from road roughness, slight pitch on braking/acceleration. Mouse wheel adjusts chase distance.
    /// </summary>
    public class FollowCamera : MonoBehaviour
    {
        public Transform target;
        public Vector3 chaseOffset = new Vector3(0f, 4.6f, -13f);
        public Vector3 cockpitOffset = new Vector3(-0.45f, 2.55f, 0.9f);
        public float positionLag = 7f, rotationLag = 6f, lookHeight = 2.2f;
        public float minDistance = 8f, maxDistance = 24f;
        public float baseFov = 55f, maxFovBoost = 8f;
        public bool cockpit;

        Camera _cam; Rigidbody _rb; float _distScale = 1f, _lastSpeed, _pitch;
        public void ToggleCockpit() => cockpit = !cockpit;

        void Awake() { _cam = GetComponent<Camera>(); }

        void LateUpdate()
        {
            if (target == null) return;
            if (_rb == null || _rb.transform != target) _rb = target.GetComponent<Rigidbody>();
            float speed = _rb != null ? _rb.linearVelocity.magnitude : 0f;
            float accel = (speed - _lastSpeed) / Mathf.Max(Time.deltaTime, 0.0001f); _lastSpeed = speed;
            _pitch = Mathf.Lerp(_pitch, Mathf.Clamp(-accel * 0.15f, -2.5f, 2.5f), 1f - Mathf.Exp(-3f * Time.deltaTime));

            if (Mouse.current != null)
            {
                float wheel = Mouse.current.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > 0.01f) _distScale = Mathf.Clamp(_distScale - Mathf.Sign(wheel) * 0.08f, minDistance / -chaseOffset.z, maxDistance / -chaseOffset.z);
            }
            if (_cam != null) _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, baseFov + maxFovBoost * Mathf.Clamp01(speed / 28f), 2f * Time.deltaTime);

            float jolt = target.TryGetComponent(out VehicleController vc) ? vc.SuspensionJolt : 0f;
            float shakeAmp = (0.004f + 0.02f * jolt) * Mathf.Clamp01(speed / 15f);
            var shake = new Vector3(Mathf.PerlinNoise(Time.time * 18f, 0f) - 0.5f, Mathf.PerlinNoise(0f, Time.time * 21f) - 0.5f, 0f) * (shakeAmp * 2f);

            if (cockpit)
            {
                transform.SetPositionAndRotation(target.TransformPoint(cockpitOffset) + shake,
                    target.rotation * Quaternion.Euler(_pitch * 0.4f, 0, 0));
                return;
            }
            var yaw = Quaternion.Euler(0f, target.eulerAngles.y, 0f);
            var off = new Vector3(chaseOffset.x, chaseOffset.y * Mathf.Lerp(0.8f, 1f, _distScale), chaseOffset.z * _distScale);
            var wantPos = target.position + yaw * off;
            transform.position = Vector3.Lerp(transform.position, wantPos, 1f - Mathf.Exp(-positionLag * Time.deltaTime)) + shake;
            var look = Quaternion.LookRotation(target.position + Vector3.up * lookHeight - transform.position) * Quaternion.Euler(_pitch, 0, 0);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-rotationLag * Time.deltaTime));
        }
    }
}
