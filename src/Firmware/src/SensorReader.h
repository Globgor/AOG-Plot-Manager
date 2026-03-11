#ifndef SENSOR_READER_H
#define SENSOR_READER_H

#include "Config.h"
#include <Arduino.h>

/// @brief ISR-driven pulse counting for flow meters + analog air-pressure
/// reading.
///
/// Flow meters produce hall-effect pulses proportional to flow rate.
/// Each rising edge increments a volatile counter inside a hardware ISR.
/// `sampleAndReset()` atomically reads & clears all counters and computes
/// frequency in Hz based on elapsed time since the last sample.
///
/// Air pressure is a simple analog voltage read (0–3.3 V → 0–10 Bar after
/// calibration on the PC side via MachineProfile constants).
///
/// Zero dynamic allocation. All arrays are fixed-size [NUM_FLOW_METERS].
class SensorReader {
public:
  /// @brief Attaches ISRs and configures ADC pin.
  void begin() {
    // Air pressure — analog input
    pinMode(AIR_PRESSURE_PIN, INPUT);

    // Flow meters — digital inputs with hardware interrupts
    for (int i = 0; i < NUM_FLOW_METERS; i++) {
      pinMode(FLOW_METER_PINS[i], INPUT_PULLUP);
      _pulseCounters[i] = 0;
      _flowHz[i] = 0.0;
    }

    // Attach interrupt for each flow meter pin.
    // Teensy 4.1: all digital pins are interrupt-capable.
    attachInterrupt(digitalPinToInterrupt(FLOW_METER_PINS[0]), isr0, RISING);
    attachInterrupt(digitalPinToInterrupt(FLOW_METER_PINS[1]), isr1, RISING);
    attachInterrupt(digitalPinToInterrupt(FLOW_METER_PINS[2]), isr2, RISING);
    attachInterrupt(digitalPinToInterrupt(FLOW_METER_PINS[3]), isr3, RISING);
    attachInterrupt(digitalPinToInterrupt(FLOW_METER_PINS[4]), isr4, RISING);
    attachInterrupt(digitalPinToInterrupt(FLOW_METER_PINS[5]), isr5, RISING);
    attachInterrupt(digitalPinToInterrupt(FLOW_METER_PINS[6]), isr6, RISING);
    attachInterrupt(digitalPinToInterrupt(FLOW_METER_PINS[7]), isr7, RISING);
    attachInterrupt(digitalPinToInterrupt(FLOW_METER_PINS[8]), isr8, RISING);
    attachInterrupt(digitalPinToInterrupt(FLOW_METER_PINS[9]), isr9, RISING);

    _lastSampleMicros = micros();
    _airVoltage = 0.0;
  }

  /// @brief Called from loop(). Reads analog input and computes flow Hz
  ///        if enough time has elapsed since the last sample.
  ///
  /// Should be called every loop iteration; internally gates on
  /// STATUS_REPORT_INTERVAL_MS to avoid excessive ADC reads.
  void update() {
    unsigned long now = micros();
    // Note: unsigned subtraction handles 32-bit overflow correctly
    // (wraps every ~71 minutes on Teensy 4.1). No special guard needed.
    unsigned long elapsedUs = now - _lastSampleMicros;

    // Only sample at the telemetry rate (100 ms = 100000 µs)
    if (elapsedUs < (TELEMETRY_INTERVAL_MS * 1000UL)) {
      return;
    }

    // Read air pressure (analog)
    int rawAdc = analogRead(AIR_PRESSURE_PIN);
    _airVoltage = (static_cast<double>(rawAdc) / ADC_RESOLUTION) * ADC_VREF;

    // Atomically read and reset pulse counters
    double elapsedSec = static_cast<double>(elapsedUs) / 1000000.0;

    noInterrupts();
    for (int i = 0; i < NUM_FLOW_METERS; i++) {
      uint32_t pulses = _pulseCounters[i];
      _pulseCounters[i] = 0;
      _flowHz[i] =
          (elapsedSec > 0.0) ? static_cast<double>(pulses) / elapsedSec : 0.0;
    }
    interrupts();

    _lastSampleMicros = now;
    _dataReady = true;
  }

  /// @brief Returns the latest air pressure sensor voltage (0–3.3 V).
  double getAirVoltage() const { return _airVoltage; }

  /// @brief Returns pointer to the flow frequency array (Hz). Length =
  /// NUM_FLOW_METERS.
  const double *getFlowHz() const { return _flowHz; }

  /// @brief Whether new data has been computed since the last call to
  /// clearDataReady().
  bool isDataReady() const { return _dataReady; }

  /// @brief Clears the data-ready flag (called after telemetry is sent).
  void clearDataReady() { _dataReady = false; }

private:
  double _airVoltage = 0.0;
  double _flowHz[NUM_FLOW_METERS] = {};
  unsigned long _lastSampleMicros = 0;
  bool _dataReady = false;

  // ── Volatile pulse counters (written by ISRs, read by update()) ──
  static volatile uint32_t _pulseCounters[NUM_FLOW_METERS];

  // ── ISR trampolines (one per channel — C++ member ISRs not supported) ──
  static void isr0() { _pulseCounters[0]++; }
  static void isr1() { _pulseCounters[1]++; }
  static void isr2() { _pulseCounters[2]++; }
  static void isr3() { _pulseCounters[3]++; }
  static void isr4() { _pulseCounters[4]++; }
  static void isr5() { _pulseCounters[5]++; }
  static void isr6() { _pulseCounters[6]++; }
  static void isr7() { _pulseCounters[7]++; }
  static void isr8() { _pulseCounters[8]++; }
  static void isr9() { _pulseCounters[9]++; }
};

// Static member definition (outside class, in header — OK for single-TU
// firmware)
volatile uint32_t SensorReader::_pulseCounters[NUM_FLOW_METERS] = {};

#endif // SENSOR_READER_H
