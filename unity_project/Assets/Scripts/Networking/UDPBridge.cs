using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;

namespace ForgeBay.Networking
{
    /// <summary>
    /// Bidirectional UDP bridge between Unity and an external Python telemetry
    /// host (<c>python_tricore_host.py</c>) running on local loopback.
    /// Inbound data is received on a background thread and safely dispatched
    /// to the main thread via a lock-free concurrent queue.
    /// </summary>
    [DisallowMultipleComponent]
    public class UDPBridge : MonoBehaviour
    {
        // ──────────────────────────────────────────────
        //  Inspector Fields
        // ──────────────────────────────────────────────

        [Header("Network Configuration")]

        [Tooltip("IP address of the Python host (local loopback).")]
        public string hostIP = "127.0.0.1";

        [Tooltip("Port this client listens on for inbound telemetry from Python.")]
        public int inboundPort = 5005;

        [Tooltip("Port the Python host listens on for outbound commands from Unity.")]
        public int outboundPort = 5006;

        [Header("Active Mode")]

        [Tooltip("Current workshop index (0 = Mechanical, 1 = Electronics, 2 = Robotics). Updated by SwipeWorkspaceManager.")]
        public int currentWorkshopIndex = 0;

        [Header("Driver Outputs — Mechanical")]

        [Tooltip("AudioSource whose pitch is modulated by incoming engine RPM.")]
        public AudioSource engineAudioSource;

        [Tooltip("Transform that receives rotational shake noise from engine vibration.")]
        public Transform engineBlockTransform;

        [Header("Driver Outputs — Electronics")]

        [Tooltip("GameObject toggled on/off by the LED pin state from Python.")]
        public GameObject ledIndicator;

        [Tooltip("AudioSource triggered when the buzzer pin state is high.")]
        public AudioSource buzzerAudioSource;

        [Header("Driver Outputs — Robotics")]

        [Tooltip("Joint transforms driven by inverse-kinematics solutions from Python.")]
        public Transform[] roboticsJointTransforms;

        [Header("Events")]

        [Tooltip("Fires on each mechanical telemetry frame (rpm, shakeIntensity).")]
        public UnityEvent<float, float> onMechanicalTelemetry;

        [Tooltip("Fires on each electronics telemetry frame (ledState, buzzerState).")]
        public UnityEvent<int, int> onElectronicsTelemetry;

        // ──────────────────────────────────────────────
        //  Private State
        // ──────────────────────────────────────────────

        private UdpClient inboundClient;
        private UdpClient outboundClient;
        private Thread listenerThread;
        private volatile bool isRunning;

        private readonly ConcurrentQueue<Action> mainThreadQueue = new ConcurrentQueue<Action>();

        // Cached telemetry values written by the dispatcher, read in Update().
        private float latestRPM;
        private float latestShakeIntensity;
        private int latestLedState;
        private int latestBuzzerState;
        private float[] latestJointAngles;

        // Shake noise state.
        private Vector3 engineBaseRotation;
        private bool engineBaseRotationCached;

        // ──────────────────────────────────────────────
        //  Lifecycle
        // ──────────────────────────────────────────────

        private void Start()
        {
            isRunning = true;

            // Outbound client — unbound, used only for Send().
            outboundClient = new UdpClient();

            // Listener thread — receives inbound telemetry off the main thread.
            listenerThread = new Thread(ListenForUDPData)
            {
                IsBackground = true,
                Name = "UDPBridge_Listener"
            };
            listenerThread.Start();
        }

        private void Update()
        {
            // Drain every queued action onto the main thread.
            while (mainThreadQueue.TryDequeue(out Action action))
            {
                action?.Invoke();
            }

            // Apply driver outputs based on the active workshop mode.
            switch (currentWorkshopIndex)
            {
                case 0:
                    ApplyMechanicalDrivers();
                    break;
                case 1:
                    ApplyElectronicsDrivers();
                    break;
                case 2:
                    ApplyRoboticsDrivers();
                    break;
            }
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private void OnApplicationQuit()
        {
            Shutdown();
        }

        // ──────────────────────────────────────────────
        //  Public API
        // ──────────────────────────────────────────────

        /// <summary>
        /// Sends a UTF-8 JSON payload to the Python host on <see cref="outboundPort"/>.
        /// Safe to call from any thread; the underlying <see cref="UdpClient.Send"/>
        /// is thread-safe for distinct datagrams.
        /// </summary>
        /// <param name="jsonPayload">Serialised JSON string to transmit.</param>
        public void SendTelemetry(string jsonPayload)
        {
            if (!isRunning || outboundClient == null)
            {
                return;
            }

            try
            {
                byte[] data = Encoding.UTF8.GetBytes(jsonPayload);
                outboundClient.Send(data, data.Length, hostIP, outboundPort);
            }
            catch (SocketException ex)
            {
                Debug.LogWarning($"[UDPBridge] Send failed: {ex.Message}");
            }
            catch (ObjectDisposedException)
            {
                // Client closed during shutdown — silently ignore.
            }
        }

        /// <summary>
        /// Called by <see cref="ForgeBay.Environment.SwipeWorkspaceManager"/>
        /// to update the active workshop mode at runtime.
        /// </summary>
        /// <param name="index">Workshop index (0, 1, or 2).</param>
        public void SetWorkshopIndex(int index)
        {
            currentWorkshopIndex = index;
        }

        // ──────────────────────────────────────────────
        //  Background Listener Thread
        // ──────────────────────────────────────────────

        /// <summary>
        /// Blocking receive loop executed on a dedicated background thread.
        /// Parses incoming JSON and enqueues main-thread-safe actions.
        /// </summary>
        private void ListenForUDPData()
        {
            try
            {
                inboundClient = new UdpClient(inboundPort);
            }
            catch (SocketException ex)
            {
                Debug.LogError($"[UDPBridge] Failed to bind to port {inboundPort}: {ex.Message}");
                return;
            }

            IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

            while (isRunning)
            {
                try
                {
                    // Blocking call — wakes when a datagram arrives or socket is closed.
                    byte[] data = inboundClient.Receive(ref remoteEndPoint);

                    if (data == null || data.Length == 0)
                    {
                        continue;
                    }

                    string json = Encoding.UTF8.GetString(data);
                    DispatchTelemetry(json);
                }
                catch (SocketException)
                {
                    // Socket closed during shutdown — exit gracefully.
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (isRunning)
                    {
                        Debug.LogWarning($"[UDPBridge] Listener error: {ex.Message}");
                    }
                }
            }
        }

        // ──────────────────────────────────────────────
        //  JSON Dispatch (called from background thread)
        // ──────────────────────────────────────────────

        /// <summary>
        /// Parses a JSON telemetry payload and enqueues the appropriate
        /// main-thread update. All <c>UnityEngine</c> API calls are deferred
        /// to <see cref="Update"/> via the concurrent queue.
        /// </summary>
        private void DispatchTelemetry(string json)
        {
            // Minimal JSON parsing via Unity's JsonUtility.
            // Each workshop mode uses a distinct payload shape.

            // Attempt mechanical telemetry.
            MechanicalPayload mechPayload = ParseJson<MechanicalPayload>(json);
            if (mechPayload != null && mechPayload.type == "mechanical")
            {
                float rpm = mechPayload.engine_rpm;
                float shake = mechPayload.shake_intensity;

                mainThreadQueue.Enqueue(() =>
                {
                    latestRPM = rpm;
                    latestShakeIntensity = shake;
                    onMechanicalTelemetry?.Invoke(rpm, shake);
                });

                return;
            }

            // Attempt electronics telemetry.
            ElectronicsPayload elecPayload = ParseJson<ElectronicsPayload>(json);
            if (elecPayload != null && elecPayload.type == "electronics")
            {
                int led = elecPayload.led_state;
                int buzzer = elecPayload.buzzer_state;

                mainThreadQueue.Enqueue(() =>
                {
                    latestLedState = led;
                    latestBuzzerState = buzzer;
                    onElectronicsTelemetry?.Invoke(led, buzzer);
                });

                return;
            }

            // Attempt robotics telemetry.
            RoboticsPayload roboPayload = ParseJson<RoboticsPayload>(json);
            if (roboPayload != null && roboPayload.type == "robotics")
            {
                // Copy the array to avoid closure over a mutable reference.
                float[] angles = roboPayload.joint_angles != null
                    ? (float[])roboPayload.joint_angles.Clone()
                    : null;

                mainThreadQueue.Enqueue(() =>
                {
                    latestJointAngles = angles;
                });

                return;
            }
        }

        /// <summary>
        /// Wraps <see cref="JsonUtility.FromJson{T}"/> with exception safety
        /// so malformed payloads never crash the listener thread.
        /// </summary>
        private static T ParseJson<T>(string json) where T : class
        {
            try
            {
                return JsonUtility.FromJson<T>(json);
            }
            catch
            {
                return null;
            }
        }

        // ──────────────────────────────────────────────
        //  Main-Thread Driver Application
        // ──────────────────────────────────────────────

        /// <summary>
        /// Modulates engine audio pitch by RPM and applies rotational shake
        /// noise to the engine block transform.
        /// </summary>
        private void ApplyMechanicalDrivers()
        {
            // Pitch: map 0–8000 RPM to 0.5–2.0 pitch range.
            if (engineAudioSource != null)
            {
                engineAudioSource.pitch = Mathf.Lerp(0.5f, 2.0f, Mathf.Clamp01(latestRPM / 8000.0f));
            }

            // Shake: apply slight random rotational offset scaled by intensity.
            if (engineBlockTransform != null)
            {
                if (!engineBaseRotationCached)
                {
                    engineBaseRotation = engineBlockTransform.localEulerAngles;
                    engineBaseRotationCached = true;
                }

                float intensity = Mathf.Clamp01(latestShakeIntensity);
                float maxOffset = intensity * 1.5f;

                Vector3 shake = new Vector3(
                    UnityEngine.Random.Range(-maxOffset, maxOffset),
                    UnityEngine.Random.Range(-maxOffset, maxOffset),
                    UnityEngine.Random.Range(-maxOffset, maxOffset)
                );

                engineBlockTransform.localEulerAngles = engineBaseRotation + shake;
            }
        }

        /// <summary>
        /// Toggles the LED indicator and triggers the buzzer audio based
        /// on pin states from the Python electronics simulation.
        /// </summary>
        private void ApplyElectronicsDrivers()
        {
            if (ledIndicator != null)
            {
                ledIndicator.SetActive(latestLedState == 1);
            }

            if (buzzerAudioSource != null)
            {
                if (latestBuzzerState == 1 && !buzzerAudioSource.isPlaying)
                {
                    buzzerAudioSource.Play();
                }
                else if (latestBuzzerState == 0 && buzzerAudioSource.isPlaying)
                {
                    buzzerAudioSource.Stop();
                }
            }
        }

        /// <summary>
        /// Applies inverse-kinematics joint angle solutions to the robotics
        /// arm joint transforms. Each angle drives local X-axis rotation.
        /// </summary>
        private void ApplyRoboticsDrivers()
        {
            if (latestJointAngles == null || roboticsJointTransforms == null)
            {
                return;
            }

            int count = Mathf.Min(latestJointAngles.Length, roboticsJointTransforms.Length);

            for (int i = 0; i < count; i++)
            {
                Transform joint = roboticsJointTransforms[i];

                if (joint == null)
                {
                    continue;
                }

                Vector3 euler = joint.localEulerAngles;
                euler.x = latestJointAngles[i];
                joint.localEulerAngles = euler;
            }
        }

        // ──────────────────────────────────────────────
        //  Shutdown
        // ──────────────────────────────────────────────

        /// <summary>
        /// Cleanly stops the listener thread and releases socket resources
        /// to prevent port locks on Ubuntu / WebGL host systems.
        /// </summary>
        private void Shutdown()
        {
            if (!isRunning)
            {
                return;
            }

            isRunning = false;

            // Closing the client unblocks the Receive() call in the listener thread.
            try { inboundClient?.Close(); } catch { /* already disposed */ }
            try { outboundClient?.Close(); } catch { /* already disposed */ }

            if (listenerThread != null && listenerThread.IsAlive)
            {
                // Give the thread a moment to exit on its own before aborting.
                if (!listenerThread.Join(TimeSpan.FromSeconds(1)))
                {
                    try
                    {
                        listenerThread.Interrupt();
                    }
                    catch
                    {
                        // Best-effort cleanup.
                    }
                }
            }

            inboundClient = null;
            outboundClient = null;
            listenerThread = null;
        }

        // ──────────────────────────────────────────────
        //  Telemetry Payload DTOs (JsonUtility compatible)
        // ──────────────────────────────────────────────

        [Serializable]
        private class MechanicalPayload
        {
            public string type;
            public float engine_rpm;
            public float shake_intensity;
        }

        [Serializable]
        private class ElectronicsPayload
        {
            public string type;
            public int led_state;
            public int buzzer_state;
        }

        [Serializable]
        private class RoboticsPayload
        {
            public string type;
            public float[] joint_angles;
        }
    }
}
