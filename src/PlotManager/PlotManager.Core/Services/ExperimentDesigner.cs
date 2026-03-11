using System;
using System.Collections.Generic;
using System.Linq;
using PlotManager.Core.Models;

namespace PlotManager.Core.Services
{
    public enum ExperimentalDesignType
    {
        RCBD, // Randomized Complete Block Design (Blocks = Rows/Reps)
        CRD,  // Completely Randomized Design
        LatinSquare // Latin Square Design
    }

    public class ExperimentDesigner
    {
        private readonly Random _random;

        public ExperimentDesigner(int? seed = null)
        {
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        public TrialMap GenerateDesign(PlotGrid grid, List<string> treatments, ExperimentalDesignType type)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (treatments == null || !treatments.Any()) throw new ArgumentException("Treatments cannot be empty.", nameof(treatments));

            var map = new TrialMap { Grid = grid };
            var assignments = new List<PlotAssignment>();

            switch (type)
            {
                case ExperimentalDesignType.CRD:
                    assignments = GenerateCRD(grid, treatments);
                    break;
                case ExperimentalDesignType.RCBD:
                    assignments = GenerateRCBD(grid, treatments);
                    break;
                case ExperimentalDesignType.LatinSquare:
                    assignments = GenerateLatinSquare(grid, treatments);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }

            // The TrialMap model currently has an init-only PlotAssignments, 
            // so we might need a workaround or just set it via reflection if it's strictly init.
            // But since C# 9, we can use an object initializer to create a new instance with it.
            return new TrialMap 
            { 
                Grid = grid,
                PlotAssignments = assignments 
            };
        }

        private List<PlotAssignment> GenerateCRD(PlotGrid grid, List<string> treatments)
        {
            var assignments = new List<PlotAssignment>();
            int totalPlots = grid.Rows * grid.Columns;
            
            // Create a pool of treatments, repeating them to fill the grid
            var pool = new List<string>(totalPlots);
            for (int i = 0; i < totalPlots; i++)
            {
                pool.Add(treatments[i % treatments.Count]);
            }

            // Shuffle the pool
            Shuffle(pool);

            // Assign to grid
            int index = 0;
            foreach (var plot in grid.Plots)
            {
                var assignedTreatment = pool[index++];
                assignments.Add(new PlotAssignment
                {
                    Plot = plot,
                    Treatment = assignedTreatment,
                    BufferType = DetermineBufferType(assignedTreatment)
                });
            }

            return assignments;
        }

        private List<PlotAssignment> GenerateRCBD(PlotGrid grid, List<string> treatments)
        {
            var assignments = new List<PlotAssignment>();
            
            // In typical RCBD for field trials, a Block is often a row (or a set of adjacent plots).
            // We'll treat each Row as a Block. Wait, what if columns < treatments?
            // If columns < treatments, RCBD across a single row is impossible without splitting blocks across rows.
            // For simplicity, we assume Columns >= Treatments, or we just fill row by row.
            
            // Let's implement standard Block = Row logic if Columns >= Treatments.
            // If not, we fall back to repeating treatments per block size.
            int plotsPerBlock = grid.Columns;
            
            for (int r = 1; r <= grid.Rows; r++)
            {
                var blockPlots = grid.Plots.Where(p => p.Row == r).OrderBy(p => p.Column).ToList();
                var blockTreatments = new List<string>(plotsPerBlock);
                
                for (int i = 0; i < plotsPerBlock; i++)
                {
                    blockTreatments.Add(treatments[i % treatments.Count]);
                }
                
                Shuffle(blockTreatments);
                
                for (int c = 0; c < blockPlots.Count; c++)
                {
                    var treatment = blockTreatments[c];
                    assignments.Add(new PlotAssignment
                    {
                        Plot = blockPlots[c],
                        Treatment = treatment,
                        BufferType = DetermineBufferType(treatment)
                    });
                }
            }

            return assignments;
        }

        private List<PlotAssignment> GenerateLatinSquare(PlotGrid grid, List<string> treatments)
        {
            var assignments = new List<PlotAssignment>();
            // True Latin Square requires Rows == Columns == Treatments.Count.
            // If not, we just fallback to a randomized shift approach (pseudo-latin square)
            int n = treatments.Count;
            
            // Generate a standard latin square
            var baseSquare = new int[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    baseSquare[i, j] = (i + j) % n;
                    
            // Shuffle rows and columns to randomize
            var rowIndices = Enumerable.Range(0, n).ToList();
            var colIndices = Enumerable.Range(0, n).ToList();
            Shuffle(rowIndices);
            Shuffle(colIndices);
            
            // Assign to grid
            foreach (var plot in grid.Plots)
            {
                // Map plot row/col to our latin square (repeating if grid is larger than N)
                int rNode = (plot.Row - 1) % n;
                int cNode = (plot.Column - 1) % n;
                
                int mappedR = rowIndices[rNode];
                int mappedC = colIndices[cNode];
                
                int treatmentIdx = baseSquare[mappedR, mappedC];
                string treatment = treatments[treatmentIdx];
                
                assignments.Add(new PlotAssignment
                {
                    Plot = plot,
                    Treatment = treatment,
                    BufferType = DetermineBufferType(treatment)
                });
            }
            
            return assignments;
        }

        private string DetermineBufferType(string treatmentName)
        {
            // Simple heuristic mapping based on common names
            if (treatmentName.IndexOf("контроль", StringComparison.OrdinalIgnoreCase) >= 0 ||
                treatmentName.IndexOf("control", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Контроль";
                
            if (treatmentName.IndexOf("гербіцид", StringComparison.OrdinalIgnoreCase) >= 0 ||
                treatmentName.IndexOf("herbicide", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Вода"; // Needs flushing
                
            return "Основний розчин";
        }

        private void Shuffle<T>(List<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = _random.Next(n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }
    }
}
