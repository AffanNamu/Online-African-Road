using UnityEngine;

namespace ARO.Vehicles
{
    /// <summary>Third-person chase camera with a cockpit-height alternative (toggle with CameraToggled).</summary>
    public class FollowCamera : MonoBehaviour
    {
        public Transform target;
        public Vector3 chaseOffset = new Vector3(0f, 4.2f, -11f);
        public Vector3 cockpitOffset = new Vector3(-0.4f, 2.5f, 0.6f);
        public float positionLag = 6f, rotationLag = 5f, lookHeight = 2f;
        public bool cockpit;

        void LateUpdate()
        {
            if (target == null) return;
            if (cockpit)
            {
                transform.SetPositionAndRotation(target.TransformPoint(cockpitOffset), target.rotation);
                return;
            }
            // Follow yaw only, so pitch/roll from bumps does not make the view seasick.
            var yaw = Quaternion.Euler(0f, target.eulerAngles.y, 0f);
            var wantPos = target.position + yaw * chaseOffset;
            transform.position = Vector3.Lerp(transform.position, wantPos, 1f - Mathf.Exp(-positionLag * Time.deltaTime));
            var look = Quaternion.LookRotation(target.position + Vector3.up * lookHeight - transform.position);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-rotationLag * Time.deltaTime));
        }

        public void ToggleCockpit() => cockpit = !cockpit;
    }
}
