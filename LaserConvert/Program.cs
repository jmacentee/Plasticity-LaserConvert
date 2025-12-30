namespace LaserConvert
{
    /// <summary>
    /// Configuration options for laser conversion processing.
    /// All measurements are in millimeters.
    /// </summary>
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
    
    internal static class Program
    {
        static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                PrintUsage();
                return 1;
            }

            var inputPath = args[0];
            var outputPath = args[1];
            
            // Parse optional arguments
            var options = ParseOptions(args);
            
            if (inputPath.EndsWith(".stp", StringComparison.OrdinalIgnoreCase) ||
               inputPath.EndsWith(".step", StringComparison.OrdinalIgnoreCase))
            {
                return StepProcess.Main(inputPath, outputPath, options);
            }

            return 0;
        }
        
        private static void PrintUsage()
        {
            Console.WriteLine("Usage: LaserConvert <input.stp> <output.svg> [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  debugMode=true|false       Enable verbose debug output (default: false)");
            Console.WriteLine("  thickness=<mm>             Target material thickness in mm (default: 3.0)");
            Console.WriteLine("  tolerance=<mm>             Thickness matching tolerance in mm (default: 0.5)");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  LaserConvert input.stp output.svg");
            Console.WriteLine("  LaserConvert input.stp output.svg thickness=6 debugMode=true");
            Console.WriteLine("  LaserConvert input.stp output.svg thickness=3 tolerance=0.25");
        }
        
        private static ProcessingOptions ParseOptions(string[] args)
        {
            bool debugMode = false;
            double thickness = 3.0;
            double tolerance = 0.5;
            
            // Parse arguments starting from index 2 (skip input and output paths)
            for (int i = 2; i < args.Length; i++)
            {
                var arg = args[i];
                
                // Handle debugMode
                if (arg.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("debugMode=true", StringComparison.OrdinalIgnoreCase))
                {
                    debugMode = true;
                }
                else if (arg.Equals("debugMode=false", StringComparison.OrdinalIgnoreCase))
                {
                    debugMode = false;
                }
                // Handle thickness=<value>
                else if (arg.StartsWith("thickness=", StringComparison.OrdinalIgnoreCase))
                {
                    var value = arg.Substring("thickness=".Length);
                    if (double.TryParse(value, System.Globalization.NumberStyles.Float, 
                        System.Globalization.CultureInfo.InvariantCulture, out var t))
                    {
                        thickness = t;
                    }
                }
                // Handle tolerance=<value>
                else if (arg.StartsWith("tolerance=", StringComparison.OrdinalIgnoreCase))
                {
                    var value = arg.Substring("tolerance=".Length);
                    if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var tol))
                    {
                        tolerance = tol;
                    }
                }
            }
            
            return new ProcessingOptions
            {
                Thickness = thickness,
                ThicknessTolerance = tolerance,
                DebugMode = debugMode
            };
        }
    }
}