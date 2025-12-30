using LaserConvertProcess;
using System.Runtime.Intrinsics.Arm;

namespace LaserConvert
{
    /// <summary>
    /// Configuration options for laser conversion processing.
    /// All measurements are in millimeters.
    /// </summary>
    
    
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
                StepReturn results = StepProcess.Main(inputPath, options);
                File.WriteAllText(outputPath, results.SVGContents);
                Console.WriteLine($"Wrote SVG: {outputPath}");
                return results.ReturnCode;
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