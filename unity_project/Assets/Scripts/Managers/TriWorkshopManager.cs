using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace ForgeBay.Managers
{
    /// <summary>
    /// Serialisable per-workshop progression container.
    /// Each stage tracks a set of required snap zones and an optional
    /// rotation count before the stage is considered complete.
    /// </summary>
    [System.Serializable]
    public struct WorkshopStage
    {
        [Tooltip("Human-readable label for this stage (e.g. 'Engine Rebuild').")]
        public string stageName;

        [Tooltip("Snap zones that must all be occupied for this stage to pass.")]
        public Interaction.AdvancedSnapZone[] requiredSnapZones;

        [Tooltip("Number of RotatablePart limit events required (e.g. bolts tightened). Set to 0 to ignore.")]
        public int requiredRotationsCount;

        [Tooltip("Runtime flag — true once every requirement in this stage is satisfied.")]
        [HideInInspector]
        public bool isCompleted;

        [Tooltip("Fired once when this stage is marked complete.")]
        public UnityEvent onStageCompleted;
    }

    /// <summary>
    /// Central game manager that coordinates stage progression, victory
    /// conditions, and state saving across the Mechanical, Electronics,
    /// and Robotics workshop environments.
    /// </summary>
    [DisallowMultipleComponent]
    public class TriWorkshopManager : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  Inspector Fields
        // ──────────────────────────────────────────────

        [Header("Workshop Stages")]

        public WorkshopStage mechanicalStage;
        public WorkshopStage electronicsStage;
        public WorkshopStage roboticsStage;

        [Header("Global FX & Audio")]

        [Tooltip("Shared AudioSource for victory chimes.")]
        public AudioSource audioSource;

        [Tooltip("Sound played when a single workshop stage is completed.")]
        public AudioClip stageCompleteSound;

        [Tooltip("Sound played when ALL three workshops are completed.")]
        public AudioClip grandTriumphSound;

        [Header("Global Events")]

        [Tooltip("Fires when any single workshop is cleared. Passes the workshop index (0, 1, or 2).")]
        public UnityEvent<int> onWorkshopCleared;

        [Tooltip("Fires once when all three workshops are completed.")]
        public UnityEvent onAllWorkshopsCompleted;

        // ──────────────────────────────────────────────
        //  Private State
        // ──────────────────────────────────────────────

        private int mechanicalRotationCount;
        private int electronicsRotationCount;
        private int roboticsRotationCount;

        private bool grandTriumphFired;

        // ──────────────────────────────────────────────
        //  Lifecycle
        // ──────────────────────────────────────────────

        private void Start()
        {
            SubscribeSnapZoneEvents(mechanicalStage.requiredSnapZones, OnMechanicalSnapChanged);
            SubscribeSnapZoneEvents(electronicsStage.requiredSnapZones, OnElectronicsSnapChanged);
            SubscribeSnapZoneEvents(roboticsStage.requiredSnapZones, OnRoboticsSnapChanged);
        }

        // ──────────────────────────────────────────────
        //  Public API — Rotation Completion Signals
        // ──────────────────────────────────────────────

        /// <summary>
        /// Called externally by a <see cref="Interaction.RotatablePart"/>
        /// <c>onMaxLimitReached</c> event in the Mechanical workshop to
        /// register a bolt fully tightened.
        /// </summary>
        public void RegisterMechanicalRotation()
        {
            mechanicalRotationCount++;
            CheckMechanicalProgress();
        }

        /// <summary>
        /// Called externally by a <see cref="Interaction.RotatablePart"/>
        /// event in the Electronics workshop.
        /// </summary>
        public void RegisterElectronicsRotation()
        {
            electronicsRotationCount++;
            CheckElectronicsProgress();
        }

        /// <summary>
        /// Called externally by a <see cref="Interaction.RotatablePart"/>
        /// event in the Robotics workshop.
        /// </summary>
        public void RegisterRoboticsRotation()
        {
            roboticsRotationCount++;
            CheckRoboticsProgress();
        }

        // ──────────────────────────────────────────────
        //  Public API — Debug / Cheat Utilities
        // ──────────────────────────────────────────────

        /// <summary>
        /// Forces a specific workshop to the completed state.
        /// Useful for QA testing and cheat-code flows.
        /// </summary>
        /// <param name="index">Workshop index (0 = Mechanical, 1 = Electronics, 2 = Robotics).</param>
        public void ForceCompleteWorkshop(int index)
        {
            switch (index)
            {
                case 0:
                    MarkStageComplete(ref mechanicalStage, 0);
                    break;
                case 1:
                    MarkStageComplete(ref electronicsStage, 1);
                    break;
                case 2:
                    MarkStageComplete(ref roboticsStage, 2);
                    break;
                default:
                    Debug.LogWarning($"[TriWorkshopManager] Invalid workshop index: {index}");
                    break;
            }
        }

        /// <summary>
        /// Resets all workshop progression state to initial values.
        /// Does NOT unsnap physical parts — call each snap zone's
        /// <c>UnsnapCurrentPart()</c> separately if needed.
        /// </summary>
        public void ResetAllProgress()
        {
            mechanicalStage.isCompleted = false;
            electronicsStage.isCompleted = false;
            roboticsStage.isCompleted = false;

            mechanicalRotationCount = 0;
            electronicsRotationCount = 0;
            roboticsRotationCount = 0;

            grandTriumphFired = false;

            Debug.Log("[TriWorkshopManager] All progress reset.");
        }

        // ──────────────────────────────────────────────
        //  Snap Zone Event Handlers
        // ──────────────────────────────────────────────

        private void OnMechanicalSnapChanged()
        {
            CheckMechanicalProgress();
        }

        private void OnElectronicsSnapChanged()
        {
            CheckElectronicsProgress();
        }

        private void OnRoboticsSnapChanged()
        {
            CheckRoboticsProgress();
        }

        // ──────────────────────────────────────────────
        //  Progress Verification
        // ──────────────────────────────────────────────

        /// <summary>
        /// Evaluates whether all engine components are snapped and bolts
        /// tightened for the Mechanical workshop.
        /// </summary>
        private void CheckMechanicalProgress()
        {
            if (mechanicalStage.isCompleted)
            {
                return;
            }

            if (!AllSnapZonesOccupied(mechanicalStage.requiredSnapZones))
            {
                return;
            }

            if (mechanicalStage.requiredRotationsCount > 0 &&
                mechanicalRotationCount < mechanicalStage.requiredRotationsCount)
            {
                return;
            }

            MarkStageComplete(ref mechanicalStage, 0);
        }

        /// <summary>
        /// Evaluates whether all breadboard sockets are occupied for the
        /// Electronics workshop.
        /// </summary>
        private void CheckElectronicsProgress()
        {
            if (electronicsStage.isCompleted)
            {
                return;
            }

            if (!AllSnapZonesOccupied(electronicsStage.requiredSnapZones))
            {
                return;
            }

            if (electronicsStage.requiredRotationsCount > 0 &&
                electronicsRotationCount < electronicsStage.requiredRotationsCount)
            {
                return;
            }

            MarkStageComplete(ref electronicsStage, 1);
        }

        /// <summary>
        /// Evaluates whether all bionic arm servos and plates are locked in
        /// the hierarchical assembly for the Robotics workshop.
        /// </summary>
        private void CheckRoboticsProgress()
        {
            if (roboticsStage.isCompleted)
            {
                return;
            }

            if (!AllSnapZonesOccupied(roboticsStage.requiredSnapZones))
            {
                return;
            }

            if (roboticsStage.requiredRotationsCount > 0 &&
                roboticsRotationCount < roboticsStage.requiredRotationsCount)
            {
                return;
            }

            MarkStageComplete(ref roboticsStage, 2);
        }

        // ──────────────────────────────────────────────
        //  Stage Completion
        // ──────────────────────────────────────────────

        /// <summary>
        /// Marks a workshop stage as complete, plays audio feedback,
        /// fires per-stage and global events, and checks for grand triumph.
        /// </summary>
        private void MarkStageComplete(ref WorkshopStage stage, int workshopIndex)
        {
            if (stage.isCompleted)
            {
                return;
            }

            stage.isCompleted = true;

            Debug.Log($"[TriWorkshopManager] Stage '{stage.stageName}' (Workshop {workshopIndex}) complete!");

            if (stageCompleteSound != null)
            {
                audioSource?.PlayOneShot(stageCompleteSound);
            }

            stage.onStageCompleted?.Invoke();
            onWorkshopCleared?.Invoke(workshopIndex);

            CheckGrandTriumph();
        }

        /// <summary>
        /// Checks if all three workshops are complete. If so, plays the
        /// grand triumph sound and fires the global completion event once.
        /// </summary>
        private void CheckGrandTriumph()
        {
            if (grandTriumphFired)
            {
                return;
            }

            if (!mechanicalStage.isCompleted ||
                !electronicsStage.isCompleted ||
                !roboticsStage.isCompleted)
            {
                return;
            }

            grandTriumphFired = true;

            Debug.Log("[TriWorkshopManager] ★ GRAND TRIUMPH — All workshops completed! ★");

            if (grandTriumphSound != null)
            {
                audioSource?.PlayOneShot(grandTriumphSound);
            }

            onAllWorkshopsCompleted?.Invoke();
        }

        // ──────────────────────────────────────────────
        //  Helpers
        // ──────────────────────────────────────────────

        /// <summary>
        /// Returns true only if every snap zone in the array is non-null
        /// and currently occupied.
        /// </summary>
        private static bool AllSnapZonesOccupied(Interaction.AdvancedSnapZone[] zones)
        {
            if (zones == null || zones.Length == 0)
            {
                // No zones required — pass by default.
                return true;
            }

            for (int i = 0; i < zones.Length; i++)
            {
                if (zones[i] == null)
                {
                    continue;
                }

                if (!zones[i].isOccupied)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Subscribes an internal listener to both <c>onPartSnapped</c> and
        /// <c>onPartRemoved</c> on every snap zone in the array so progress
        /// is re-evaluated whenever any socket state changes.
        /// </summary>
        private static void SubscribeSnapZoneEvents(
            Interaction.AdvancedSnapZone[] zones,
            UnityAction listener)
        {
            if (zones == null)
            {
                return;
            }

            for (int i = 0; i < zones.Length; i++)
            {
                Interaction.AdvancedSnapZone zone = zones[i];

                if (zone == null)
                {
                    continue;
                }

                zone.onPartSnapped?.AddListener(listener);
                zone.onPartRemoved?.AddListener(listener);
            }
        }
    }
}
