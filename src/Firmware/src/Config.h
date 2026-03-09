#ifndef CONFIG_H
#define CONFIG_H

#include <stdint.h>

// ============================================
// AOG Plot Manager — Teensy 4.1 Firmware
// Hardware Pin Mapping & Constants
// ============================================

// -- Number of valve outputs --
constexpr int NUM_VALVES = 14;

// -- Valve MOSFET output pins (D4184 modules) --
// Teensy 4.1 digital pins connected to MOSFET gate drivers.
// Adjust these to match your physical wiring.
constexpr int VALVE_PINS[NUM_VALVES] = {
    2,  // Section 0
    3,  // Section 1
    4,  // Section 2
    5,  // Section 3
    6,  // Section 4
    7,  // Section 5
    8,  // Section 6
    9,  // Section 7
    10, // Section 8
    11, // Section 9
    12, // Section 10
    24, // Section 11
    25, // Section 12
    26, // Section 13
};

// -- Protocol constants --
constexpr uint8_t SYNC_BYTE_1 = 0xAA;
constexpr uint8_t SYNC_BYTE_2 = 0x55;

// Commands from PlotManager
constexpr uint8_t CMD_SET_VALVES = 0x01;
constexpr uint8_t CMD_PRIME = 0x02;
constexpr uint8_t CMD_HEARTBEAT = 0x03;
constexpr uint8_t CMD_EMERGENCY_STOP = 0x04;

// Responses to PlotManager
constexpr uint8_t RESP_STATUS = 0x80;
constexpr uint8_t RESP_ACK = 0x81;

// -- Timing --
constexpr unsigned long HEARTBEAT_TIMEOUT_MS_DEFAULT = 2000;
constexpr unsigned long STATUS_REPORT_INTERVAL_MS = 100;
constexpr unsigned long SERIAL_BAUD_RATE = 115200;

// -- Safety --
constexpr uint16_t VALVE_MASK_ALL_OFF = 0x0000;
constexpr uint16_t VALVE_MASK_14BIT = 0x3FFF;

// -- Status LED --
constexpr int LED_PIN = 13; // Built-in LED on Teensy 4.1

#endif // CONFIG_H
