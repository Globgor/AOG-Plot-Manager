#ifndef DIAGNOSTICS_H
#define DIAGNOSTICS_H

#include "Config.h"
#include "ValveController.h"
#include <Arduino.h>

/// @brief Diagnostics and heartbeat monitoring.
///        If no heartbeat is received within HEARTBEAT_TIMEOUT_MS,
///        triggers automatic emergency stop (fail-safe).
class Diagnostics {
public:
  /// @brief Initializes diagnostics: LED pin, heartbeat timer.
  void begin() {
    pinMode(LED_PIN, OUTPUT);
    _lastHeartbeat = millis();
    _heartbeatTimeoutMs = HEARTBEAT_TIMEOUT_MS_DEFAULT;
  }

  /// @brief Records a heartbeat reception.
  void recordHeartbeat() {
    _lastHeartbeat = millis();
    _heartbeatLost = false;
  }

  /// @brief Checks if heartbeat has timed out.
  /// @return true if heartbeat is lost (fail-safe should activate).
  bool isHeartbeatLost() {
    if (millis() - _lastHeartbeat > _heartbeatTimeoutMs) {
      _heartbeatLost = true;
    }
    return _heartbeatLost;
  }

  /// @brief Updates the status LED based on system state.
  /// @param valvesActive Whether any valve is currently open.
  void updateLed(bool valvesActive) {
    if (_heartbeatLost) {
      // Fast blink = heartbeat lost (error)
      digitalWrite(LED_PIN, (millis() / 100) % 2 ? HIGH : LOW);
    } else if (valvesActive) {
      // Solid ON = spraying
      digitalWrite(LED_PIN, HIGH);
    } else {
      // Slow blink = connected, idle
      digitalWrite(LED_PIN, (millis() / 500) % 2 ? HIGH : LOW);
    }
  }

  /// @brief Sends status report packet over Serial.
  /// @param valveController Reference to valve controller for current mask.
  void sendStatusReport(const ValveController &valveController) {
    if (millis() - _lastStatusReport < STATUS_REPORT_INTERVAL_MS) {
      return; // Not time yet
    }
    _lastStatusReport = millis();

    uint16_t mask = valveController.getCurrentMask();
    uint8_t errorFlags = 0;
    if (_heartbeatLost)
      errorFlags |= 0x01; // Bit 0: heartbeat lost

    // Build response packet: [SYNC1] [SYNC2] [RESP_STATUS] [MASK_MSB]
    // [MASK_LSB] [ERR] [CRC]
    uint8_t packet[7];
    packet[0] = SYNC_BYTE_1;
    packet[1] = SYNC_BYTE_2;
    packet[2] = RESP_STATUS;
    packet[3] = (uint8_t)(mask >> 8);
    packet[4] = (uint8_t)(mask & 0xFF);
    packet[5] = errorFlags;
    packet[6] = packet[2] ^ packet[3] ^ packet[4] ^ packet[5]; // CRC

    Serial.write(packet, sizeof(packet));
  }

  /// @brief Builds and sends an ACK response.
  /// @param ackedCommand The command byte being acknowledged.
  void sendAck(uint8_t ackedCommand) {
    uint8_t packet[5];
    packet[0] = SYNC_BYTE_1;
    packet[1] = SYNC_BYTE_2;
    packet[2] = RESP_ACK;
    packet[3] = ackedCommand;
    packet[4] = packet[2] ^ packet[3]; // CRC

    Serial.write(packet, sizeof(packet));
  }

private:
  unsigned long _lastHeartbeat = 0;
  unsigned long _lastStatusReport = 0;
  unsigned long _heartbeatTimeoutMs = HEARTBEAT_TIMEOUT_MS_DEFAULT;
  bool _heartbeatLost = false;
};

#endif // DIAGNOSTICS_H
