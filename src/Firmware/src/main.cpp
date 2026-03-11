// ============================================
// AOG Plot Manager — Teensy 4.1 Firmware
// Main Entry Point
// ============================================
//
// Receives commands from PlotManager over USB Serial,
// controls 14 solenoid valves via MOSFET drivers (D4184),
// reads sensor telemetry (air pressure + 10 flow meters),
// and streams JSON telemetry over Ethernet UDP at 10 Hz.
//
// Safety: If heartbeat is lost for >2 seconds, all valves
// are automatically closed (fail-safe) and Estop flag is
// set in the telemetry stream.
// ============================================

#include "CommandParser.h"
#include "Config.h"
#include "Diagnostics.h"
#include "SensorReader.h"
#include "TelemetrySender.h"
#include "ValveController.h"
#include <Arduino.h>

// -- Global instances --
CommandParser parser;
ValveController valves;
Diagnostics diagnostics;
SensorReader sensors;
TelemetrySender telemetry;

// -- Prime state --
bool primeActive = false;
unsigned long primeStartTime = 0;
unsigned long primeDurationMs = 0;
uint16_t primeMask = 0;

// -- Forward declarations --
void handleCommand(const ParsedCommand &cmd);

void setup() {
  Serial.begin(SERIAL_BAUD_RATE);

  valves.begin();
  diagnostics.begin();
  sensors.begin();

  // Initialize Ethernet + UDP (non-fatal if no link)
  bool ethOk = telemetry.begin();
  if (ethOk) {
    Serial.println(F("[TELEM] Ethernet UP, sending to 192.168.5.1:9999"));
  } else {
    Serial.println(F("[TELEM] Ethernet link DOWN — telemetry disabled"));
  }

  // Signal ready
  delay(100);
  diagnostics.sendAck(0x00); // Boot-up acknowledgement
}

void loop() {
  // 1. Read and parse incoming serial data
  while (Serial.available() > 0) {
    uint8_t byte = Serial.read();
    if (parser.feed(byte)) {
      handleCommand(parser.getCommand());
    }
  }

  // 2. Check heartbeat timeout (fail-safe)
  if (diagnostics.isHeartbeatLost()) {
    valves.emergencyStop();
    primeActive = false;
  }

  // 3. Handle prime timeout (L3 FIX: subtraction-based comparison handles millis() overflow)
  if (primeActive && (millis() - primeStartTime) >= primeDurationMs) {
    // Prime duration elapsed — close primed valves
    uint16_t currentMask = valves.getCurrentMask();
    valves.setValves(currentMask & ~primeMask);
    primeActive = false;
  }

  // 4. Update LED status
  diagnostics.updateLed(valves.getCurrentMask() != VALVE_MASK_ALL_OFF);

  // 5. Send periodic status report (binary serial → PlotManager)
  diagnostics.sendStatusReport(valves);

  // 6. Sample sensors & send JSON telemetry (UDP → PlotManager SensorHub)
  sensors.update();
  bool estop = diagnostics.isHeartbeatLost();
  telemetry.trySend(sensors, estop);
}

/// @brief Handles a parsed command from PlotManager.
void handleCommand(const ParsedCommand &cmd) {
  if (!cmd.valid)
    return;

  switch (cmd.command) {
  case CMD_SET_VALVES: {
    uint16_t mask = ((uint16_t)cmd.payload[0] << 8) | cmd.payload[1];
    valves.setValves(mask);
    diagnostics.sendAck(CMD_SET_VALVES);
    break;
  }

  case CMD_PRIME: {
    primeMask = ((uint16_t)cmd.payload[0] << 8) | cmd.payload[1];
    uint16_t durationMs = ((uint16_t)cmd.payload[2] << 8) | cmd.payload[3];
    primeMask &= VALVE_MASK_14BIT;

    // Open the specified valves for the specified duration
    uint16_t currentMask = valves.getCurrentMask();
    valves.setValves(currentMask | primeMask);
    primeStartTime = millis();
    primeDurationMs = durationMs;
    primeActive = true;

    diagnostics.sendAck(CMD_PRIME);
    break;
  }

  case CMD_HEARTBEAT: {
    diagnostics.recordHeartbeat();
    diagnostics.sendAck(CMD_HEARTBEAT);
    break;
  }

  case CMD_EMERGENCY_STOP: {
    // L7 FIX: Send ACK before acting — USB Serial is independent of valve power
    diagnostics.sendAck(CMD_EMERGENCY_STOP);
    valves.emergencyStop();
    primeActive = false;
    break;
  }

  default:
    break; // Unknown command — ignore
  }
}
