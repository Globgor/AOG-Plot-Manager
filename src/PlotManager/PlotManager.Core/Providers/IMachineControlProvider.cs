using System;

namespace PlotManager.Core.Providers;

public interface IMachineControlProvider
{
    bool IsConnected { get; }
    void Connect();
    void Disconnect();

    /// <summary>
    /// Sets the state of a specific valve/relay (e.g. 1 to 14)
    /// </summary>
    void SetValveState(int valveId, bool isOpen);

    /// <summary>
    /// Opens all specified valves and closes the rest.
    /// </summary>
    void SetActiveValves(int[] valveIds);
}
