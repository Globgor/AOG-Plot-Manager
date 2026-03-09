#ifndef VALVE_CONTROLLER_H
#define VALVE_CONTROLLER_H

#include <Arduino.h>
#include "Config.h"

/// @brief Controls 14 solenoid valves via MOSFET drivers (D4184).
///        Provides atomic set, individual control, and safety shutdown.
class ValveController {
public:
    /// @brief Initializes all valve pins as OUTPUT and sets them LOW.
    void begin() {
        for (int i = 0; i < NUM_VALVES; i++) {
            pinMode(VALVE_PINS[i], OUTPUT);
            digitalWrite(VALVE_PINS[i], LOW);
        }
        _currentMask = VALVE_MASK_ALL_OFF;
    }

    /// @brief Sets all 14 valves atomically from a 14-bit mask.
    /// @param mask Bit N = valve N state (1=OPEN, 0=CLOSED). Upper 2 bits ignored.
    void setValves(uint16_t mask) {
        mask &= VALVE_MASK_14BIT;
        for (int i = 0; i < NUM_VALVES; i++) {
            digitalWrite(VALVE_PINS[i], (mask >> i) & 1 ? HIGH : LOW);
        }
        _currentMask = mask;
    }

    /// @brief Emergency shutdown — close ALL valves immediately.
    void emergencyStop() {
        for (int i = 0; i < NUM_VALVES; i++) {
            digitalWrite(VALVE_PINS[i], LOW);
        }
        _currentMask = VALVE_MASK_ALL_OFF;
    }

    /// @brief Returns the current valve mask.
    uint16_t getCurrentMask() const {
        return _currentMask;
    }

    /// @brief Opens a single valve by index (0-13).
    void openValve(int index) {
        if (index >= 0 && index < NUM_VALVES) {
            digitalWrite(VALVE_PINS[index], HIGH);
            _currentMask |= (1 << index);
        }
    }

    /// @brief Closes a single valve by index (0-13).
    void closeValve(int index) {
        if (index >= 0 && index < NUM_VALVES) {
            digitalWrite(VALVE_PINS[index], LOW);
            _currentMask &= ~(1 << index);
        }
    }

private:
    uint16_t _currentMask = VALVE_MASK_ALL_OFF;
};

#endif // VALVE_CONTROLLER_H
