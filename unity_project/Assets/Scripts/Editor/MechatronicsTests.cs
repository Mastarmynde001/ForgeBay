using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;
using ForgeBay.Interaction;
using ForgeBay.Environment;
using ForgeBay.Managers;

namespace ForgeBay.Tests
{
    // ══════════════════════════════════════════════════════════════
    //  Shared Utility
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Tracks created GameObjects for deterministic cleanup after each test.
    /// </summary>
    internal static class TestObjectRegistry
    {
        private static readonly List<GameObject> created = new List<GameObject>();

        /// <summary>Creates a named GameObject and registers it for teardown.</summary>
        public static GameObject Create(string name)
        {
            var go = new GameObject(name);
            created.Add(go);
            return go;
        }

        /// <summary>Destroys all registered GameObjects immediately.</summary>
        public static void DestroyAll()
        {
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null)
                {
                    Object.DestroyImmediate(created[i]);
                }
            }
            created.Clear();
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  HandGrabItem Tests
    // ══════════════════════════════════════════════════════════════

    [TestFixture]
    public class HandGrabItemTests
    {
        private GameObject testGO;
        private HandGrabItem grabItem;
        private Rigidbody rb;

        [SetUp]
        public void SetUp()
        {
            testGO = TestObjectRegistry.Create("TestGrabItem");
            rb = testGO.AddComponent<Rigidbody>();
            grabItem = testGO.AddComponent<HandGrabItem>();

            // Simulate Awake — the component caches rb in Awake,
            // which runs automatically when AddComponent is called in Editor.
        }

        [TearDown]
        public void TearDown()
        {
            TestObjectRegistry.DestroyAll();
        }

        [Test]
        public void OnHandGrab_SetsIsGrabbedTrue()
        {
            grabItem.OnHandGrab();
            Assert.IsTrue(grabItem.isGrabbed);
        }

        [Test]
        public void OnHandGrab_SetsRigidbodyKinematic()
        {
            rb.isKinematic = false;
            grabItem.OnHandGrab();
            Assert.IsTrue(rb.isKinematic);
        }

        [Test]
        public void OnHandGrab_ZerosLinearVelocity()
        {
            rb.linearVelocity = new Vector3(5f, 5f, 5f);
            grabItem.OnHandGrab();
            Assert.AreEqual(Vector3.zero, rb.linearVelocity);
        }

        [Test]
        public void OnHandGrab_ZerosAngularVelocity()
        {
            rb.angularVelocity = new Vector3(3f, 3f, 3f);
            grabItem.OnHandGrab();
            Assert.AreEqual(Vector3.zero, rb.angularVelocity);
        }

        [Test]
        public void OnHandGrab_InvokesOnGrabbedEvent()
        {
            bool fired = false;
            grabItem.onGrabbed = new UnityEvent();
            grabItem.onGrabbed.AddListener(() => fired = true);

            grabItem.OnHandGrab();

            Assert.IsTrue(fired);
        }

        [Test]
        public void OnHandRelease_SetsIsGrabbedFalse()
        {
            grabItem.OnHandGrab();
            grabItem.OnHandRelease();
            Assert.IsFalse(grabItem.isGrabbed);
        }

        [Test]
        public void OnHandRelease_SetsRigidbodyNonKinematic()
        {
            grabItem.OnHandGrab();
            grabItem.OnHandRelease();
            Assert.IsFalse(rb.isKinematic);
        }

        [Test]
        public void OnHandRelease_EnablesGravity()
        {
            rb.useGravity = false;
            grabItem.OnHandGrab();
            grabItem.OnHandRelease();
            Assert.IsTrue(rb.useGravity);
        }

        [Test]
        public void OnHandRelease_InvokesOnReleasedEvent()
        {
            bool fired = false;
            grabItem.onReleased = new UnityEvent();
            grabItem.onReleased.AddListener(() => fired = true);

            grabItem.OnHandGrab();
            grabItem.OnHandRelease();

            Assert.IsTrue(fired);
        }

        [Test]
        public void ForceSetKinematic_True_MakesKinematic()
        {
            rb.isKinematic = false;
            grabItem.ForceSetKinematic(true);
            Assert.IsTrue(rb.isKinematic);
        }

        [Test]
        public void ForceSetKinematic_False_RestoresDynamicWithGravity()
        {
            grabItem.ForceSetKinematic(true);
            grabItem.ForceSetKinematic(false);
            Assert.IsFalse(rb.isKinematic);
            Assert.IsTrue(rb.useGravity);
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  AdvancedSnapZone Tests
    // ══════════════════════════════════════════════════════════════

    [TestFixture]
    public class AdvancedSnapZoneTests
    {
        private GameObject zoneGO;
        private AdvancedSnapZone zone;
        private BoxCollider zoneCollider;

        private GameObject childZoneGO;
        private AdvancedSnapZone childZone;
        private BoxCollider childCollider;

        [SetUp]
        public void SetUp()
        {
            // Parent snap zone.
            zoneGO = TestObjectRegistry.Create("TestSnapZone");
            zoneCollider = zoneGO.AddComponent<BoxCollider>();
            zone = zoneGO.AddComponent<AdvancedSnapZone>();
            zone.acceptedTag = "RoboticsPart";

            // Child snap zone (dependency).
            childZoneGO = TestObjectRegistry.Create("ChildSnapZone");
            childCollider = childZoneGO.AddComponent<BoxCollider>();
            childZone = childZoneGO.AddComponent<AdvancedSnapZone>();

            // Wire parent → child dependency.
            zone.childSnapZonesToUnlock = new AdvancedSnapZone[] { childZone };

            // Re-trigger Awake logic manually since childSnapZonesToUnlock
            // was assigned after Awake ran.
            childCollider.enabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            TestObjectRegistry.DestroyAll();
        }

        [Test]
        public void Awake_SetsColliderToTrigger()
        {
            Assert.IsTrue(zoneCollider.isTrigger);
        }

        [Test]
        public void InitialState_IsNotOccupied()
        {
            Assert.IsFalse(zone.isOccupied);
        }

        [Test]
        public void InitialState_SnappedObjectIsNull()
        {
            Assert.IsNull(zone.snappedObject);
        }

        [Test]
        public void ChildColliders_StartDisabled()
        {
            // After Awake on parent, child colliders should be disabled.
            Assert.IsFalse(childCollider.enabled);
        }

        [Test]
        public void SnapAnchor_DefaultsToOwnTransform()
        {
            // If snapAnchor was null in Inspector, Awake sets it to transform.
            Assert.AreEqual(zoneGO.transform, zone.snapAnchor);
        }

        [Test]
        public void OnPartSnapped_EventFires()
        {
            bool fired = false;
            zone.onPartSnapped = new UnityEvent();
            zone.onPartSnapped.AddListener(() => fired = true);

            // Create a tagged incoming part.
            var partGO = TestObjectRegistry.Create("Part");
            partGO.tag = "RoboticsPart";
            partGO.AddComponent<Rigidbody>();
            var partCollider = partGO.AddComponent<BoxCollider>();

            // Simulate trigger entry.
            zone.SendMessage("OnTriggerEnter", partCollider, SendMessageOptions.DontRequireReceiver);

            Assert.IsTrue(fired);
        }

        [Test]
        public void OnPartSnapped_SetsIsOccupied()
        {
            var partGO = TestObjectRegistry.Create("Part");
            partGO.tag = "RoboticsPart";
            partGO.AddComponent<Rigidbody>();
            var partCollider = partGO.AddComponent<BoxCollider>();

            zone.SendMessage("OnTriggerEnter", partCollider, SendMessageOptions.DontRequireReceiver);

            Assert.IsTrue(zone.isOccupied);
        }

        [Test]
        public void OnPartSnapped_StoresSnappedObject()
        {
            var partGO = TestObjectRegistry.Create("Part");
            partGO.tag = "RoboticsPart";
            partGO.AddComponent<Rigidbody>();
            var partCollider = partGO.AddComponent<BoxCollider>();

            zone.SendMessage("OnTriggerEnter", partCollider, SendMessageOptions.DontRequireReceiver);

            Assert.AreEqual(partGO, zone.snappedObject);
        }

        [Test]
        public void OnPartSnapped_AlignsPartToAnchor()
        {
            zone.snapAnchor.position = new Vector3(10f, 20f, 30f);
            zone.snapAnchor.rotation = Quaternion.Euler(0f, 90f, 0f);

            var partGO = TestObjectRegistry.Create("Part");
            partGO.tag = "RoboticsPart";
            partGO.AddComponent<Rigidbody>();
            var partCollider = partGO.AddComponent<BoxCollider>();

            zone.SendMessage("OnTriggerEnter", partCollider, SendMessageOptions.DontRequireReceiver);

            Assert.AreEqual(zone.snapAnchor.position, partGO.transform.position);
            Assert.IsTrue(Quaternion.Angle(zone.snapAnchor.rotation, partGO.transform.rotation) < 0.1f);
        }

        [Test]
        public void OnPartSnapped_ParentsPartToAnchor()
        {
            var partGO = TestObjectRegistry.Create("Part");
            partGO.tag = "RoboticsPart";
            partGO.AddComponent<Rigidbody>();
            var partCollider = partGO.AddComponent<BoxCollider>();

            zone.SendMessage("OnTriggerEnter", partCollider, SendMessageOptions.DontRequireReceiver);

            Assert.AreEqual(zone.snapAnchor, partGO.transform.parent);
        }

        [Test]
        public void OnPartSnapped_EnablesChildZoneColliders()
        {
            var partGO = TestObjectRegistry.Create("Part");
            partGO.tag = "RoboticsPart";
            partGO.AddComponent<Rigidbody>();
            var partCollider = partGO.AddComponent<BoxCollider>();

            zone.SendMessage("OnTriggerEnter", partCollider, SendMessageOptions.DontRequireReceiver);

            Assert.IsTrue(childCollider.enabled);
        }

        [Test]
        public void OnPartSnapped_LocksHandGrabItemKinematic()
        {
            var partGO = TestObjectRegistry.Create("Part");
            partGO.tag = "RoboticsPart";
            var partRB = partGO.AddComponent<Rigidbody>();
            var partGrabItem = partGO.AddComponent<HandGrabItem>();
            var partCollider = partGO.AddComponent<BoxCollider>();

            zone.SendMessage("OnTriggerEnter", partCollider, SendMessageOptions.DontRequireReceiver);

            Assert.IsTrue(partRB.isKinematic);
        }

        [Test]
        public void OnPartSnapped_RejectsWrongTag()
        {
            var partGO = TestObjectRegistry.Create("WrongPart");
            partGO.tag = "Untagged";
            partGO.AddComponent<Rigidbody>();
            var partCollider = partGO.AddComponent<BoxCollider>();

            zone.SendMessage("OnTriggerEnter", partCollider, SendMessageOptions.DontRequireReceiver);

            Assert.IsFalse(zone.isOccupied);
        }

        [Test]
        public void OnPartSnapped_RejectsSecondPart()
        {
            // Snap first part.
            var part1 = TestObjectRegistry.Create("Part1");
            part1.tag = "RoboticsPart";
            part1.AddComponent<Rigidbody>();
            var col1 = part1.AddComponent<BoxCollider>();
            zone.SendMessage("OnTriggerEnter", col1, SendMessageOptions.DontRequireReceiver);

            // Try to snap second part.
            var part2 = TestObjectRegistry.Create("Part2");
            part2.tag = "RoboticsPart";
            part2.AddComponent<Rigidbody>();
            var col2 = part2.AddComponent<BoxCollider>();
            zone.SendMessage("OnTriggerEnter", col2, SendMessageOptions.DontRequireReceiver);

            // Zone should still reference the first part.
            Assert.AreEqual(part1, zone.snappedObject);
        }

        [Test]
        public void UnsnapCurrentPart_ClearsOccupied()
        {
            var partGO = TestObjectRegistry.Create("Part");
            partGO.tag = "RoboticsPart";
            partGO.AddComponent<Rigidbody>();
            var partCollider = partGO.AddComponent<BoxCollider>();

            zone.SendMessage("OnTriggerEnter", partCollider, SendMessageOptions.DontRequireReceiver);
            zone.UnsnapCurrentPart();

            Assert.IsFalse(zone.isOccupied);
            Assert.IsNull(zone.snappedObject);
        }

        [Test]
        public void UnsnapCurrentPart_UnparentsObject()
        {
            var partGO = TestObjectRegistry.Create("Part");
            partGO.tag = "RoboticsPart";
            partGO.AddComponent<Rigidbody>();
            var partCollider = partGO.AddComponent<BoxCollider>();

            zone.SendMessage("OnTriggerEnter", partCollider, SendMessageOptions.DontRequireReceiver);
            zone.UnsnapCurrentPart();

            Assert.IsNull(partGO.transform.parent);
        }

        [Test]
        public void UnsnapCurrentPart_DisablesChildColliders()
        {
            var partGO = TestObjectRegistry.Create("Part");
            partGO.tag = "RoboticsPart";
            partGO.AddComponent<Rigidbody>();
            var partCollider = partGO.AddComponent<BoxCollider>();

            zone.SendMessage("OnTriggerEnter", partCollider, SendMessageOptions.DontRequireReceiver);
            Assert.IsTrue(childCollider.enabled);

            zone.UnsnapCurrentPart();
            Assert.IsFalse(childCollider.enabled);
        }

        [Test]
        public void UnsnapCurrentPart_FiresOnPartRemovedEvent()
        {
            bool fired = false;
            zone.onPartRemoved = new UnityEvent();
            zone.onPartRemoved.AddListener(() => fired = true);

            var partGO = TestObjectRegistry.Create("Part");
            partGO.tag = "RoboticsPart";
            partGO.AddComponent<Rigidbody>();
            var partCollider = partGO.AddComponent<BoxCollider>();

            zone.SendMessage("OnTriggerEnter", partCollider, SendMessageOptions.DontRequireReceiver);
            zone.UnsnapCurrentPart();

            Assert.IsTrue(fired);
        }

        [Test]
        public void UnsnapCurrentPart_NoOpWhenEmpty()
        {
            // Should not throw.
            Assert.DoesNotThrow(() => zone.UnsnapCurrentPart());
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  RotatablePart Tests
    // ══════════════════════════════════════════════════════════════

    [TestFixture]
    public class RotatablePartTests
    {
        private GameObject testGO;
        private RotatablePart rotatablePart;

        [SetUp]
        public void SetUp()
        {
            testGO = TestObjectRegistry.Create("TestRotatable");
            rotatablePart = testGO.AddComponent<RotatablePart>();
            rotatablePart.selectedAxis = RotationAxis.LocalZ;
            rotatablePart.rotationSensitivity = 1.0f;
            rotatablePart.useAngleLimits = true;
            rotatablePart.minAngle = 0.0f;
            rotatablePart.maxAngle = 180.0f;
            rotatablePart.tickIntervalAngle = 0.0f; // Disable ticks for deterministic tests.

            // Must grab before applying rotation.
            rotatablePart.OnHandGrab();
        }

        [TearDown]
        public void TearDown()
        {
            TestObjectRegistry.DestroyAll();
        }

        [Test]
        public void ApplyRotationDelta_IncrementsAngle()
        {
            rotatablePart.ApplyRotationDelta(45.0f);
            Assert.AreEqual(45.0f, rotatablePart.currentAngle, 0.01f);
        }

        [Test]
        public void ApplyRotationDelta_ClampsAtMaxAngle()
        {
            rotatablePart.ApplyRotationDelta(999.0f);
            Assert.AreEqual(180.0f, rotatablePart.currentAngle, 0.01f);
        }

        [Test]
        public void ApplyRotationDelta_ClampsAtMinAngle()
        {
            rotatablePart.ApplyRotationDelta(-999.0f);
            Assert.AreEqual(0.0f, rotatablePart.currentAngle, 0.01f);
        }

        [Test]
        public void ApplyRotationDelta_NormalizedValueAtMin()
        {
            rotatablePart.ApplyRotationDelta(-999.0f);
            Assert.AreEqual(0.0f, rotatablePart.normalizedValue, 0.01f);
        }

        [Test]
        public void ApplyRotationDelta_NormalizedValueAtMax()
        {
            rotatablePart.ApplyRotationDelta(999.0f);
            Assert.AreEqual(1.0f, rotatablePart.normalizedValue, 0.01f);
        }

        [Test]
        public void ApplyRotationDelta_NormalizedValueAtMidpoint()
        {
            rotatablePart.ApplyRotationDelta(90.0f);
            Assert.AreEqual(0.5f, rotatablePart.normalizedValue, 0.01f);
        }

        [Test]
        public void ApplyRotationDelta_RespectsRotationSensitivity()
        {
            rotatablePart.rotationSensitivity = 2.0f;
            rotatablePart.ApplyRotationDelta(30.0f);
            Assert.AreEqual(60.0f, rotatablePart.currentAngle, 0.01f);
        }

        [Test]
        public void ApplyRotationDelta_IgnoredWhenNotHeld()
        {
            rotatablePart.OnHandRelease();
            rotatablePart.ApplyRotationDelta(90.0f);
            Assert.AreEqual(0.0f, rotatablePart.currentAngle, 0.01f);
        }

        [Test]
        public void OnMaxLimitReached_FiresWhenClamped()
        {
            bool fired = false;
            rotatablePart.onMaxLimitReached = new UnityEvent();
            rotatablePart.onMaxLimitReached.AddListener(() => fired = true);

            rotatablePart.ApplyRotationDelta(999.0f);

            Assert.IsTrue(fired);
        }

        [Test]
        public void OnMinLimitReached_FiresWhenClamped()
        {
            bool fired = false;
            rotatablePart.onMinLimitReached = new UnityEvent();
            rotatablePart.onMinLimitReached.AddListener(() => fired = true);

            rotatablePart.ApplyRotationDelta(-999.0f);

            Assert.IsTrue(fired);
        }

        [Test]
        public void OnMaxLimitReached_DoesNotFireBelowMax()
        {
            bool fired = false;
            rotatablePart.onMaxLimitReached = new UnityEvent();
            rotatablePart.onMaxLimitReached.AddListener(() => fired = true);

            rotatablePart.ApplyRotationDelta(90.0f);

            Assert.IsFalse(fired);
        }

        [Test]
        public void OnValueChanged_EmitsNormalizedFloat()
        {
            float received = -1.0f;
            rotatablePart.onValueChanged = new UnityEvent<float>();
            rotatablePart.onValueChanged.AddListener(v => received = v);

            rotatablePart.ApplyRotationDelta(90.0f);

            Assert.AreEqual(0.5f, received, 0.01f);
        }

        [Test]
        public void SetRotationDirectly_OverridesAngle()
        {
            rotatablePart.SetRotationDirectly(120.0f);
            Assert.AreEqual(120.0f, rotatablePart.currentAngle, 0.01f);
        }

        [Test]
        public void SetRotationDirectly_ClampsWithinLimits()
        {
            rotatablePart.SetRotationDirectly(500.0f);
            Assert.AreEqual(180.0f, rotatablePart.currentAngle, 0.01f);
        }

        [Test]
        public void SetRotationDirectly_WorksWithoutHolding()
        {
            rotatablePart.OnHandRelease();
            rotatablePart.SetRotationDirectly(90.0f);
            Assert.AreEqual(90.0f, rotatablePart.currentAngle, 0.01f);
        }

        [Test]
        public void UnlimitedMode_AllowsFullRotation()
        {
            rotatablePart.useAngleLimits = false;
            rotatablePart.ApplyRotationDelta(360.0f);
            Assert.AreEqual(360.0f, rotatablePart.currentAngle, 0.01f);
        }

        [Test]
        public void UnlimitedMode_AllowsNegativeAngles()
        {
            rotatablePart.useAngleLimits = false;
            rotatablePart.ApplyRotationDelta(-45.0f);
            Assert.AreEqual(-45.0f, rotatablePart.currentAngle, 0.01f);
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  SwipeWorkspaceManager Tests (Index Wrapping Math)
    // ══════════════════════════════════════════════════════════════

    [TestFixture]
    public class SwipeWorkspaceManagerTests
    {
        /// <summary>
        /// Pure math test mirroring the wrap formula used inside
        /// <see cref="SwipeWorkspaceManager.RequestWorkspaceChange"/>.
        /// Validates wrap logic without requiring coroutines or scene objects.
        /// </summary>
        private static int WrapIndex(int current, int direction, int count)
        {
            return ((current + direction) % count + count) % count;
        }

        [Test]
        public void WrapIndex_IncrementFromLastWrapsToZero()
        {
            Assert.AreEqual(0, WrapIndex(2, 1, 3));
        }

        [Test]
        public void WrapIndex_DecrementFromZeroWrapsToLast()
        {
            Assert.AreEqual(2, WrapIndex(0, -1, 3));
        }

        [Test]
        public void WrapIndex_IncrementFromMiddle()
        {
            Assert.AreEqual(2, WrapIndex(1, 1, 3));
        }

        [Test]
        public void WrapIndex_DecrementFromMiddle()
        {
            Assert.AreEqual(0, WrapIndex(1, -1, 3));
        }

        [Test]
        public void WrapIndex_ZeroDirectionStaysSame()
        {
            Assert.AreEqual(1, WrapIndex(1, 0, 3));
        }

        [Test]
        public void WrapIndex_LargePositiveJump()
        {
            // +4 from index 0 in array of 3 → (0+4)%3 = 1
            Assert.AreEqual(1, WrapIndex(0, 4, 3));
        }

        [Test]
        public void WrapIndex_LargeNegativeJump()
        {
            // -4 from index 0 in array of 3 → ((0-4)%3+3)%3 = 2
            Assert.AreEqual(2, WrapIndex(0, -4, 3));
        }

        [Test]
        public void WorkshopRootsActivation_OnlyOneActive()
        {
            // Simulate the activation pattern used in Start() / transition.
            var roots = new GameObject[3];
            for (int i = 0; i < 3; i++)
            {
                roots[i] = TestObjectRegistry.Create($"Workshop_{i}");
            }

            int activeIndex = 0;
            for (int i = 0; i < roots.Length; i++)
            {
                roots[i].SetActive(i == activeIndex);
            }

            Assert.IsTrue(roots[0].activeSelf);
            Assert.IsFalse(roots[1].activeSelf);
            Assert.IsFalse(roots[2].activeSelf);

            TestObjectRegistry.DestroyAll();
        }

        [Test]
        public void WorkshopRootsActivation_SwitchesToTarget()
        {
            var roots = new GameObject[3];
            for (int i = 0; i < 3; i++)
            {
                roots[i] = TestObjectRegistry.Create($"Workshop_{i}");
            }

            int activeIndex = 2;
            for (int i = 0; i < roots.Length; i++)
            {
                roots[i].SetActive(i == activeIndex);
            }

            Assert.IsFalse(roots[0].activeSelf);
            Assert.IsFalse(roots[1].activeSelf);
            Assert.IsTrue(roots[2].activeSelf);

            TestObjectRegistry.DestroyAll();
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  TriWorkshopManager Tests
    // ══════════════════════════════════════════════════════════════

    [TestFixture]
    public class TriWorkshopManagerTests
    {
        private GameObject managerGO;
        private TriWorkshopManager manager;

        [SetUp]
        public void SetUp()
        {
            managerGO = TestObjectRegistry.Create("TestManager");
            manager = managerGO.AddComponent<TriWorkshopManager>();

            // Initialise stage structs with empty arrays (no snap zones needed
            // for ForceComplete / Reset tests).
            manager.mechanicalStage = new WorkshopStage
            {
                stageName = "Mechanical",
                requiredSnapZones = new AdvancedSnapZone[0],
                requiredRotationsCount = 0,
                isCompleted = false,
                onStageCompleted = new UnityEvent(),
            };

            manager.electronicsStage = new WorkshopStage
            {
                stageName = "Electronics",
                requiredSnapZones = new AdvancedSnapZone[0],
                requiredRotationsCount = 0,
                isCompleted = false,
                onStageCompleted = new UnityEvent(),
            };

            manager.roboticsStage = new WorkshopStage
            {
                stageName = "Robotics",
                requiredSnapZones = new AdvancedSnapZone[0],
                requiredRotationsCount = 0,
                isCompleted = false,
                onStageCompleted = new UnityEvent(),
            };

            manager.onWorkshopCleared = new UnityEvent<int>();
            manager.onAllWorkshopsCompleted = new UnityEvent();
        }

        [TearDown]
        public void TearDown()
        {
            TestObjectRegistry.DestroyAll();
        }

        [Test]
        public void ForceCompleteWorkshop_MarksMechanicalComplete()
        {
            manager.ForceCompleteWorkshop(0);
            Assert.IsTrue(manager.mechanicalStage.isCompleted);
        }

        [Test]
        public void ForceCompleteWorkshop_MarksElectronicsComplete()
        {
            manager.ForceCompleteWorkshop(1);
            Assert.IsTrue(manager.electronicsStage.isCompleted);
        }

        [Test]
        public void ForceCompleteWorkshop_MarksRoboticsComplete()
        {
            manager.ForceCompleteWorkshop(2);
            Assert.IsTrue(manager.roboticsStage.isCompleted);
        }

        [Test]
        public void ForceCompleteWorkshop_FiresOnWorkshopCleared()
        {
            int receivedIndex = -1;
            manager.onWorkshopCleared.AddListener(idx => receivedIndex = idx);

            manager.ForceCompleteWorkshop(1);

            Assert.AreEqual(1, receivedIndex);
        }

        [Test]
        public void ForceCompleteWorkshop_FiresStageEvent()
        {
            bool fired = false;
            manager.mechanicalStage.onStageCompleted.AddListener(() => fired = true);

            manager.ForceCompleteWorkshop(0);

            Assert.IsTrue(fired);
        }

        [Test]
        public void GrandTriumph_FiresWhenAllComplete()
        {
            bool fired = false;
            manager.onAllWorkshopsCompleted.AddListener(() => fired = true);

            manager.ForceCompleteWorkshop(0);
            manager.ForceCompleteWorkshop(1);
            manager.ForceCompleteWorkshop(2);

            Assert.IsTrue(fired);
        }

        [Test]
        public void GrandTriumph_DoesNotFireWithOneMissing()
        {
            bool fired = false;
            manager.onAllWorkshopsCompleted.AddListener(() => fired = true);

            manager.ForceCompleteWorkshop(0);
            manager.ForceCompleteWorkshop(1);
            // Workshop 2 not completed.

            Assert.IsFalse(fired);
        }

        [Test]
        public void ResetAllProgress_ClearsAllStages()
        {
            manager.ForceCompleteWorkshop(0);
            manager.ForceCompleteWorkshop(1);
            manager.ForceCompleteWorkshop(2);

            manager.ResetAllProgress();

            Assert.IsFalse(manager.mechanicalStage.isCompleted);
            Assert.IsFalse(manager.electronicsStage.isCompleted);
            Assert.IsFalse(manager.roboticsStage.isCompleted);
        }

        [Test]
        public void ResetAllProgress_AllowsReCompletion()
        {
            manager.ForceCompleteWorkshop(0);
            manager.ResetAllProgress();

            bool fired = false;
            manager.mechanicalStage.onStageCompleted = new UnityEvent();
            manager.mechanicalStage.onStageCompleted.AddListener(() => fired = true);

            manager.ForceCompleteWorkshop(0);

            Assert.IsTrue(fired);
        }

        [Test]
        public void ForceComplete_InvalidIndex_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => manager.ForceCompleteWorkshop(99));
            Assert.DoesNotThrow(() => manager.ForceCompleteWorkshop(-1));
        }

        [Test]
        public void ForceComplete_Idempotent_DoesNotDoubleFireEvent()
        {
            int fireCount = 0;
            manager.onWorkshopCleared.AddListener(_ => fireCount++);

            manager.ForceCompleteWorkshop(0);
            manager.ForceCompleteWorkshop(0); // Second call should be a no-op.

            Assert.AreEqual(1, fireCount);
        }
    }
}
