using UnityEngine;
using UnityEngine.Events;

namespace ForgeBay.Interaction
{
    /// <summary>
    /// Defines which local axis the part rotates around.
    /// </summary>
    public enum RotationAxis
    {
        LocalX,
        LocalY,
        LocalZ
    }

    /// <summary>
    /// Single-axis rotatable part for dials, knobs, valves, bolts, and gears.
    /// Computes rotational delta from hand/wrist input, enforces optional angle
    /// limits, outputs a normalised 0–1 value, and plays ratchet tick audio at
    /// configurable step intervals.
    /// </summary>
    [DisallowMultipleComponent]
    public class RotatablePart : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  Inspector Fields
        // ──────────────────────────────────────────────

        [Header("Rotation Settings")]

        [Tooltip("Local axis this part rotates around.")]
        public RotationAxis selectedAxis = RotationAxis.LocalZ;

        [Tooltip("Multiplier applied to incoming hand/wrist rotation delta.")]
        public float rotationSensitivity = 1.0f;

        [Tooltip("When true, rotation is clamped between minAngle and maxAngle.")]
        public bool useAngleLimits = false;

        [Tooltip("Minimum rotation boundary in degrees (inclusive).")]
        public float minAngle = 0.0f;

        [Tooltip("Maximum rotation boundary in degrees (inclusive).")]
        public float maxAngle = 180.0f;

        [Header("Feedback & Audio")]

        [Tooltip("Degrees of cumulative rotation between each ratchet tick. Set to 0 to disable.")]
        public float tickIntervalAngle = 15.0f;

        [Tooltip("Optional AudioSource for ratchet tick playback.")]
        public AudioSource audioSource;

        [Tooltip("Sound played at every tick interval threshold crossing.")]
        public AudioClip ratchetTickSound;

        [Header("Events")]

        [Tooltip("Emits a normalised float [0.0 – 1.0] whenever rotation changes.")]
        public UnityEvent<float> onValueChanged;

        [Tooltip("Fired once when the part reaches or passes the minimum angle limit.")]
        public UnityEvent onMinLimitReached;

        [Tooltip("Fired once when the part reaches or passes the maximum angle limit.")]
        public UnityEvent onMaxLimitReached;

        // ──────────────────────────────────────────────
        //  Read-Only State
        // ──────────────────────────────────────────────

        /// <summary>Current rotation in degrees along the selected axis.</summary>
        public float currentAngle { get; private set; }

        /// <summary>Normalised value (0–1) mapped from minAngle to maxAngle.</summary>
        public float normalizedValue { get; private set; }

        // ──────────────────────────────────────────────
        //  Private State
        // ──────────────────────────────────────────────

        private bool isHeld;
        private float accumulatedTickDelta;
        private bool wasAtMin;
        private bool wasAtMax;

        // ──────────────────────────────────────────────
        //  Lifecycle
        // ──────────────────────────────────────────────

        private void Awake()
        {
            currentAngle = ReadLocalAxisAngle();
            UpdateNormalizedValue();
        }

        // ──────────────────────────────────────────────
        //  Public API — Meta Building Block Event Binding
        // ──────────────────────────────────────────────

        /// <summary>
        /// Call when the user's hand grabs or pinches this part.
        /// Enables rotation delta processing.
        /// </summary>
        public void OnHandGrab()
        {
            isHeld = true;
            accumulatedTickDelta = 0.0f;
        }

        /// <summary>
        /// Call when the user's hand releases this part.
        /// Disables rotation delta processing.
        /// </summary>
        public void OnHandRelease()
        {
            isHeld = false;
        }

        /// <summary>
        /// Applies an incremental rotation in degrees, scaled by
        /// <see cref="rotationSensitivity"/>.  Clamps within limits if
        /// enabled, updates normalised output, and triggers ratchet ticks.
        /// </summary>
        /// <param name="deltaDegrees">Raw rotation delta from hand/wrist tracking.</param>
        public void ApplyRotationDelta(float deltaDegrees)
        {
            if (!isHeld)
            {
                return;
            }

            float scaledDelta = deltaDegrees * rotationSensitivity;
            float previousAngle = currentAngle;

            currentAngle += scaledDelta;

            if (useAngleLimits)
            {
                currentAngle = Mathf.Clamp(currentAngle, minAngle, maxAngle);
            }

            ApplyLocalRotation();
            UpdateNormalizedValue();

            onValueChanged?.Invoke(normalizedValue);

            // Limit-reached events (fire once per boundary contact).
            if (useAngleLimits)
            {
                CheckLimitEvents();
            }

            // Ratchet tick audio.
            ProcessTickAudio(currentAngle - previousAngle);
        }

        /// <summary>
        /// Programmatically sets the rotation to a specific angle,
        /// updating the visual transform, normalised value, and events
        /// without requiring hand input.
        /// </summary>
        /// <param name="targetAngle">Desired angle in degrees.</param>
        public void SetRotationDirectly(float targetAngle)
        {
            if (useAngleLimits)
            {
                targetAngle = Mathf.Clamp(targetAngle, minAngle, maxAngle);
            }

            float previousAngle = currentAngle;
            currentAngle = targetAngle;

            ApplyLocalRotation();
            UpdateNormalizedValue();

            onValueChanged?.Invoke(normalizedValue);

            if (useAngleLimits)
            {
                CheckLimitEvents();
            }

            ProcessTickAudio(currentAngle - previousAngle);
        }

        // ──────────────────────────────────────────────
        //  Internal Helpers
        // ──────────────────────────────────────────────

        /// <summary>
        /// Writes <see cref="currentAngle"/> into the transform's local
        /// rotation on the selected axis while preserving the other two axes.
        /// Uses <see cref="Quaternion.AngleAxis"/> to avoid Euler gimbal
        /// and 0°/360° wraparound glitches.
        /// </summary>
        private void ApplyLocalRotation()
        {
            Vector3 axis = GetAxisVector();
            transform.localRotation = Quaternion.AngleAxis(currentAngle, axis);
        }

        /// <summary>
        /// Reads the initial local Euler component matching
        /// <see cref="selectedAxis"/>.
        /// </summary>
        private float ReadLocalAxisAngle()
        {
            Vector3 euler = transform.localEulerAngles;

            return selectedAxis switch
            {
                RotationAxis.LocalX => NormalizeEuler(euler.x),
                RotationAxis.LocalY => NormalizeEuler(euler.y),
                RotationAxis.LocalZ => NormalizeEuler(euler.z),
                _ => 0.0f
            };
        }

        /// <summary>
        /// Returns the unit vector for the selected local axis.
        /// </summary>
        private Vector3 GetAxisVector()
        {
            return selectedAxis switch
            {
                RotationAxis.LocalX => Vector3.right,
                RotationAxis.LocalY => Vector3.up,
                RotationAxis.LocalZ => Vector3.forward,
                _ => Vector3.forward
            };
        }

        /// <summary>
        /// Converts a Unity Euler component (0–360) into a signed range
        /// (−180 to +180) to prevent jump glitches at the 0°/360° boundary.
        /// </summary>
        private static float NormalizeEuler(float angle)
        {
            if (angle > 180.0f)
            {
                angle -= 360.0f;
            }

            return angle;
        }

        /// <summary>
        /// Recomputes <see cref="normalizedValue"/> from the current angle
        /// mapped between minAngle and maxAngle.
        /// </summary>
        private void UpdateNormalizedValue()
        {
            float range = maxAngle - minAngle;

            if (Mathf.Approximately(range, 0.0f))
            {
                normalizedValue = 0.0f;
                return;
            }

            normalizedValue = Mathf.Clamp01((currentAngle - minAngle) / range);
        }

        /// <summary>
        /// Fires limit-reached events once when the angle first touches
        /// a boundary, and resets when it moves away.
        /// </summary>
        private void CheckLimitEvents()
        {
            bool atMin = currentAngle <= minAngle;
            bool atMax = currentAngle >= maxAngle;

            if (atMin && !wasAtMin)
            {
                onMinLimitReached?.Invoke();
            }

            if (atMax && !wasAtMax)
            {
                onMaxLimitReached?.Invoke();
            }

            wasAtMin = atMin;
            wasAtMax = atMax;
        }

        /// <summary>
        /// Accumulates angular delta and plays a ratchet tick each time
        /// the cumulative movement crosses a <see cref="tickIntervalAngle"/>
        /// threshold.
        /// </summary>
        private void ProcessTickAudio(float angleDelta)
        {
            if (tickIntervalAngle <= 0.0f)
            {
                return;
            }

            accumulatedTickDelta += Mathf.Abs(angleDelta);

            while (accumulatedTickDelta >= tickIntervalAngle)
            {
                accumulatedTickDelta -= tickIntervalAngle;

                if (ratchetTickSound != null)
                {
                    audioSource?.PlayOneShot(ratchetTickSound);
                }
            }
        }
    }
}
