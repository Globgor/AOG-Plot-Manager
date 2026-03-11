using PlotManager.Core.Services;

namespace PlotManager.Tests;

/// <summary>
/// Tests for AutoWeatherService: NMEA parsing and stationary-detection state machine.
/// </summary>
public class AutoWeatherServiceTests
{
    // ════════════════════════════════════════════════════════════════════
    // ParseWimwv — unit conversion
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParseWimwv_Knots_ConvertsToMs()
    {
        // $WIMWV,007,R,4.1,N,A*3F → example from NMEA spec
        var result = AutoWeatherService.ParseWimwv("$WIMWV,007,R,4.1,N,A*3F");
        Assert.NotNull(result);
        Assert.Equal(7.0, result.Value.AngleDeg, 1);
        Assert.Equal(4.1 * 0.514444, result.Value.SpeedMs, 3);
    }

    [Fact]
    public void ParseWimwv_Kmh_ConvertsToMs()
    {
        var result = AutoWeatherService.ParseWimwv("$WIMWV,180,R,36.0,K,A");
        Assert.NotNull(result);
        Assert.Equal(180.0, result.Value.AngleDeg, 1);
        Assert.Equal(10.0, result.Value.SpeedMs, 3); // 36 km/h = 10 m/s
    }

    [Fact]
    public void ParseWimwv_MetersPerSecond_PassedThrough()
    {
        var result = AutoWeatherService.ParseWimwv("$WIMWV,090,R,5.5,M,A");
        Assert.NotNull(result);
        Assert.Equal(5.5, result.Value.SpeedMs, 3);
    }

    [Fact]
    public void ParseWimwv_InvalidStatus_V_ReturnsNull()
    {
        // V = invalid reading — should be rejected
        var result = AutoWeatherService.ParseWimwv("$WIMWV,090,R,5.5,M,V");
        Assert.Null(result);
    }

    [Fact]
    public void ParseWimwv_WrongPrefix_ReturnsNull()
    {
        var result = AutoWeatherService.ParseWimwv("$GPRMC,123456,A,4807.038,N,01131.000,E,0.0,0.0,010203,,,A*68");
        Assert.Null(result);
    }

    [Fact]
    public void ParseWimwv_TooFewFields_ReturnsNull()
    {
        var result = AutoWeatherService.ParseWimwv("$WIMWV,090,R");
        Assert.Null(result);
    }

    [Fact]
    public void ParseWimwv_NonNumericSpeed_ReturnsNull()
    {
        var result = AutoWeatherService.ParseWimwv("$WIMWV,090,R,notaNumber,M,A");
        Assert.Null(result);
    }

    [Fact]
    public void ParseWimwv_CaseInsensitivePrefix()
    {
        var lower = AutoWeatherService.ParseWimwv("$wimwv,090,R,3.0,M,A");
        Assert.NotNull(lower);
    }

    // ════════════════════════════════════════════════════════════════════
    // ParseWimda — temperature & humidity extraction
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParseWimda_ExtractsTempAndHumidity()
    {
        // WIMDA sentence: fields are comma-separated
        // index: 0=$WIMDA,1=barHg,2=I,3=barMbar,4=B,5=tempC,6=C,7=,8=,9=humidity,...
        // Build a sentence where parts[5]=22.5 and parts[9]=65.0
        string sentence = "$WIMDA,29.87,I,1.012,B,22.5,C,,,65.0,";
        var result = AutoWeatherService.ParseWimda(sentence);
        Assert.NotNull(result);
        Assert.Equal(22.5, result.Value.TempC, 1);
        Assert.Equal(65.0, result.Value.Humidity, 1);
    }

    [Fact]
    public void ParseWimda_WrongPrefix_ReturnsNull()
    {
        var result = AutoWeatherService.ParseWimda("$WIMWV,007,R,4.1,N,A");
        Assert.Null(result);
    }

    [Fact]
    public void ParseWimda_TooFewFields_ReturnsNull()
    {
        var result = AutoWeatherService.ParseWimda("$WIMDA,1,2,3");
        Assert.Null(result);
    }

    // ════════════════════════════════════════════════════════════════════
    // UpdateSpeed — stationary state machine
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void UpdateSpeed_MovingThenStopping_TransitionsIsStationary()
    {
        var svc = new AutoWeatherService { StationaryThresholdMs = 10_000 };

        svc.UpdateSpeed(5.0); // moving
        Assert.False(svc.IsStationary);

        svc.UpdateSpeed(0.0); // stopped
        Assert.True(svc.IsStationary);
    }

    [Fact]
    public void UpdateSpeed_Moving_DoesNotTrigger()
    {
        bool triggered = false;
        var svc = new AutoWeatherService { StationaryThresholdMs = 100 };
        svc.OnWeatherFetchRequired += () => triggered = true;

        svc.UpdateSpeed(3.0);
        Thread.Sleep(200);
        svc.UpdateSpeed(3.0); // still moving, no trigger
        Assert.False(triggered);
    }

    [Fact]
    public void UpdateSpeed_LongStationary_FiresEvent()
    {
        bool triggered = false;
        var svc = new AutoWeatherService { StationaryThresholdMs = 50 };
        svc.OnWeatherFetchRequired += () => triggered = true;

        svc.UpdateSpeed(0); // start stationary timer
        Thread.Sleep(80);
        svc.UpdateSpeed(0); // check threshold
        Assert.True(triggered);
    }

    [Fact]
    public void ResetTrigger_AllowsSecondFire()
    {
        int count = 0;
        var svc = new AutoWeatherService { StationaryThresholdMs = 50 };
        svc.OnWeatherFetchRequired += () => count++;

        svc.UpdateSpeed(0);
        Thread.Sleep(120);
        svc.UpdateSpeed(0);
        Assert.Equal(1, count);

        svc.ResetTrigger();

        // After reset, moving and stopping again should re-fire
        svc.UpdateSpeed(5); // reset IsStationary
        svc.UpdateSpeed(0); // restart stationary timer
        Thread.Sleep(120);
        svc.UpdateSpeed(0);
        Assert.Equal(2, count);
    }

    [Fact]
    public void UpdateSpeed_AfterMoving_ResetsTrigger()
    {
        bool triggered = false;
        var svc = new AutoWeatherService { StationaryThresholdMs = 50 };
        svc.OnWeatherFetchRequired += () => triggered = true;

        svc.UpdateSpeed(0);
        Thread.Sleep(80);
        svc.UpdateSpeed(0);
        Assert.True(triggered);

        // Resume movement — should clear state
        svc.UpdateSpeed(5);
        Assert.False(svc.IsStationary);
    }

    [Fact]
    public void Dispose_PreventsUpdates()
    {
        var svc = new AutoWeatherService();
        svc.Dispose();
        // Should not throw
        svc.UpdateSpeed(0);
        svc.ResetTrigger();
    }
}
