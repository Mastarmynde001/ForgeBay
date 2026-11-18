using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ForgeBay.Environment
{
    /// <summary>
    /// Smoothly cycles between Mechanical, Electronics, and Robotics workshop
    /// environments using horizontal hand-swipe gestures or keyboard fallback
    /// inputs.  Drives a full-screen Canvas fade-to-black transition to prevent
    /// motion sickness in VR and web viewports.
    /// </summary>
    [DisallowMultipleComponent]
    public class SwipeWorkspaceManager : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  Inspector Fields
        // ──────────────────────────────────────────────

        [Header("Workspace Setup")]

        [Tooltip("Root GameObjects for each workshop (0 = Mechanical, 1 = Electronics, 2 = Robotics).")]
        public GameObject[] workshopRoots;

        [Header("Transition FX")]

        [Tooltip("Full-screen UI CanvasGroup used for fade-to-black transitions.")]
        public CanvasGroup fadeCanvasGroup;

        [Tooltip("Duration in seconds for each half of the fade (fade-out + fade-in).")]
        public float fadeDuration = 0.35f;

        [Tooltip("Optional AudioSource for transition sound effects.")]
        public AudioSource audioSource;

        [Tooltip("Whoosh sound played at the start of every workspace switch.")]
        public AudioClip swipeWhooshSound;

        [Header("Input Settings")]

        [Tooltip("Transform of the primary tracked hand or controller used for swipe detection.")]
        public Transform trackingHand;

        [Tooltip("Minimum horizontal velocity (m/s) to register as a swipe gesture.")]
        public float swipeVelocityThreshold = 1.8f;

        [Tooltip("Enable Left/Right Arrow and A/D keys for WebGL browser testing.")]
        public bool enableKeyboardFallback = true;

        [Header("Events")]

        [Tooltip("Fires after a workshop transition completes. Passes the new workshop index (0, 1, or 2).")]
        public UnityEvent<int> onWorkshopChanged;

        // ──────────────────────────────────────────────
        //  Read-Only State
        // ──────────────────────────────────────────────

        /// <summary>Index of the currently active workshop (0 = Mechanical, 1 = Electronics, 2 = Robotics).</summary>
        public int currentWorkshopIndex { get; private set; }

        // ──────────────────────────────────────────────
        //  Private State
        // ──────────────────────────────────────────────

        private bool isTransitioning;
        private Vector3 previousHandPosition;
        private bool hasPreviousHandPosition;

        // ──────────────────────────────────────────────
        //  Lifecycle
        // ──────────────────────────────────────────────

        private void Start()
        {
            // Initialise the fade overlay to fully transparent.
            if (fadeCanvasGroup != null)
            {
                fadeCanvasGroup.alpha = 0.0f;
                fadeCanvasGroup.blocksRaycasts = false;
                fadeCanvasGroup.interactable = false;
            }

            // Activate only the first workshop.
            ActivateWorkshopExclusive(0);
            currentWorkshopIndex = 0;
        }

        private void Update()
        {
            if (isTransitioning)
            {
                return;
            }

            DetectHandSwipe();
            DetectKeyboardInput();
        }

        // ──────────────────────────────────────────────
        //  Public API
        // ──────────────────────────────────────────────

        /// <summary>
        /// Requests a workspace change in the given direction.
        /// The index wraps cleanly around the workshop array length.
        /// </summary>
        /// <param name="direction">+1 for next workshop, −1 for previous.</param>
        public void RequestWorkspaceChange(int direction)
        {
            if (isTransitioning)
            {
                return;
            }

            if (workshopRoots == null || workshopRoots.Length == 0)
            {
                return;
            }

            int count = workshopRoots.Length;
            int targetIndex = ((currentWorkshopIndex + direction) % count + count) % count;

            if (targetIndex == currentWorkshopIndex)
            {
                return;
            }

            StartCoroutine(TransitionRoutine(targetIndex));
        }

        /// <summary>
        /// Jumps directly to a workshop by index with a full fade transition.
        /// </summary>
        /// <param name="targetIndex">Workshop index to switch to.</param>
        public void GoToWorkshop(int targetIndex)
        {
            if (isTransitioning)
            {
                return;
            }

            if (workshopRoots == null || workshopRoots.Length == 0)
            {
                return;
            }

            targetIndex = Mathf.Clamp(targetIndex, 0, workshopRoots.Length - 1);

            if (targetIndex == currentWorkshopIndex)
            {
                return;
            }

            StartCoroutine(TransitionRoutine(targetIndex));
        }

        // ──────────────────────────────────────────────
        //  Input Detection
        // ──────────────────────────────────────────────

        /// <summary>
        /// Computes horizontal hand velocity across frames and triggers a
        /// workspace change when velocity exceeds the threshold.
        /// </summary>
        private void DetectHandSwipe()
        {
            if (trackingHand == null)
            {
                hasPreviousHandPosition = false;
                return;
            }

            Vector3 currentPosition = trackingHand.position;

            if (!hasPreviousHandPosition)
            {
                previousHandPosition = currentPosition;
                hasPreviousHandPosition = true;
                return;
            }

            float deltaTime = Time.deltaTime;

            if (deltaTime <= 0.0f)
            {
                previousHandPosition = currentPosition;
                return;
            }

            float horizontalVelocity = (currentPosition.x - previousHandPosition.x) / deltaTime;
            previousHandPosition = currentPosition;

            if (horizontalVelocity < -swipeVelocityThreshold)
            {
                // Swipe left → next workshop.
                RequestWorkspaceChange(1);
            }
            else if (horizontalVelocity > swipeVelocityThreshold)
            {
                // Swipe right → previous workshop.
                RequestWorkspaceChange(-1);
            }
        }

        /// <summary>
        /// Polls keyboard inputs for WebGL / browser testing fallback.
        /// </summary>
        private void DetectKeyboardInput()
        {
            if (!enableKeyboardFallback)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
            {
                RequestWorkspaceChange(1);
            }
            else if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))
            {
                RequestWorkspaceChange(-1);
            }
        }

        // ──────────────────────────────────────────────
        //  Transition Coroutine
        // ──────────────────────────────────────────────

        /// <summary>
        /// Fades to black, swaps the active workshop root, then fades back in.
        /// </summary>
        private IEnumerator TransitionRoutine(int targetIndex)
        {
            isTransitioning = true;

            // Play transition audio.
            if (swipeWhooshSound != null)
            {
                audioSource?.PlayOneShot(swipeWhooshSound);
            }

            // ── Fade Out (transparent → opaque) ──
            yield return FadeCanvasGroup(0.0f, 1.0f);

            // Swap active workshops at the mid-point while the screen is black.
            ActivateWorkshopExclusive(targetIndex);
            currentWorkshopIndex = targetIndex;

            // Notify listeners.
            onWorkshopChanged?.Invoke(currentWorkshopIndex);

            // ── Fade In (opaque → transparent) ──
            yield return FadeCanvasGroup(1.0f, 0.0f);

            // Restore interactivity.
            if (fadeCanvasGroup != null)
            {
                fadeCanvasGroup.blocksRaycasts = false;
                fadeCanvasGroup.interactable = false;
            }

            isTransitioning = false;
        }

        // ──────────────────────────────────────────────
        //  Helpers
        // ──────────────────────────────────────────────

        /// <summary>
        /// Linearly interpolates the fade overlay's alpha between two values
        /// over <see cref="fadeDuration"/> seconds.
        /// </summary>
        private IEnumerator FadeCanvasGroup(float from, float to)
        {
            if (fadeCanvasGroup == null)
            {
                yield break;
            }

            // Block raycasts during the fade to prevent accidental UI interaction.
            fadeCanvasGroup.blocksRaycasts = true;
            fadeCanvasGroup.interactable = false;

            float elapsed = 0.0f;
            float safeDuration = Mathf.Max(fadeDuration, 0.01f);

            while (elapsed < safeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / safeDuration);
                fadeCanvasGroup.alpha = Mathf.Lerp(from, to, t);
                yield return null;
            }

            fadeCanvasGroup.alpha = to;
        }

        /// <summary>
        /// Activates only the workshop at the given index and deactivates all others.
        /// Null entries in the array are safely skipped.
        /// </summary>
        private void ActivateWorkshopExclusive(int activeIndex)
        {
            if (workshopRoots == null)
            {
                return;
            }

            for (int i = 0; i < workshopRoots.Length; i++)
            {
                if (workshopRoots[i] != null)
                {
                    workshopRoots[i].SetActive(i == activeIndex);
                }
            }
        }
    }
}
