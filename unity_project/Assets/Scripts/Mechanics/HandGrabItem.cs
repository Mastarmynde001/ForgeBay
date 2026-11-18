using UnityEngine;
using UnityEngine.Events;

namespace ForgeBay.Interaction
{
    /// <summary>
    /// Manages picking up, holding, and releasing 3D objects in a hands-first VR environment.
    /// Designed for use with Meta Horizon Building Blocks (Grabbable / HandGrabInteractable).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class HandGrabItem : MonoBehaviour
    {
        [Header("Audio")]
        [Tooltip("Optional AudioSource used to play grab/release sounds.")]
        [SerializeField] private AudioSource audioSource;

        [Tooltip("Sound played when the item is grabbed.")]
        [SerializeField] private AudioClip grabSound;

        [Header("Events")]
        [Tooltip("Fired when the item is picked up.")]
        public UnityEvent onGrabbed;

        [Tooltip("Fired when the item is released.")]
        public UnityEvent onReleased;

        private Rigidbody rb;

        /// <summary>
        /// Whether the item is currently held by a hand.
        /// </summary>
        public bool isGrabbed { get; private set; }

        // ──────────────────────────────────────────────

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
        }

        // ──────────────────────────────────────────────
        //  Public API — wire these in the Inspector or
        //  call from Meta Interaction SDK events.
        // ──────────────────────────────────────────────

        /// <summary>
        /// Call when a hand grabs this item.
        /// Switches the Rigidbody to kinematic so physics do not
        /// fight hand tracking, plays an optional sound, and raises
        /// <see cref="onGrabbed"/>.
        /// </summary>
        public void OnHandGrab()
        {
            isGrabbed = true;

            rb.isKinematic = true;

            // Zero out residual velocity to prevent jitter on re-parent.
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            audioSource?.PlayOneShot(grabSound);

            onGrabbed?.Invoke();
        }

        /// <summary>
        /// Call when the hand releases this item.
        /// Restores dynamic physics with gravity so the object
        /// falls naturally, and raises <see cref="onReleased"/>.
        /// </summary>
        public void OnHandRelease()
        {
            isGrabbed = false;

            rb.isKinematic = false;
            rb.useGravity  = true;

            onReleased?.Invoke();
        }

        /// <summary>
        /// Explicitly sets the kinematic state of the Rigidbody.
        /// Use from external scripts such as SnapZones to lock or
        /// unlock physics when the object is socketed.
        /// </summary>
        /// <param name="state">
        /// <c>true</c> to make kinematic (locked); <c>false</c> to restore dynamics.
        /// </param>
        public void ForceSetKinematic(bool state)
        {
            rb.isKinematic = state;

            if (!state)
            {
                rb.useGravity = true;
            }
        }
    }
}