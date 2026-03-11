using System;
using System.Collections.Generic;
using System.Linq;
using PlotManager.Core.Models;

namespace PlotManager.Core.Services
{
    public enum ExperimentalDesignType
    {
        RCBD,       // Randomized Complete Block Design (Blocks = Rows/Reps)
        CRD,        // Completely Randomized Design
        LatinSquare // Latin Square Design
    }

    /// <summary>
    /// Generates experimental design layouts (CRD, RCBD, Latin Square) for a trial grid.
    /// Produces a <see cref="TrialMap"/> with PlotAssignments mapping PlotId → treatment name.
    /// </summary>
    public class ExperimentDesigner
    {
        private readonly Random _random;

        public ExperimentDesigner(int? seed = null)
        {
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        /// <summary>
        /// Generates a <see cref="TrialMap"/> using the specified experimental design.
        /// </summary>
        /// <param name="grid">The plot grid to assign treatments to.</param>
        /// <param name="treatments">List of treatment names to assign.</param>
        /// <param name="type">Experimental design type.</param>
        /// <returns>A TrialMap with randomized treatment assignments.</returns>
        public TrialMap GenerateDesign(PlotGrid grid, List<string> treatments, ExperimentalDesignType type)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (treatments == null || !treatments.Any()) throw new ArgumentException("Treatments cannot be empty.", nameof(treatments));

            Dictionary<string, string> assignments = type switch
            {
                ExperimentalDesignType.CRD        => GenerateCRD(grid, treatments),
                ExperimentalDesignType.RCBD       => GenerateRCBD(grid, treatments),
                ExperimentalDesignType.LatinSquare => GenerateLatinSquare(grid, treatments),
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
            };

            return new TrialMap
            {
                TrialName = $"Generated {type}",
                PlotAssignments = assignments
            };
        }

        /// <summary>
        /// Completely Randomized Design: treatments are randomly shuffled across all plots.
        /// </summary>
        private Dictionary<string, string> GenerateCRD(PlotGrid grid, List<string> treatments)
        {
            int totalPlots = grid.Rows * grid.Columns;

            // Fill pool with treatments cyclically, then shuffle
            var pool = new List<string>(totalPlots);
            for (int i = 0; i < totalPlots; i++)
                pool.Add(treatments[i % treatments.Count]);
            Shuffle(pool);

            var result = new Dictionary<string, string>(totalPlots);
            int index = 0;
            for (int r = 0; r < grid.Rows; r++)
                for (int c = 0; c < grid.Columns; c++)
                    result[grid.Plots[r, c].PlotId] = pool[index++];

            return result;
        }

        /// <summary>
        /// Randomized Complete Block Design: each row is a block with one full set of treatments.
        /// </summary>
        private Dictionary<string, string> GenerateRCBD(PlotGrid grid, List<string> treatments)
        {
            var result = new Dictionary<string, string>(grid.Rows * grid.Columns);

            for (int r = 0; r < grid.Rows; r++)
            {
                // Build one block (row) worth of treatments, cycling if columns > treatments
                var blockTreatments = new List<string>(grid.Columns);
                for (int i = 0; i < grid.Columns; i++)
                    blockTreatments.Add(treatments[i % treatments.Count]);
                Shuffle(blockTreatments);

                for (int c = 0; c < grid.Columns; c++)
                    result[grid.Plots[r, c].PlotId] = blockTreatments[c];
            }

            return result;
        }

        /// <summary>
        /// Latin Square Design: each treatment appears exactly once per row and per column.
        /// Repeats cyclically if grid is larger than the number of treatments.
        /// </summary>
        private Dictionary<string, string> GenerateLatinSquare(PlotGrid grid, List<string> treatments)
        {
            int n = treatments.Count;

            // Build base n×n latin square using row shifts
            var baseSquare = new int[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    baseSquare[i, j] = (i + j) % n;

            // Randomize row and column ordering
            var rowPerm = Enumerable.Range(0, n).ToList();
            var colPerm = Enumerable.Range(0, n).ToList();
            Shuffle(rowPerm);
            Shuffle(colPerm);

            var result = new Dictionary<string, string>(grid.Rows * grid.Columns);
            for (int r = 0; r < grid.Rows; r++)
            {
                for (int c = 0; c < grid.Columns; c++)
                {
                    int mappedR = rowPerm[r % n];
                    int mappedC = colPerm[c % n];
                    int treatmentIdx = baseSquare[mappedR, mappedC];
                    result[grid.Plots[r, c].PlotId] = treatments[treatmentIdx];
                }
            }

            return result;
        }

        /// <summary>Fisher-Yates in-place shuffle.</summary>
        private void Shuffle<T>(List<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = _random.Next(n + 1);
                (list[k], list[n]) = (list[n], list[k]);
            }
        }
    }
}
