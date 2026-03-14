using System;
using System.Diagnostics;
using System.Linq;

namespace PlotManager.Core.Providers;

/// <summary>
/// A physical provider that connects to a Teensy/Arduino over Serial or UDP.
/// Translates valve state commands into hardware-specific byte arrays or strings.
/// </summary>
public class TeensySerialProvider : IMachineControlProvider
{
    private bool _isConnected;
    public bool IsConnected => _isConnected;

    public void Connect()
    {
        // TODO: Initialize SerialPort or UdpClient
        _isConnected = true;
        Debug.WriteLine("TeensySerialProvider: Connected to hardware");
    }

    public void Disconnect()
    {
        // TODO: Close SerialPort or UdpClient
        _isConnected = false;
        Debug.WriteLine("TeensySerialProvider: Disconnected from hardware");
    }

    public void SetValveState(int valveId, bool isOpen)
    {
        // Translate a single valve command into a serial string, e.g. "V,1,1\n"
        if (!_isConnected) return;
        
        string command = $"V,{valveId},{(isOpen ? 1 : 0)}\n";
        Debug.WriteLine($"[Teensy] Sending: {command.Trim()}");
        // port.Write(command)
    }

    public void SetActiveValves(int[] valveIds)
    {
        // Sends an array of open valves using bitmask or CSV format
        // Example: "B,1,3,4\n" meaning open 1, 3, 4, close others (or 14-bit mask)
        if (!_isConnected) return;
        
        string idList = string.Join(",", valveIds);
        string maskCommand = $"B,{idList}\n";
        
        Debug.WriteLine($"[Teensy] Sending Bulk Valves: {maskCommand.Trim()}");
        // port.Write(maskCommand)
    }
}
