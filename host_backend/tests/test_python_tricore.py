"""
test_python_tricore.py
─────────────────────────────────────────────────────────────────────
Pytest suite validating all calculation engines, telemetry boundaries,
and JSON payload formatting in python_tricore_host.py.

Runs fully headless — no live socket bindings required.
─────────────────────────────────────────────────────────────────────
"""

import copy
import json
import math
import sys
import os

import pytest

# ── Ensure the host_backend package is importable regardless of cwd. ──
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

import python_tricore_host as host  # noqa: E402


# ══════════════════════════════════════════════════════════════════
#  Fixtures
# ══════════════════════════════════════════════════════════════════

@pytest.fixture(autouse=True)
def reset_state():
    """Snapshot and restore global simulation state between tests."""
    original = copy.deepcopy(host.state)
    yield
    host.state.update(original)


# ══════════════════════════════════════════════════════════════════
#  ENGINE 0 — Mechanical (ECU RPM Physics)
# ══════════════════════════════════════════════════════════════════

class TestMechanicalEngine:
    """Validates RPM lerp acceleration, deceleration, and shake bounds."""

    def test_idle_rpm_at_zero_throttle(self):
        """At zero throttle the RPM should settle toward IDLE (800)."""
        host.state["throttle"] = 0.0
        host.state["current_rpm"] = host.RPM_IDLE

        result = host.tick_mechanical(0.05)

        assert result["rpm"] == pytest.approx(host.RPM_IDLE, abs=1.0)

    def test_rpm_increases_with_full_throttle(self):
        """Full throttle should accelerate RPM above idle."""
        host.state["throttle"] = 1.0
        host.state["current_rpm"] = host.RPM_IDLE

        result = host.tick_mechanical(0.05)

        assert result["rpm"] > host.RPM_IDLE

    def test_rpm_approaches_redline_over_time(self):
        """Sustained full throttle over many ticks should approach REDLINE."""
        host.state["throttle"] = 1.0
        host.state["current_rpm"] = host.RPM_IDLE

        for _ in range(500):
            host.tick_mechanical(0.05)

        assert host.state["current_rpm"] == pytest.approx(host.RPM_REDLINE, abs=10.0)

    def test_rpm_decelerates_on_throttle_release(self):
        """Releasing throttle from redline should lower RPM."""
        host.state["throttle"] = 0.0
        host.state["current_rpm"] = host.RPM_REDLINE

        result = host.tick_mechanical(0.05)

        assert result["rpm"] < host.RPM_REDLINE

    def test_rpm_decelerates_to_idle_over_time(self):
        """Sustained zero throttle should return RPM to idle."""
        host.state["throttle"] = 0.0
        host.state["current_rpm"] = host.RPM_REDLINE

        for _ in range(500):
            host.tick_mechanical(0.05)

        assert host.state["current_rpm"] == pytest.approx(host.RPM_IDLE, abs=10.0)

    def test_accel_rate_faster_than_zero(self):
        """Acceleration rate constant must be positive."""
        assert host.RPM_ACCEL_RATE > 0

    def test_decel_rate_faster_than_zero(self):
        """Deceleration rate constant must be positive."""
        assert host.RPM_DECEL_RATE > 0

    def test_shake_intensity_bounded_zero_to_one_at_idle(self):
        """Shake at idle RPM must stay within [0.0, 1.0]."""
        host.state["throttle"] = 0.0
        host.state["current_rpm"] = host.RPM_IDLE

        for _ in range(100):
            result = host.tick_mechanical(0.05)
            assert 0.0 <= result["shake"] <= 1.0

    def test_shake_intensity_bounded_zero_to_one_at_redline(self):
        """Shake at redline RPM must stay within [0.0, 1.0]."""
        host.state["throttle"] = 1.0
        host.state["current_rpm"] = host.RPM_REDLINE

        for _ in range(100):
            result = host.tick_mechanical(0.05)
            assert 0.0 <= result["shake"] <= 1.0

    def test_shake_increases_with_rpm(self):
        """Average shake at high RPM should exceed average shake at idle."""
        host.state["throttle"] = 0.0
        host.state["current_rpm"] = host.RPM_IDLE
        low_shakes = [host.tick_mechanical(0.05)["shake"] for _ in range(200)]

        host.state["throttle"] = 1.0
        host.state["current_rpm"] = host.RPM_REDLINE
        high_shakes = [host.tick_mechanical(0.05)["shake"] for _ in range(200)]

        assert sum(high_shakes) / len(high_shakes) > sum(low_shakes) / len(low_shakes)

    def test_throttle_clamped_above_one(self):
        """Throttle values above 1.0 should be treated as 1.0."""
        host.state["throttle"] = 5.0
        host.state["current_rpm"] = host.RPM_IDLE

        result = host.tick_mechanical(0.05)

        # RPM should increase as if throttle == 1.0
        host.state["throttle"] = 1.0
        host.state["current_rpm"] = host.RPM_IDLE
        expected = host.tick_mechanical(0.05)

        assert result["rpm"] == pytest.approx(expected["rpm"], abs=1.0)

    def test_throttle_clamped_below_zero(self):
        """Negative throttle should be treated as 0.0."""
        host.state["throttle"] = -1.0
        host.state["current_rpm"] = 3000.0

        result = host.tick_mechanical(0.05)

        host.state["throttle"] = 0.0
        host.state["current_rpm"] = 3000.0
        expected = host.tick_mechanical(0.05)

        assert result["rpm"] == pytest.approx(expected["rpm"], abs=1.0)

    def test_result_contains_required_keys(self):
        """Mechanical output must have 'rpm' and 'shake' keys."""
        result = host.tick_mechanical(0.05)
        assert "rpm" in result
        assert "shake" in result


# ══════════════════════════════════════════════════════════════════
#  ENGINE 1 — Electronics (Ohm's Law / Circuit)
# ══════════════════════════════════════════════════════════════════

class TestElectronicsEngine:
    """Validates LED, brightness, and buzzer logic across voltage ranges."""

    # ── LED off when voltage is below threshold ──

    def test_led_off_at_low_voltage_closed_circuit(self):
        """Voltage below 2.5 V with closed circuit → LED off."""
        host.state["potentiometer_val"] = 0.4  # 2.0 V
        host.state["circuit_closed"] = True

        result = host.tick_electronics(0.05)

        assert result["led"] == 0
        assert result["brightness"] == pytest.approx(0.0)

    def test_led_off_when_circuit_open(self):
        """High voltage but open circuit → LED off."""
        host.state["potentiometer_val"] = 1.0  # 5.0 V
        host.state["circuit_closed"] = False

        result = host.tick_electronics(0.05)

        assert result["led"] == 0

    # ── LED on when voltage exceeds threshold and circuit closed ──

    def test_led_on_above_threshold_closed(self):
        """Voltage above 2.5 V with closed circuit → LED on."""
        host.state["potentiometer_val"] = 0.6  # 3.0 V
        host.state["circuit_closed"] = True

        result = host.tick_electronics(0.05)

        assert result["led"] == 1

    def test_brightness_scales_with_voltage(self):
        """LED brightness should equal voltage / 5.0."""
        host.state["potentiometer_val"] = 0.8  # 4.0 V
        host.state["circuit_closed"] = True

        result = host.tick_electronics(0.05)

        assert result["brightness"] == pytest.approx(0.8, abs=0.01)

    def test_brightness_at_full_voltage(self):
        """Full potentiometer → brightness = 1.0."""
        host.state["potentiometer_val"] = 1.0
        host.state["circuit_closed"] = True

        result = host.tick_electronics(0.05)

        assert result["brightness"] == pytest.approx(1.0, abs=0.01)

    # ── Buzzer ──

    def test_buzzer_off_below_threshold(self):
        """Voltage below 4.5 V → buzzer off."""
        host.state["potentiometer_val"] = 0.8  # 4.0 V
        host.state["circuit_closed"] = True

        result = host.tick_electronics(0.05)

        assert result["buzzer"] == 0

    def test_buzzer_on_above_threshold(self):
        """Voltage above 4.5 V with closed circuit → buzzer on."""
        host.state["potentiometer_val"] = 0.95  # 4.75 V
        host.state["circuit_closed"] = True

        result = host.tick_electronics(0.05)

        assert result["buzzer"] == 1

    def test_buzzer_off_when_circuit_open(self):
        """High voltage but open circuit → buzzer off."""
        host.state["potentiometer_val"] = 1.0
        host.state["circuit_closed"] = False

        result = host.tick_electronics(0.05)

        assert result["buzzer"] == 0

    # ── Edge cases ──

    def test_zero_potentiometer(self):
        """Zero potentiometer → everything off."""
        host.state["potentiometer_val"] = 0.0
        host.state["circuit_closed"] = True

        result = host.tick_electronics(0.05)

        assert result["led"] == 0
        assert result["buzzer"] == 0
        assert result["brightness"] == pytest.approx(0.0)

    def test_exact_led_threshold_voltage(self):
        """Exactly at threshold (2.5 V) → LED should be off (not strictly >)."""
        host.state["potentiometer_val"] = 0.5  # Exactly 2.5 V
        host.state["circuit_closed"] = True

        result = host.tick_electronics(0.05)

        assert result["led"] == 0

    def test_result_contains_required_keys(self):
        """Electronics output must have 'led', 'brightness', 'buzzer'."""
        result = host.tick_electronics(0.05)
        assert "led" in result
        assert "brightness" in result
        assert "buzzer" in result


# ══════════════════════════════════════════════════════════════════
#  ENGINE 2 — Robotics (2-Link Planar Inverse Kinematics)
# ══════════════════════════════════════════════════════════════════

class TestRoboticsEngine:
    """Validates 2-DOF IK solver accuracy, clamping, and edge cases."""

    # ── Reachable targets ──

    def test_reachable_target_returns_true(self):
        """A point within reach should report reachable == True."""
        host.state["target_x"] = 1.0
        host.state["target_y"] = 1.0
        host.state["link1_len"] = 1.0
        host.state["link2_len"] = 1.0

        result = host.tick_robotics(0.05)

        assert result["reachable"] is True

    def test_reachable_target_returns_valid_angles(self):
        """Angles for a reachable point must be finite numbers."""
        host.state["target_x"] = 1.0
        host.state["target_y"] = 1.0
        host.state["link1_len"] = 1.0
        host.state["link2_len"] = 1.0

        result = host.tick_robotics(0.05)

        assert math.isfinite(result["theta1"])
        assert math.isfinite(result["theta2"])

    def test_forward_kinematics_roundtrip(self):
        """IK solution, when fed back through FK, should reach the target."""
        tx, ty = 1.2, 0.8
        l1, l2 = 1.0, 1.0
        host.state["target_x"] = tx
        host.state["target_y"] = ty
        host.state["link1_len"] = l1
        host.state["link2_len"] = l2

        result = host.tick_robotics(0.05)
        t1 = math.radians(result["theta1"])
        t2 = math.radians(result["theta2"])

        # Forward kinematics.
        end_x = l1 * math.cos(t1) + l2 * math.cos(t1 + t2)
        end_y = l1 * math.sin(t1) + l2 * math.sin(t1 + t2)

        assert end_x == pytest.approx(tx, abs=0.05)
        assert end_y == pytest.approx(ty, abs=0.05)

    def test_fully_extended_arm(self):
        """Target at maximum reach should be reachable (just barely)."""
        host.state["target_x"] = 1.99  # Just under l1 + l2 = 2.0
        host.state["target_y"] = 0.0
        host.state["link1_len"] = 1.0
        host.state["link2_len"] = 1.0

        result = host.tick_robotics(0.05)

        assert result["reachable"] is True
        assert math.isfinite(result["theta1"])
        assert math.isfinite(result["theta2"])

    # ── Unreachable targets ──

    def test_unreachable_target_returns_false(self):
        """A point far beyond reach should report reachable == False."""
        host.state["target_x"] = 5.0
        host.state["target_y"] = 5.0
        host.state["link1_len"] = 1.0
        host.state["link2_len"] = 1.0

        result = host.tick_robotics(0.05)

        assert result["reachable"] is False

    def test_unreachable_target_no_math_error(self):
        """Unreachable targets must not raise ValueError or ZeroDivisionError."""
        host.state["target_x"] = 100.0
        host.state["target_y"] = 100.0
        host.state["link1_len"] = 1.0
        host.state["link2_len"] = 1.0

        # Must not raise.
        result = host.tick_robotics(0.05)

        assert math.isfinite(result["theta1"])
        assert math.isfinite(result["theta2"])

    def test_unreachable_returns_clamped_angles(self):
        """Clamped IK angles for unreachable targets should still form valid FK."""
        host.state["target_x"] = 10.0
        host.state["target_y"] = 0.0
        host.state["link1_len"] = 1.0
        host.state["link2_len"] = 1.0

        result = host.tick_robotics(0.05)

        # The arm should be nearly fully extended toward the target direction.
        t1 = math.radians(result["theta1"])
        t2 = math.radians(result["theta2"])
        end_x = 1.0 * math.cos(t1) + 1.0 * math.cos(t1 + t2)

        # End effector X should be close to max reach (2.0).
        assert end_x == pytest.approx(2.0, abs=0.1)

    # ── Degenerate cases ──

    def test_target_at_origin(self):
        """Target at (0, 0) is degenerate; must not crash."""
        host.state["target_x"] = 0.0
        host.state["target_y"] = 0.0
        host.state["link1_len"] = 1.0
        host.state["link2_len"] = 1.0

        result = host.tick_robotics(0.05)

        assert result["reachable"] is False
        assert math.isfinite(result["theta1"])
        assert math.isfinite(result["theta2"])

    def test_negative_coordinates(self):
        """Negative target coordinates should produce valid results."""
        host.state["target_x"] = -0.5
        host.state["target_y"] = -0.5
        host.state["link1_len"] = 1.0
        host.state["link2_len"] = 1.0

        result = host.tick_robotics(0.05)

        assert math.isfinite(result["theta1"])
        assert math.isfinite(result["theta2"])

    def test_asymmetric_link_lengths(self):
        """Unequal link lengths should still solve correctly."""
        host.state["target_x"] = 1.5
        host.state["target_y"] = 0.5
        host.state["link1_len"] = 1.2
        host.state["link2_len"] = 0.8

        result = host.tick_robotics(0.05)

        assert result["reachable"] is True
        assert math.isfinite(result["theta1"])
        assert math.isfinite(result["theta2"])

    def test_result_contains_required_keys(self):
        """Robotics output must have 'theta1', 'theta2', 'reachable'."""
        result = host.tick_robotics(0.05)
        assert "theta1" in result
        assert "theta2" in result
        assert "reachable" in result


# ══════════════════════════════════════════════════════════════════
#  Inbound Packet Parsing
# ══════════════════════════════════════════════════════════════════

class TestInboundParsing:
    """Validates JSON ingestion and state mutation safety."""

    def test_valid_mechanical_payload(self):
        """A well-formed mechanical packet should update throttle."""
        data = json.dumps({"mode": 0, "throttle": 0.75}).encode("utf-8")
        host.process_inbound(data)

        assert host.state["mode"] == 0
        assert host.state["throttle"] == pytest.approx(0.75)

    def test_valid_electronics_payload(self):
        """A well-formed electronics packet should update pot and circuit."""
        data = json.dumps({
            "mode": 1,
            "potentiometer_val": 0.6,
            "circuit_closed": True,
        }).encode("utf-8")
        host.process_inbound(data)

        assert host.state["mode"] == 1
        assert host.state["potentiometer_val"] == pytest.approx(0.6)
        assert host.state["circuit_closed"] is True

    def test_valid_robotics_payload(self):
        """A well-formed robotics packet should update target coords."""
        data = json.dumps({
            "mode": 2,
            "target_x": 0.8,
            "target_y": 1.2,
        }).encode("utf-8")
        host.process_inbound(data)

        assert host.state["mode"] == 2
        assert host.state["target_x"] == pytest.approx(0.8)
        assert host.state["target_y"] == pytest.approx(1.2)

    def test_malformed_json_does_not_crash(self):
        """Garbage bytes must be silently dropped."""
        host.process_inbound(b"{this is not valid json!!")
        # No exception expected — state unchanged.

    def test_empty_bytes_do_not_crash(self):
        """Empty input must be silently dropped."""
        host.process_inbound(b"")

    def test_non_dict_json_ignored(self):
        """A JSON array or scalar should be silently ignored."""
        host.process_inbound(json.dumps([1, 2, 3]).encode("utf-8"))
        host.process_inbound(json.dumps(42).encode("utf-8"))
        host.process_inbound(json.dumps("hello").encode("utf-8"))

    def test_partial_payload_updates_only_present_keys(self):
        """A packet with only 'throttle' should not alter electronics state."""
        original_pot = host.state["potentiometer_val"]
        data = json.dumps({"throttle": 0.5}).encode("utf-8")
        host.process_inbound(data)

        assert host.state["throttle"] == pytest.approx(0.5)
        assert host.state["potentiometer_val"] == pytest.approx(original_pot)

    def test_invalid_type_for_throttle_ignored(self):
        """Non-numeric throttle value should be silently skipped."""
        original = host.state["throttle"]
        data = json.dumps({"throttle": "not_a_number"}).encode("utf-8")
        host.process_inbound(data)

        assert host.state["throttle"] == pytest.approx(original)

    def test_invalid_type_for_mode_ignored(self):
        """Non-integer mode should be silently skipped."""
        original = host.state["mode"]
        data = json.dumps({"mode": "abc"}).encode("utf-8")
        host.process_inbound(data)

        assert host.state["mode"] == original


# ══════════════════════════════════════════════════════════════════
#  Outbound JSON Payload Schema
# ══════════════════════════════════════════════════════════════════

class TestOutboundPayloadSchema:
    """Validates that outbound JSON conforms to UDPBridge.cs DTO expectations."""

    @staticmethod
    def _build_outbound(mode):
        """Simulate one tick and build the outbound dict as main() would."""
        host.state["mode"] = mode

        mech = host.tick_mechanical(0.05)
        elec = host.tick_electronics(0.05)
        robo = host.tick_robotics(0.05)

        outbound = {
            "type": host._outbound_type_for_mode(mode),
            "mode": mode,
            "mech": mech,
            "elec": elec,
            "robotics": robo,
        }

        if mode == 0:
            outbound["engine_rpm"] = mech["rpm"]
            outbound["shake_intensity"] = mech["shake"]
        elif mode == 1:
            outbound["led_state"] = elec["led"]
            outbound["buzzer_state"] = elec["buzzer"]
        elif mode == 2:
            outbound["joint_angles"] = [robo["theta1"], robo["theta2"]]

        return outbound

    def test_mechanical_payload_has_required_fields(self):
        """Mode 0 payload must include 'type', 'engine_rpm', 'shake_intensity'."""
        payload = self._build_outbound(0)

        assert payload["type"] == "mechanical"
        assert "engine_rpm" in payload
        assert "shake_intensity" in payload
        assert isinstance(payload["engine_rpm"], float)
        assert isinstance(payload["shake_intensity"], float)

    def test_electronics_payload_has_required_fields(self):
        """Mode 1 payload must include 'type', 'led_state', 'buzzer_state'."""
        payload = self._build_outbound(1)

        assert payload["type"] == "electronics"
        assert "led_state" in payload
        assert "buzzer_state" in payload
        assert isinstance(payload["led_state"], int)
        assert isinstance(payload["buzzer_state"], int)

    def test_robotics_payload_has_required_fields(self):
        """Mode 2 payload must include 'type' and 'joint_angles' array."""
        payload = self._build_outbound(2)

        assert payload["type"] == "robotics"
        assert "joint_angles" in payload
        assert isinstance(payload["joint_angles"], list)
        assert len(payload["joint_angles"]) == 2

    def test_payload_is_json_serializable(self):
        """Every outbound payload must serialise to valid JSON without error."""
        for mode in (0, 1, 2):
            payload = self._build_outbound(mode)
            serialized = json.dumps(payload)
            roundtrip = json.loads(serialized)
            assert roundtrip["mode"] == mode

    def test_all_modes_include_sub_objects(self):
        """Every mode should include mech, elec, and robotics sub-objects."""
        for mode in (0, 1, 2):
            payload = self._build_outbound(mode)
            assert "mech" in payload
            assert "elec" in payload
            assert "robotics" in payload

    def test_type_string_mapping(self):
        """Mode-to-type string mapping must match UDPBridge.cs DTO class names."""
        assert host._outbound_type_for_mode(0) == "mechanical"
        assert host._outbound_type_for_mode(1) == "electronics"
        assert host._outbound_type_for_mode(2) == "robotics"
        assert host._outbound_type_for_mode(99) == "unknown"
