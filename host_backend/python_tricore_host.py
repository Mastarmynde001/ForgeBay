#!/usr/bin/env python3
"""
python_tricore_host.py
─────────────────────────────────────────────────────────────────────
Local physics and telemetry engine for ForgeBay Unity 6 workshops.

Listens on UDP 5006 for inbound JSON from Unity.
Sends computed telemetry on UDP 5005 back to Unity.
Runs a non-blocking main loop at 20 Hz.

Engines:
  0 — Mechanical  (ECU RPM physics with inertia)
  1 — Electronics  (Ohm's Law / circuit simulation)
  2 — Robotics     (2-link planar inverse kinematics)

Zero external dependencies — stdlib only.
─────────────────────────────────────────────────────────────────────
"""

import json
import math
import random
import select
import socket
import sys
import time

# ──────────────────────────────────────────────────────────────────
#  Network Configuration
# ──────────────────────────────────────────────────────────────────

HOST_IP = "127.0.0.1"
INBOUND_PORT = 5006   # Unity sends TO this port
OUTBOUND_PORT = 5005  # This script sends TO this port (Unity listens)
TICK_RATE = 20        # Hz
TICK_INTERVAL = 1.0 / TICK_RATE

# ──────────────────────────────────────────────────────────────────
#  Engine 0 — Mechanical (ECU Physics) Constants
# ──────────────────────────────────────────────────────────────────

RPM_IDLE = 800.0
RPM_REDLINE = 7000.0
RPM_ACCEL_RATE = 4500.0   # RPM/s when throttle is applied
RPM_DECEL_RATE = 3000.0   # RPM/s when throttle is released
SHAKE_BASE_FACTOR = 0.0002
SHAKE_JITTER_MAX = 0.03

# ──────────────────────────────────────────────────────────────────
#  Engine 1 — Electronics Constants
# ──────────────────────────────────────────────────────────────────

SUPPLY_VOLTAGE = 5.0
LED_THRESHOLD = 2.5
BUZZER_THRESHOLD = 4.5

# ──────────────────────────────────────────────────────────────────
#  Engine 2 — Robotics (2-Link IK) Defaults
# ──────────────────────────────────────────────────────────────────

DEFAULT_LINK1 = 1.0
DEFAULT_LINK2 = 1.0

# ──────────────────────────────────────────────────────────────────
#  Simulation State
# ──────────────────────────────────────────────────────────────────

state = {
    "mode": 0,

    # Mechanical
    "throttle": 0.0,
    "current_rpm": RPM_IDLE,

    # Electronics
    "potentiometer_val": 0.0,
    "circuit_closed": False,

    # Robotics
    "target_x": 1.0,
    "target_y": 0.5,
    "link1_len": DEFAULT_LINK1,
    "link2_len": DEFAULT_LINK2,
}


# ──────────────────────────────────────────────────────────────────
#  Engine 0 — Mechanical (ECU RPM with Inertia)
# ──────────────────────────────────────────────────────────────────

def tick_mechanical(dt):
    """Lerp current_rpm toward target RPM with acceleration / deceleration inertia."""
    throttle = max(0.0, min(1.0, state["throttle"]))
    target_rpm = RPM_IDLE + (RPM_REDLINE - RPM_IDLE) * throttle
    current = state["current_rpm"]

    if current < target_rpm:
        current = min(current + RPM_ACCEL_RATE * dt, target_rpm)
    else:
        current = max(current - RPM_DECEL_RATE * dt, target_rpm)

    state["current_rpm"] = current

    rpm_norm = (current - RPM_IDLE) / (RPM_REDLINE - RPM_IDLE)
    shake = rpm_norm * SHAKE_BASE_FACTOR * current + random.uniform(0.0, SHAKE_JITTER_MAX)
    shake = max(0.0, min(1.0, shake))

    return {"rpm": round(current, 1), "shake": round(shake, 4)}


# ──────────────────────────────────────────────────────────────────
#  Engine 1 — Electronics (Ohm's Law / Circuit)
# ──────────────────────────────────────────────────────────────────

def tick_electronics(_dt):
    """Compute LED and buzzer states from potentiometer and circuit loop."""
    pot = max(0.0, min(1.0, state["potentiometer_val"]))
    closed = bool(state["circuit_closed"])

    voltage = pot * SUPPLY_VOLTAGE
    led_state = 0
    led_brightness = 0.0
    buzzer_state = 0

    if closed and voltage > LED_THRESHOLD:
        led_state = 1
        led_brightness = voltage / SUPPLY_VOLTAGE

    if closed and voltage > BUZZER_THRESHOLD:
        buzzer_state = 1

    return {
        "led": led_state,
        "brightness": round(led_brightness, 3),
        "buzzer": buzzer_state,
    }


# ──────────────────────────────────────────────────────────────────
#  Engine 2 — Robotics (2-Link Planar Inverse Kinematics)
# ──────────────────────────────────────────────────────────────────

def tick_robotics(_dt):
    """Solve 2-DOF planar IK for theta1 and theta2 in degrees."""
    tx = state["target_x"]
    ty = state["target_y"]
    l1 = state.get("link1_len", DEFAULT_LINK1)
    l2 = state.get("link2_len", DEFAULT_LINK2)

    dist_sq = tx * tx + ty * ty
    dist = math.sqrt(dist_sq)
    reach_max = l1 + l2
    reach_min = abs(l1 - l2)

    reachable = True

    # Clamp to reachable workspace if target is out of range.
    if dist < 1e-6:
        # Target at origin — degenerate; fold the arm.
        tx = reach_min + 1e-4
        ty = 0.0
        dist_sq = tx * tx + ty * ty
        dist = math.sqrt(dist_sq)
        reachable = False
    elif dist > reach_max:
        scale = (reach_max - 1e-4) / dist
        tx *= scale
        ty *= scale
        dist_sq = tx * tx + ty * ty
        dist = math.sqrt(dist_sq)
        reachable = False
    elif dist < reach_min:
        scale = (reach_min + 1e-4) / dist
        tx *= scale
        ty *= scale
        dist_sq = tx * tx + ty * ty
        dist = math.sqrt(dist_sq)
        reachable = False

    # Law of cosines for elbow angle (theta2).
    cos_theta2 = (dist_sq - l1 * l1 - l2 * l2) / (2.0 * l1 * l2)
    cos_theta2 = max(-1.0, min(1.0, cos_theta2))
    theta2 = math.acos(cos_theta2)

    # Shoulder angle (theta1).
    k1 = l1 + l2 * cos_theta2
    k2 = l2 * math.sin(theta2)
    theta1 = math.atan2(ty, tx) - math.atan2(k2, k1)

    theta1_deg = math.degrees(theta1)
    theta2_deg = math.degrees(theta2)

    return {
        "theta1": round(theta1_deg, 2),
        "theta2": round(theta2_deg, 2),
        "reachable": reachable,
    }


# ──────────────────────────────────────────────────────────────────
#  Inbound Packet Parser
# ──────────────────────────────────────────────────────────────────

def process_inbound(data_bytes):
    """Parse an inbound JSON packet from Unity and update simulation state."""
    try:
        payload = json.loads(data_bytes.decode("utf-8", errors="replace"))
    except (json.JSONDecodeError, UnicodeDecodeError):
        return  # Silently drop malformed packets.

    if not isinstance(payload, dict):
        return

    # Mode selection.
    if "mode" in payload:
        try:
            state["mode"] = int(payload["mode"])
        except (ValueError, TypeError):
            pass

    # Mechanical inputs.
    if "throttle" in payload:
        try:
            state["throttle"] = float(payload["throttle"])
        except (ValueError, TypeError):
            pass

    # Electronics inputs.
    if "potentiometer_val" in payload:
        try:
            state["potentiometer_val"] = float(payload["potentiometer_val"])
        except (ValueError, TypeError):
            pass
    if "circuit_closed" in payload:
        state["circuit_closed"] = bool(payload["circuit_closed"])

    # Robotics inputs.
    for key in ("target_x", "target_y", "link1_len", "link2_len"):
        if key in payload:
            try:
                state[key] = float(payload[key])
            except (ValueError, TypeError):
                pass


# ──────────────────────────────────────────────────────────────────
#  Terminal Dashboard
# ──────────────────────────────────────────────────────────────────

MODE_LABELS = {0: "MECH", 1: "ELEC", 2: "ROBO"}


def render_dashboard(mech, elec, robo):
    """Print a single-line real-time status to stdout."""
    mode_label = MODE_LABELS.get(state["mode"], "????")
    line = (
        f"[TriCore] Mode:{mode_label} | "
        f"RPM:{mech['rpm']:7.1f}  Shake:{mech['shake']:.4f} | "
        f"LED:{elec['led']}  Bri:{elec['brightness']:.3f}  Buzz:{elec['buzzer']} | "
        f"θ1:{robo['theta1']:7.2f}°  θ2:{robo['theta2']:7.2f}°  "
        f"Reach:{'✓' if robo['reachable'] else '✗'}   "
    )
    sys.stdout.write("\r" + line)
    sys.stdout.flush()


# ──────────────────────────────────────────────────────────────────
#  Main Loop
# ──────────────────────────────────────────────────────────────────

def main():
    # ── Create sockets ──
    sock_in = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock_in.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock_in.setblocking(False)

    try:
        sock_in.bind((HOST_IP, INBOUND_PORT))
    except OSError as exc:
        print(f"[TriCore] ERROR — Cannot bind to {HOST_IP}:{INBOUND_PORT}: {exc}")
        sys.exit(1)

    sock_out = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    print(f"[TriCore] Listening on {HOST_IP}:{INBOUND_PORT}")
    print(f"[TriCore] Sending to   {HOST_IP}:{OUTBOUND_PORT}")
    print(f"[TriCore] Tick rate     {TICK_RATE} Hz")
    print("[TriCore] Press CTRL+C to stop.\n")

    try:
        while True:
            tick_start = time.monotonic()

            # ── Drain all pending inbound packets (non-blocking) ──
            while True:
                readable, _, _ = select.select([sock_in], [], [], 0)
                if not readable:
                    break
                try:
                    data, _addr = sock_in.recvfrom(4096)
                    if data:
                        process_inbound(data)
                except OSError:
                    break

            # ── Tick all three engines ──
            dt = TICK_INTERVAL
            mech = tick_mechanical(dt)
            elec = tick_electronics(dt)
            robo = tick_robotics(dt)

            # ── Build and send outbound payload ──
            outbound = {
                "type": _outbound_type_for_mode(state["mode"]),
                "mode": state["mode"],
                "mech": mech,
                "elec": elec,
                "robotics": robo,
            }

            # Also flatten fields that UDPBridge.cs expects per mode.
            if state["mode"] == 0:
                outbound["engine_rpm"] = mech["rpm"]
                outbound["shake_intensity"] = mech["shake"]
            elif state["mode"] == 1:
                outbound["led_state"] = elec["led"]
                outbound["buzzer_state"] = elec["buzzer"]
            elif state["mode"] == 2:
                outbound["joint_angles"] = [robo["theta1"], robo["theta2"]]

            try:
                packet = json.dumps(outbound, separators=(",", ":")).encode("utf-8")
                sock_out.sendto(packet, (HOST_IP, OUTBOUND_PORT))
            except OSError:
                pass  # Silently drop on transient send failures.

            # ── Dashboard ──
            render_dashboard(mech, elec, robo)

            # ── Precise tick sleep ──
            elapsed = time.monotonic() - tick_start
            remaining = TICK_INTERVAL - elapsed
            if remaining > 0:
                time.sleep(remaining)

    except KeyboardInterrupt:
        print("\n[TriCore] Shutting down…")

    finally:
        try:
            sock_in.close()
        except OSError:
            pass
        try:
            sock_out.close()
        except OSError:
            pass
        print("[TriCore] Sockets released. Goodbye.")


def _outbound_type_for_mode(mode):
    """Return the 'type' string that UDPBridge.cs payload DTOs expect."""
    if mode == 0:
        return "mechanical"
    if mode == 1:
        return "electronics"
    if mode == 2:
        return "robotics"
    return "unknown"


# ──────────────────────────────────────────────────────────────────

if __name__ == "__main__":
    main()
