using System;
using System.Collections.Generic;
using System.Text;

namespace LaserConvertProcess
{
    public record ProcessingOptions
    {
        /// <summary>
        /// Target thickness of material in mm. Solids with this thickness will be processed.
        /// Default: 3.0mm
        /// </summary>
        public double Thickness { get; init; } = 3.0;

        /// <summary>
        /// Tolerance for thickness matching in mm. 
        /// A solid with thickness within (Thickness - Tolerance) to (Thickness + Tolerance) will be considered a match.
        /// Default: 0.5mm
        /// </summary>
        public double ThicknessTolerance { get; init; } = 0.5;

        /// <summary>
        /// Enable debug mode for verbose output.
        /// Default: false
        /// </summary>
        public bool DebugMode { get; init; } = false;

        /// <summary>
        /// Computed minimum thickness based on Thickness and ThicknessTolerance.
        /// </summary>
        public double MinThickness => Thickness - ThicknessTolerance;

        /// <summary>
        /// Computed maximum thickness based on Thickness and ThicknessTolerance.
        /// </summary>
        public double MaxThickness => Thickness + ThicknessTolerance;
    }
}
