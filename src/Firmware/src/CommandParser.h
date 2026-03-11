#ifndef COMMAND_PARSER_H
#define COMMAND_PARSER_H

#include "Config.h"
#include <Arduino.h>

/// @brief Parsed command from PlotManager.
struct ParsedCommand {
  uint8_t command;       ///< Command byte (CMD_SET_VALVES, etc.)
  uint8_t payload[8];    ///< Payload data
  uint8_t payloadLength; ///< Actual payload length
  bool valid;            ///< Whether the command parsed successfully
};

/// @brief State machine parser for the PlotManager serial protocol.
///
/// Packet format: [0xAA] [0x55] [CMD] [DATA...] [CRC]
/// CRC = XOR of CMD and all DATA bytes.
class CommandParser {
public:
  /// @brief Feeds a single byte to the parser state machine.
  /// @param byte The received byte.
  /// @return true if a complete, valid command has been parsed.
  bool feed(uint8_t byte) {
    switch (_state) {
    case State::WaitSync1:
      if (byte == SYNC_BYTE_1) {
        _state = State::WaitSync2;
      }
      break;

    case State::WaitSync2:
      if (byte == SYNC_BYTE_2) {
        _state = State::WaitCmd;
      } else {
        _state = State::WaitSync1; // Reset on invalid sync
      }
      break;

    case State::WaitCmd:
      _cmd.command = byte;
      _cmd.payloadLength = 0;
      _cmd.valid = false;
      _expectedPayloadLen = getPayloadLength(byte);
      // Q1 FIX: Cap payload length to buffer size to prevent overflow
      if (_expectedPayloadLen > sizeof(_cmd.payload)) {
        _expectedPayloadLen = sizeof(_cmd.payload);
      }
      _runningCrc = byte;

      if (_expectedPayloadLen == 0) {
        _state = State::WaitCrc; // No payload, go to CRC
      } else {
        _state = State::WaitData;
      }
      break;

    case State::WaitData:
      if (_cmd.payloadLength < sizeof(_cmd.payload)) {
        _cmd.payload[_cmd.payloadLength] = byte;
      }
      _cmd.payloadLength++;
      _runningCrc ^= byte;

      if (_cmd.payloadLength >= _expectedPayloadLen) {
        _state = State::WaitCrc;
      }
      break;

    case State::WaitCrc:
      _state = State::WaitSync1; // Reset for next packet
      if (byte == _runningCrc) {
        _cmd.valid = true;
        return true; // Valid command!
      }
      // CRC mismatch — discard
      break;
    }

    return false;
  }

  /// @brief Returns the last parsed command.
  const ParsedCommand &getCommand() const { return _cmd; }

  /// @brief Resets the parser state machine.
  void reset() {
    _state = State::WaitSync1;
    _cmd = {};
  }

private:
  enum class State : uint8_t {
    WaitSync1,
    WaitSync2,
    WaitCmd,
    WaitData,
    WaitCrc,
  };

  State _state = State::WaitSync1;
  ParsedCommand _cmd = {};
  uint8_t _expectedPayloadLen = 0;
  uint8_t _runningCrc = 0;

  /// @brief Returns expected payload length for a given command.
  static uint8_t getPayloadLength(uint8_t cmd) {
    switch (cmd) {
    case CMD_SET_VALVES:
      return 2; // MSB, LSB
    case CMD_PRIME:
      return 4; // SectionMask(2) + Duration(2)
    case CMD_HEARTBEAT:
      return 0;
    case CMD_EMERGENCY_STOP:
      return 0;
    default:
      return 0;
    }
  }
};

#endif // COMMAND_PARSER_H
