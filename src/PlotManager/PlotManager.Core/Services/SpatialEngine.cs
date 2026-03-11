namespace PlotManager.Core.Services;

using PlotManager.Core.Models;

/// <summary>
/// Represents the spatial state of the boom relative to the plot grid.
/// </summary>
public enum BoomState
{
    /// <summary>Boom is outside the grid entirely.</summary>
    OutsideGrid,

    /// <summary>Boom is in a buffer zone (alley) between plots.</summary>
    InAlley,

    /// <summary>Boom is within 0.5m of entering a plot — valves should pre-activate.</summary>
    ApproachingPlot,

    /// <summary>Boom is fully inside a plot — valves active.</summary>
    InPlot,

    /// <summary>Boom is within 0.2m of leaving a plot — valves should pre-deactivate.</summary>
    LeavingPlot,
}

/// <summary>
/// Result of a spatial evaluation: what state the boom is in,
/// which plot (if any), computed valve mask, and distances.
/// </summary>
public record SpatialResult
{
    /// <summary>Current boom state.</summary>
    public required BoomState State { get; init; }

    /// <summary>The plot the boom is in or approaching (null if OutsideGrid/InAlley).</summary>
    public Plot? ActivePlot { get; init; }

    /// <summary>Product assigned to the active plot (null if no plot).</summary>
    public string? ActiveProduct { get; init; }

    /// <summary>14-bit valve mask to send to Teensy. 0 = all off.</summary>
    public ushort ValveMask { get; init; }

    /// <summary>Distance to the nearest plot boundary along the heading (meters). Positive = ahead.</summary>
    public double DistanceToBoundaryMeters { get; init; }

    /// <summary>Computed activation distance at current speed (meters).</summary>
    public double ActivationDistanceMeters { get; init; }

    /// <summary>Computed deactivation distance at current speed (meters).</summary>
    public double DeactivationDistanceMeters { get; init; }
}

/// <summary>
/// Determines which plot (if any) the tractor is currently over,
/// and which sections should be active based on the trial map and hardware routing.
/// Implements look-ahead logic for fluid dynamics compensation.
/// </summary>
public class SpatialEngine
{
    private PlotGrid? _grid;
    private TrialMap? _trialMap;
    private HardwareRouting? _routing;

    // ── Cached grid bounds for fast point-in-grid check ──
    private GeoPoint _gridSouthWest;
    private GeoPoint _gridNorthEast;

    // ── Acceleration tracking with EMA filter ──
    private double _prevSpeedKmh;
    private DateTime _prevSpeedTime = DateTime.MinValue;
    private double _rawAcceleration;

    // ── Overlap hysteresis ──
    private int _prevBoomMask;

    /// <summary>
    /// User-facing pre-activation distance in meters.
    /// The desired physical offset before the plot boundary where spray should start.
    /// Set by the operator based on field experience.
    /// </summary>
    public double PreActivationMeters { get; set; } = 0.5;

    /// <summary>
    /// User-facing pre-deactivation distance in meters.
    /// The desired physical offset before the plot exit where spray should stop.
    /// </summary>
    public double PreDeactivationMeters { get; set; } = 0.2;

    /// <summary>
    /// Valve system delay in milliseconds (activation).
    /// Time from valve-open command to fluid reaching the nozzle tip.
    /// </summary>
    public double SystemActivationDelayMs { get; set; } = 300;

    /// <summary>
    /// Valve system delay in milliseconds (deactivation).
    /// </summary>
    public double SystemDeactivationDelayMs { get; set; } = 150;

    /// <summary>
    /// EMA smoothing factor for acceleration (0–1).
    /// 0.0 = no smoothing (raw dv/dt), 1.0 = ignores new data.
    /// Default 0.3 = moderate smoothing, filters GPS noise.
    /// </summary>
    public double AccelerationSmoothingAlpha { get; set; } = 0.3;

    /// <summary>Current estimated acceleration in m/s² (EMA-filtered).</summary>
    public double CurrentAccelerationMs2 { get; private set; }

    /// <summary>
    /// Minimum Heading-vs-COG difference (degrees) to use COG for rear boom projection.
    /// Below threshold: heading is used. Above: COG is used for rear booms (Y < 0).
    /// </summary>
    public double CogHeadingThresholdDegrees { get; set; } = 3.0;

    /// <summary>
    /// Speed below which heading is frozen to last valid value (km/h).
    /// Prevents GPS COG/heading jitter from causing chaotic valve switching at standstill.
    /// Default: 1.0 km/h. Set to 0 to disable.
    /// </summary>
    public double FreezeHeadingBelowSpeedKmh { get; set; } = 1.0;

    /// <summary>Last valid heading used for freeze logic.</summary>
    private double _frozenHeadingDegrees;

    /// <summary>
    /// Computes the effective activation distance using a specific delay value.
    /// Used for per-boom delay overrides.
    /// </summary>
    public double GetEffectiveActivationDistance(double speedKmh, double delayMs)
    {
        double speedMs = speedKmh / 3.6;
        double delaySeconds = delayMs / 1000.0;
        double predictedSpeedMs = Math.Max(0, speedMs + CurrentAccelerationMs2 * delaySeconds);
        return PreActivationMeters + (predictedSpeedMs * delaySeconds);
    }

    /// <summary>
    /// Computes the effective activation distance using global system delay.
    /// </summary>
    public double GetEffectiveActivationDistance(double speedKmh)
        => GetEffectiveActivationDistance(speedKmh, SystemActivationDelayMs);

    /// <summary>
    /// Computes the effective deactivation distance using a specific delay value.
    /// </summary>
    public double GetEffectiveDeactivationDistance(double speedKmh, double delayMs)
    {
        double speedMs = speedKmh / 3.6;
        double delaySeconds = delayMs / 1000.0;
        double predictedSpeedMs = Math.Max(0, speedMs + CurrentAccelerationMs2 * delaySeconds);
        return PreDeactivationMeters + (predictedSpeedMs * delaySeconds);
    }

    /// <summary>
    /// Computes the effective deactivation distance using global system delay.
    /// </summary>
    public double GetEffectiveDeactivationDistance(double speedKmh)
        => GetEffectiveDeactivationDistance(speedKmh, SystemDeactivationDelayMs);

    /// <summary>
    /// Updates acceleration estimate from consecutive speed readings.
    /// Uses EMA (Exponential Moving Average) to filter GPS noise.
    /// Call this on every GPS update cycle.
    /// </summary>
    public void UpdateAcceleration(double speedKmh)
    {
        DateTime now = DateTime.UtcNow;
        if (_prevSpeedTime != DateTime.MinValue)
        {
            double dt = (now - _prevSpeedTime).TotalSeconds;
            if (dt > 0.01 && dt < 5.0) // Sanity: 10ms–5s window
            {
                double dv = (speedKmh / 3.6) - (_prevSpeedKmh / 3.6);
                _rawAcceleration = dv / dt;

                // EMA filter: smoothed = alpha * old + (1 - alpha) * new
                CurrentAccelerationMs2 = AccelerationSmoothingAlpha * CurrentAccelerationMs2
                    + (1.0 - AccelerationSmoothingAlpha) * _rawAcceleration;
            }
        }
        _prevSpeedKmh = speedKmh;
        _prevSpeedTime = now;
    }

    /// <summary>Raw (unfiltered) acceleration for diagnostics.</summary>
    public double RawAccelerationMs2 => _rawAcceleration;

    /// <summary>Whether the engine is fully configured and ready to evaluate.</summary>
    public bool IsConfigured => _grid != null && _trialMap != null && _routing != null;

    /// <summary>
    /// Configures the spatial engine with grid, trial map, and routing.
    /// Must be called before querying position.
    /// </summary>
    public void Configure(PlotGrid grid, TrialMap trialMap, HardwareRouting routing)
    {
        _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        _trialMap = trialMap ?? throw new ArgumentNullException(nameof(trialMap));
        _routing = routing ?? throw new ArgumentNullException(nameof(routing));

        // T2 FIX: Validate that every grid cell has a TrialMap assignment.
        // Exception: if PlotAssignments is empty (e.g. ResumeSession without re-loading CSV),
        // skip silently — operator must load trial map before activating sections.
        if (trialMap.PlotAssignments.Count > 0)
        {
            var missingPlots = new List<string>();
            for (int r = 0; r < grid.Rows; r++)
            {
                for (int c = 0; c < grid.Columns; c++)
                {
                    if (trialMap.GetProduct(r, c) == null)
                        missingPlots.Add($"R{r + 1}C{c + 1}");
                }
            }
            if (missingPlots.Count > 0)
                throw new ArgumentException(
                    $"TrialMap is missing assignments for {missingPlots.Count} plots: " +
                    $"{string.Join(", ", missingPlots.Take(5))}" +
                    (missingPlots.Count > 5 ? $"... and {missingPlots.Count - 5} more" : ""));
        }

        // Cache grid bounds
        Plot first = _grid.Plots[0, 0];
        Plot last = _grid.Plots[_grid.Rows - 1, _grid.Columns - 1];
        _gridSouthWest = first.SouthWest;
        _gridNorthEast = last.NorthEast;
    }

    /// <summary>
    /// Core evaluation method. Projects look-ahead points along the heading vector,
    /// determines boom state, and computes the final valve mask.
    /// </summary>
    /// <param name="boomCenter">Current GPS position of the boom center.</param>
    /// <param name="headingDegrees">Current heading in degrees (0=North, 90=East).</param>
    /// <param name="speedKmh">Current speed in km/h — used to compute dynamic look-ahead distances.</param>
    /// <returns>SpatialResult with state, plot, and valve mask.</returns>
    public SpatialResult EvaluatePosition(GeoPoint boomCenter, double headingDegrees, double speedKmh)
    {
        if (!IsConfigured)
        {
            return new SpatialResult
            {
                State = BoomState.OutsideGrid,
                ValveMask = 0,
                DistanceToBoundaryMeters = double.MaxValue,
            };
        }

        double headingRad = headingDegrees * Math.PI / 180.0;

        // User #2: GPS deadband — freeze heading at low speed
        if (FreezeHeadingBelowSpeedKmh > 0 && speedKmh < FreezeHeadingBelowSpeedKmh)
        {
            headingRad = _frozenHeadingDegrees * Math.PI / 180.0;
        }
        else
        {
            _frozenHeadingDegrees = headingDegrees;
        }

        // Dynamic distances: user's desired meters + speed-dependent valve delay compensation
        double activationDist = GetEffectiveActivationDistance(speedKmh);
        double deactivationDist = GetEffectiveDeactivationDistance(speedKmh);

        // 1. Check if boom center is inside a plot
        Plot? currentPlot = FindPlot(boomCenter);

        if (currentPlot != null)
        {
            // Boom is inside a plot — check if we're about to leave (pre-deactivation)
            // Distance to the exit boundary along the heading
            double distToExit = DistanceToExitBoundary(boomCenter, currentPlot, headingRad);

            if (distToExit <= deactivationDist)
            {
                // D2 FIX: Within dynamic pre-deactivation zone — shut off sections for THIS
                // plot's product only. ValveMask=0 because EvaluatePosition returns a single-plot
                // result; the per-boom path (EvaluatePerBoom) handles multi-plot overlap.
                return new SpatialResult
                {
                    State = BoomState.LeavingPlot,
                    ActivePlot = currentPlot,
                    ActiveProduct = GetProduct(currentPlot),
                    ValveMask = 0, // Sections for this product shut off (dry cutoff)
                    DistanceToBoundaryMeters = distToExit,
                    ActivationDistanceMeters = activationDist,
                    DeactivationDistanceMeters = deactivationDist,
                };
            }

            // Fully inside the plot — valves active
            return new SpatialResult
            {
                State = BoomState.InPlot,
                ActivePlot = currentPlot,
                ActiveProduct = GetProduct(currentPlot),
                ValveMask = ComputeValveMask(currentPlot),
                DistanceToBoundaryMeters = distToExit,
                ActivationDistanceMeters = activationDist,
                DeactivationDistanceMeters = deactivationDist,
            };
        }

        // 2. Boom is NOT inside a plot — check if we're approaching one (pre-activation)
        GeoPoint forwardPoint = ProjectPoint(boomCenter, headingRad, activationDist);
        Plot? aheadPlot = FindPlot(forwardPoint);

        if (aheadPlot != null)
        {
            // Approaching a plot — pre-activate for pressure ramp-up
            double distToEntry = DistanceToEntryBoundary(boomCenter, aheadPlot, headingRad);

            return new SpatialResult
            {
                State = BoomState.ApproachingPlot,
                ActivePlot = aheadPlot,
                ActiveProduct = GetProduct(aheadPlot),
                ValveMask = ComputeValveMask(aheadPlot),
                DistanceToBoundaryMeters = distToEntry,
                ActivationDistanceMeters = activationDist,
                DeactivationDistanceMeters = deactivationDist,
            };
        }

        // 3. Check if we're at least inside the grid area
        if (IsInGridArea(boomCenter))
        {
            return new SpatialResult
            {
                State = BoomState.InAlley,
                ValveMask = 0,
                DistanceToBoundaryMeters = DistanceToNearestPlot(boomCenter),
                ActivationDistanceMeters = activationDist,
                DeactivationDistanceMeters = deactivationDist,
            };
        }

        // 4. Completely outside the grid
        return new SpatialResult
        {
            State = BoomState.OutsideGrid,
            ValveMask = 0,
            DistanceToBoundaryMeters = double.MaxValue,
            ActivationDistanceMeters = activationDist,
            DeactivationDistanceMeters = deactivationDist,
        };
    }

    /// <summary>
    /// Per-boom spatial evaluation with COG support.
    /// Each boom is evaluated at its own GPS position, with individual delays.
    /// Rear booms (Y < 0) use COG instead of heading when crab-walking.
    /// </summary>
    /// <param name="antennaPosition">GPS antenna position.</param>
    /// <param name="headingDegrees">Current heading (0=North, 90=East).</param>
    /// <param name="cogDegrees">Course Over Ground (actual movement direction).</param>
    /// <param name="speedKmh">Current speed.</param>
    /// <param name="hardwareSetup">Boom definitions with Y-offsets.</param>
    /// <param name="boomDelayProvider">Optional: returns per-boom (activationMs, deactivationMs). Null = use global.</param>
    /// <returns>Composite SpatialResult with per-boom mask.</returns>
    public SpatialResult EvaluatePerBoom(
        GeoPoint antennaPosition, double headingDegrees, double cogDegrees,
        double speedKmh, HardwareSetup hardwareSetup,
        Func<int, (double actMs, double deactMs)>? boomDelayProvider = null)
    {
        if (!IsConfigured || hardwareSetup == null)
        {
            return new SpatialResult
            {
                State = BoomState.OutsideGrid,
                ValveMask = 0,
                DistanceToBoundaryMeters = double.MaxValue,
            };
        }

        double headingRad = headingDegrees * Math.PI / 180.0;
        double cogRad = cogDegrees * Math.PI / 180.0;
        double headingCogDelta = Math.Abs(NormalizeAngle(headingDegrees - cogDegrees));
        bool useCogForRear = headingCogDelta > CogHeadingThresholdDegrees;
        ushort compositeMask = 0;
        BoomState worstState = BoomState.OutsideGrid;
        Plot? firstActivePlot = null;
        string? firstProduct = null;
        double minDistance = double.MaxValue;

        foreach (Boom boom in hardwareSetup.Booms)
        {
            if (!boom.Enabled) continue;

            // Choose projection direction: rear booms use COG on slopes
            bool isRearBoom = boom.YOffsetMeters < 0;
            double projectionRad = (useCogForRear && isRearBoom) ? cogRad : headingRad;

            // Project boom position: antenna + projection * YOffset
            GeoPoint boomPos = ProjectPoint(antennaPosition, projectionRad, boom.YOffsetMeters);

            // Evaluate this boom's position with look-ahead/cut-off
            SpatialResult boomResult = EvaluatePosition(boomPos, headingDegrees, speedKmh);

            if (boomResult.ActivePlot != null)
            {
                string? product = GetProduct(boomResult.ActivePlot);
                if (product != null)
                {
                    IReadOnlyList<int> sections = _routing!.GetSections(product);
                    if (sections.Contains(boom.ValveChannel))
                    {
                        // Calculate overlap % using the same projection direction as boom position
                        double overlapPct = boom.CalculateOverlap(
                            boomPos, boomResult.ActivePlot, projectionRad);

                        // Hysteresis: use different thresholds for activation vs deactivation
                        bool wasActive = (_prevBoomMask & (1 << boom.ValveChannel)) != 0;
                        bool shouldActivate = wasActive
                            ? overlapPct >= boom.DeactivationOverlapPercent  // Stay ON above deactivation threshold
                            : overlapPct >= boom.ActivationOverlapPercent;   // Turn ON above activation threshold

                        if (shouldActivate && boomResult.ValveMask != 0)
                        {
                            compositeMask |= (ushort)(1 << boom.ValveChannel);
                        }
                    }
                }
            }

            // Track the "most active" state for reporting
            if (boomResult.State > worstState)
            {
                worstState = boomResult.State;
            }

            if (boomResult.ActivePlot != null && firstActivePlot == null)
            {
                firstActivePlot = boomResult.ActivePlot;
                firstProduct = boomResult.ActiveProduct;
            }

            if (boomResult.DistanceToBoundaryMeters < minDistance)
            {
                minDistance = boomResult.DistanceToBoundaryMeters;
            }
        }

        // Store mask for hysteresis on next cycle
        _prevBoomMask = compositeMask;

        return new SpatialResult
        {
            State = worstState,
            ActivePlot = firstActivePlot,
            ActiveProduct = firstProduct,
            ValveMask = compositeMask,
            DistanceToBoundaryMeters = minDistance,
        };
    }

    /// <summary>
    /// Finds the plot that contains the given GPS position.
    /// Returns null if the position is in a buffer zone or outside the grid.
    /// </summary>
    public Plot? FindPlot(GeoPoint position)
    {
        if (_grid == null) return null;

        // O(1) fast path for axis-aligned (heading=0) grids
        if (_grid.HeadingDegrees == 0 && _grid.Rows > 0 && _grid.Columns > 0)
        {
            Plot origin = _grid.Plots[0, 0];
            double rowStep = _grid.PlotLengthMeters + _grid.BufferLengthMeters;
            double colStep = _grid.PlotWidthMeters + _grid.BufferWidthMeters;

            // Guard: avoid division by zero if grid config is degenerate
            if (rowStep > 0 && colStep > 0)
            {
                double dLatMeters = (position.Latitude - origin.SouthWest.Latitude) * 110540.0;
                double cosLat = Math.Cos(origin.SouthWest.Latitude * Math.PI / 180.0);
                double dLonMeters = (position.Longitude - origin.SouthWest.Longitude) * 111320.0 * cosLat;

                int candidateRow = (int)(dLatMeters / rowStep);
                int candidateCol = (int)(dLonMeters / colStep);

                if (candidateRow >= 0 && candidateRow < _grid.Rows &&
                    candidateCol >= 0 && candidateCol < _grid.Columns &&
                    _grid.Plots[candidateRow, candidateCol].Contains(position))
                {
                    return _grid.Plots[candidateRow, candidateCol];
                }
                // L1 FIX: Fall through to linear scan instead of returning null.
                // O(1) candidate may miss due to floating-point precision at boundaries.
            }
        }

        // Fallback: linear scan for rotated grids
        for (int row = 0; row < _grid.Rows; row++)
        {
            for (int col = 0; col < _grid.Columns; col++)
            {
                if (_grid.Plots[row, col].Contains(position))
                {
                    return _grid.Plots[row, col];
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Computes the 14-bit valve mask for a specific plot.
    /// </summary>
    public ushort ComputeValveMask(Plot plot)
    {
        if (_trialMap == null || _routing == null)
            return 0;

        string? product = _trialMap.GetProduct(plot.Row, plot.Column);
        if (product == null)
            return 0;

        IReadOnlyList<int> sections = _routing.GetSections(product);
        ushort mask = 0;
        foreach (int section in sections)
        {
            if (section >= 0 && section < HardwareRouting.TotalSections)
            {
                mask |= (ushort)(1 << section);
            }
        }

        return mask;
    }

    /// <summary>
    /// Legacy method — computes valve mask from raw GPS position.
    /// </summary>
    public ushort ComputeValveMask(GeoPoint position)
    {
        Plot? plot = FindPlot(position);
        return plot != null ? ComputeValveMask(plot) : (ushort)0;
    }

    /// <summary>
    /// Checks if the current position is within the grid bounds (including buffers).
    /// WARNING: Uses axis-aligned bounding box — only accurate for HeadingDegrees == 0 grids.
    /// For rotated grids, positions near corners may be falsely classified.
    /// </summary>
    public bool IsInGridArea(GeoPoint position)
    {
        if (_grid == null) return false;

        return position.Latitude >= _gridSouthWest.Latitude &&
               position.Latitude <= _gridNorthEast.Latitude &&
               position.Longitude >= _gridSouthWest.Longitude &&
               position.Longitude <= _gridNorthEast.Longitude;
    }

    // ════════════════════════════════════════════════════════════════════
    // Private geometry helpers
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Projects a point forward along the heading by a given distance in meters.
    /// </summary>
    private static GeoPoint ProjectPoint(GeoPoint origin, double headingRad, double distanceMeters)
    {
        // heading: 0 = North (+lat), 90 = East (+lon)
        double dNorth = distanceMeters * Math.Cos(headingRad);  // meters north
        double dEast = distanceMeters * Math.Sin(headingRad);   // meters east

        double latOffset = dNorth / 110540.0;
        double lonOffset = dEast / (111320.0 * Math.Cos(origin.Latitude * Math.PI / 180.0));

        return new GeoPoint(origin.Latitude + latOffset, origin.Longitude + lonOffset);
    }

    private string? GetProduct(Plot plot)
    {
        return _trialMap?.GetProduct(plot.Row, plot.Column);
    }

    /// <summary>
    /// Calculates the distance from a point to the exit boundary of a plot
    /// along the heading direction. Returns meters to exit.
    /// </summary>
    private static double DistanceToExitBoundary(GeoPoint position, Plot plot, double headingRad)
    {
        // Project from current position to each boundary and find the nearest exit
        double cosH = Math.Cos(headingRad);
        double sinH = Math.Sin(headingRad);

        // Convert plot boundaries to meter offsets from position
        double dNorth_toNorth = (plot.NorthEast.Latitude - position.Latitude) * 110540.0;
        double dNorth_toSouth = (position.Latitude - plot.SouthWest.Latitude) * 110540.0;
        double cosLat = Math.Cos(position.Latitude * Math.PI / 180.0);
        double dEast_toEast = (plot.NorthEast.Longitude - position.Longitude) * 111320.0 * cosLat;
        double dEast_toWest = (position.Longitude - plot.SouthWest.Longitude) * 111320.0 * cosLat;

        double minDist = double.MaxValue;

        // Distance to north boundary along heading (if heading has northward component)
        if (cosH > 0.01)
        {
            double d = dNorth_toNorth / cosH;
            if (d > 0 && d < minDist) minDist = d;
        }

        // Distance to south boundary along heading (if heading has southward component)
        if (cosH < -0.01)
        {
            double d = dNorth_toSouth / (-cosH);
            if (d > 0 && d < minDist) minDist = d;
        }

        // Distance to east boundary along heading (if heading has eastward component)
        if (sinH > 0.01)
        {
            double d = dEast_toEast / sinH;
            if (d > 0 && d < minDist) minDist = d;
        }

        // Distance to west boundary along heading (if heading has westward component)
        if (sinH < -0.01)
        {
            double d = dEast_toWest / (-sinH);
            if (d > 0 && d < minDist) minDist = d;
        }

        return minDist;
    }

    /// <summary>
    /// Calculates the distance from a point to the entry boundary of a target plot
    /// along the heading direction.
    /// </summary>
    private static double DistanceToEntryBoundary(GeoPoint position, Plot plot, double headingRad)
    {
        double cosH = Math.Cos(headingRad);
        double sinH = Math.Sin(headingRad);

        double cosLat = Math.Cos(position.Latitude * Math.PI / 180.0);
        double minDist = double.MaxValue;

        // Distance to south boundary (entry from south when heading north)
        if (cosH > 0.01)
        {
            double d = (plot.SouthWest.Latitude - position.Latitude) * 110540.0 / cosH;
            if (d > 0 && d < minDist) minDist = d;
        }

        // Distance to north boundary (entry from north when heading south)
        if (cosH < -0.01)
        {
            double d = (position.Latitude - plot.NorthEast.Latitude) * 110540.0 / (-cosH);
            if (d > 0 && d < minDist) minDist = d;
        }

        // Distance to west boundary (entry from west when heading east)
        if (sinH > 0.01)
        {
            double d = (plot.SouthWest.Longitude - position.Longitude) * 111320.0 * cosLat / sinH;
            if (d > 0 && d < minDist) minDist = d;
        }

        // Distance to east boundary (entry from east when heading west)
        if (sinH < -0.01)
        {
            double d = (position.Longitude - plot.NorthEast.Longitude) * 111320.0 * cosLat / (-sinH);
            if (d > 0 && d < minDist) minDist = d;
        }

        return minDist;
    }

    /// <summary>
    /// Calculates the distance to the nearest plot from a position (simple approximation).
    /// </summary>
    private double DistanceToNearestPlot(GeoPoint position)
    {
        if (_grid == null) return double.MaxValue;

        double minDist = double.MaxValue;
        for (int row = 0; row < _grid.Rows; row++)
        {
            for (int col = 0; col < _grid.Columns; col++)
            {
                Plot plot = _grid.Plots[row, col];
                // Clamp position to plot bounds and measure distance
                double clampedLat = Math.Clamp(position.Latitude, plot.SouthWest.Latitude, plot.NorthEast.Latitude);
                double clampedLon = Math.Clamp(position.Longitude, plot.SouthWest.Longitude, plot.NorthEast.Longitude);
                var clamped = new GeoPoint(clampedLat, clampedLon);
                double dist = position.DistanceTo(clamped);
                if (dist < minDist) minDist = dist;
            }
        }

        return minDist;
    }

    /// <summary>
    /// Normalizes an angle difference to the range -180..+180.
    /// </summary>
    private static double NormalizeAngle(double angleDegrees)
    {
        double a = angleDegrees % 360.0;
        if (a > 180.0) a -= 360.0;
        if (a < -180.0) a += 360.0;
        return a;
    }

    /// <summary>
    /// Backward-compatible overload: heading = COG (no crab-walk correction).
    /// </summary>
    public SpatialResult EvaluatePerBoom(
        GeoPoint antennaPosition, double headingDegrees, double speedKmh,
        HardwareSetup hardwareSetup)
    {
        return EvaluatePerBoom(antennaPosition, headingDegrees, headingDegrees,
            speedKmh, hardwareSetup);
    }
}
