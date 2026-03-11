#ifndef TELEMETRY_SENDER_H
#define TELEMETRY_SENDER_H

#include "Config.h"
#include "SensorReader.h"
#include <Arduino.h>
#include <ArduinoJson.h>
#include <NativeEthernet.h>

/// @brief Non-blocking JSON UDP telemetry sender.
///
/// Serializes sensor readings into a compact JSON payload matching the
/// C# RawTelemetry model expected by PlotManager SensorHub (port 9999):
///
///   {"T":345098,"AirV":1.75,"FlowHz":[25.0,0.0,...,0.0],"Estop":false}
///
/// Uses NativeEthernet (hardware MAC on Teensy 4.1) with a static IP.
/// ArduinoJson v7 JsonDocument handles memory internally.
/// Sending is gated by the data-ready flag from SensorReader, so the
/// rate is determined by SensorReader::update() (~10 Hz).
class TelemetrySender {
public:
  /// @brief Initializes Ethernet with static IP and opens a UDP socket.
  /// @return true if Ethernet link was established.
  bool begin() {
    // Copy constexpr arrays into mutable locals for the Ethernet API
    uint8_t mac[6];
    for (int i = 0; i < 6; i++)
      mac[i] = TEENSY_MAC[i];

    IPAddress ip(TEENSY_IP[0], TEENSY_IP[1], TEENSY_IP[2], TEENSY_IP[3]);
    IPAddress subnet(TEENSY_SUBNET[0], TEENSY_SUBNET[1], TEENSY_SUBNET[2],
                     TEENSY_SUBNET[3]);

    Ethernet.begin(mac, ip, ip, ip,
                   subnet); // ip, dns, gw all = ip (no gateway needed on LAN)

    // Brief settle time for the PHY
    delay(50);

    if (Ethernet.linkStatus() == LinkOFF) {
      _linkUp = false;
      // Non-fatal: firmware continues without telemetry.
      // Link is re-checked on every trySend().
      return false;
    }

    _linkUp = true;
    _udp.begin(TELEMETRY_LOCAL_PORT);
    _targetIp =
        IPAddress(TARGET_IP[0], TARGET_IP[1], TARGET_IP[2], TARGET_IP[3]);
    return true;
  }

  /// @brief Attempts to send a telemetry packet if new sensor data is ready.
  ///
  /// Call this from loop() every iteration. It only sends when
  /// SensorReader signals data-ready, keeping the rate at ~10 Hz.
  ///
  /// @param sensors  Reference to the sensor reader (provides AirV, FlowHz).
  /// @param estop    True if the system is in E-STOP state (heartbeat lost).
  void trySend(SensorReader &sensors, bool estop) {
    if (!sensors.isDataReady()) {
      return; // No new data since last send
    }

    // Re-check Ethernet link (cable might be reconnected in the field)
    if (!_linkUp) {
      if (Ethernet.linkStatus() != LinkON) {
        sensors.clearDataReady();
        return; // Still no link — skip silently
      }
      _linkUp = true;
      _udp.begin(TELEMETRY_LOCAL_PORT);
    }

    // Build JSON document (ArduinoJson v7 manages memory internally)
    JsonDocument doc;

    doc["T"] = millis();
    doc["AirV"] = roundTo2(sensors.getAirVoltage());
    doc["Estop"] = estop;

    JsonArray flowArr = doc["FlowHz"].to<JsonArray>();
    const double *flowHz = sensors.getFlowHz();
    for (int i = 0; i < NUM_FLOW_METERS; i++) {
      flowArr.add(roundTo1(flowHz[i]));
    }

    // Serialize to internal buffer
    size_t len = serializeJson(doc, _jsonBuf, sizeof(_jsonBuf));

    // Send UDP packet
    _udp.beginPacket(_targetIp, TELEMETRY_UDP_PORT);
    _udp.write(reinterpret_cast<const uint8_t *>(_jsonBuf), len);
    _udp.endPacket();

    sensors.clearDataReady();
    _packetsSent++;
  }

  /// @brief Total number of telemetry packets sent since boot.
  unsigned long getPacketsSent() const { return _packetsSent; }

  /// @brief Whether the Ethernet link is currently up.
  bool isLinkUp() const { return _linkUp; }

private:
  EthernetUDP _udp;
  IPAddress _targetIp;
  char _jsonBuf[256]; // Serialization buffer (payload is ~120 bytes typical)
  unsigned long _packetsSent = 0;
  bool _linkUp = false;

  /// @brief Rounds a double to 2 decimal places (avoids long float strings in
  /// JSON).
  static double roundTo2(double v) {
    return static_cast<long>(v * 100.0 + 0.5) / 100.0;
  }

  /// @brief Rounds a double to 1 decimal place.
  static double roundTo1(double v) {
    return static_cast<long>(v * 10.0 + 0.5) / 10.0;
  }
};

#endif // TELEMETRY_SENDER_H
