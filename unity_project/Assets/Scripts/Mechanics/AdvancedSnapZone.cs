using UnityEngine;
using UnityEngine.Events;

namespace ForgeBay.Interaction
{
    /// <summary>
    /// Hierarchical magnetic snap zone for complex robotics assembly.
    /// Detects tagged grabbable parts entering its trigger volume, locks them
    /// to an anchor transform, and cascades activation to dependent child zones.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [DisallowMultipleComponent]
    public class AdvancedSnapZone : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  Inspector Fields
        // ──────────────────────────────────────────────

        [Header("Socket Configuration")]

        [Tooltip("Tag that an incoming collider must carry to be accepted by this zone.")]
        public string acceptedTag = "RoboticsPart";

        [Tooltip("Transform the part aligns to. Falls back to this GameObject's transform if left empty.")]
        public Transform snapAnchor;

        [Tooltip("Optional AudioSource for snap/unsnap feedback.")]
        public AudioSource audioSource;

        [Tooltip("Sound played when a part snaps into this zone.")]
        public AudioClip snapSound;

        [Header("Nested Assembly Dependencies")]

        [Tooltip("Child snap zones that remain disabled until this zone is filled.")]
        public AdvancedSnapZone[] childSnapZonesToUnlock;

        [Header("State & Events")]

        [Tooltip("Fired when a valid part snaps into this zone.")]
        public UnityEvent onPartSnapped;

        [Tooltip("Fired when the snapped part is removed from this zone.")]
        public UnityEvent onPartRemoved;

        // ──────────────────────────────────────────────
        //  Read-Only State
        // ──────────────────────────────────────────────

        /// <summary>Whether a part is currently locked into this zone.</summary>
        public bool isOccupied { get; private set; }

        /// <summary>Reference to the currently snapped GameObject, or null.</summary>
        public GameObject snappedObject { get; private set; }

        // ──────────────────────────────────────────────
        //  Private
        // ──────────────────────────────────────────────

        private Collider zoneCollider;

        // ──────────────────────────────────────────────
        //  Lifecycle
        // ──────────────────────────────────────────────

        private void Awake()
        {
            zoneCollider = GetComponent<Collider>();
            zoneCollider.isTrigger = true;

            if (snapAnchor == null)
            {
                snapAnchor = transform;
            }

            DisableChildZones();
        }

        // ──────────────────────────────────────────────
        //  Trigger Detection
        // ──────────────────────────────────────────────

        private void OnTriggerEnter(Collider other)
        {
            if (isOccupied)
            {
                return;
            }

            if (!other.CompareTag(acceptedTag))
            {
                return;
            }

            SnapPart(other.gameObject);
        }

        // ──────────────────────────────────────────────
        //  Public API
        // ──────────────────────────────────────────────

        /// <summary>
        /// Removes the currently snapped part, restores its physics,
        /// and recursively unsnaps all dependent child zones.
        /// </summary>
        public void UnsnapCurrentPart()
        {
            if (!isOccupied)
            {
                return;
            }

            // Unparent and restore dynamics on the snapped object.
            if (snappedObject != null)
            {
                snappedObject.transform.SetParent(null);

                HandGrabItem grabItem = snappedObject.GetComponent<HandGrabItem>();
                grabItem?.ForceSetKinematic(false);
            }

            // Cascade: recursively unsnap and disable every child zone.
            if (childSnapZonesToUnlock != null)
            {
                for (int i = 0; i < childSnapZonesToUnlock.Length; i++)
                {
                    AdvancedSnapZone child = childSnapZonesToUnlock[i];
                    if (child == null)
                    {
                        continue;
                    }

                    child.UnsnapCurrentPart();
                    SetZoneColliderEnabled(child, false);
                }
            }

            snappedObject = null;
            isOccupied = false;

            onPartRemoved?.Invoke();
        }

        // ──────────────────────────────────────────────
        //  Internal Helpers
        // ──────────────────────────────────────────────

        /// <summary>
        /// Aligns the part to the snap anchor, locks its physics,
        /// parents it, activates child zones, and fires events.
        /// </summary>
        private void SnapPart(GameObject part)
        {
            isOccupied = true;
            snappedObject = part;

            // Align position and rotation to the anchor.
            Transform partTransform = part.transform;
            partTransform.SetPositionAndRotation(snapAnchor.position, snapAnchor.rotation);
            partTransform.SetParent(snapAnchor);

            // Lock physics through HandGrabItem if present.
            HandGrabItem grabItem = part.GetComponent<HandGrabItem>();
            grabItem?.ForceSetKinematic(true);

            // Enable dependent child snap zones for sequential assembly.
            EnableChildZones();

            // Audio feedback.
            if (snapSound != null)
            {
                audioSource?.PlayOneShot(snapSound);
            }

            onPartSnapped?.Invoke();
        }

        /// <summary>
        /// Enables the trigger collider on every valid child snap zone.
        /// </summary>
        private void EnableChildZones()
        {
            if (childSnapZonesToUnlock == null)
            {
                return;
            }

            for (int i = 0; i < childSnapZonesToUnlock.Length; i++)
            {
                SetZoneColliderEnabled(childSnapZonesToUnlock[i], true);
            }
        }

        /// <summary>
        /// Disables the trigger collider on every valid child snap zone
        /// to prevent out-of-order assembly.
        /// </summary>
        private void DisableChildZones()
        {
            if (childSnapZonesToUnlock == null)
            {
                return;
            }

            for (int i = 0; i < childSnapZonesToUnlock.Length; i++)
            {
                SetZoneColliderEnabled(childSnapZonesToUnlock[i], false);
            }
        }

        /// <summary>
        /// Safely toggles the trigger collider on a child snap zone.
        /// </summary>
        private static void SetZoneColliderEnabled(AdvancedSnapZone zone, bool enabled)
        {
            if (zone == null)
            {
                return;
            }

            Collider col = zone.GetComponent<Collider>();
            if (col != null)
            {
                col.enabled = enabled;
            }
        }
    }
}
