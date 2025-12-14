namespace LaserConvert
{
    internal static class Program
    {
        static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: LaserConvert <input.igs|stp> <output.svg> [debugMode=true|false]");
                return 1;
            }

            var inputPath = args[0];
            var outputPath = args[1];
            
            // Parse optional debugMode argument (defaults to false)
            bool debugMode = false;
            if (args.Length >= 3)
            {
                var debugArg = args[2];
                if (debugArg.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                    debugArg.Equals("debugMode=true", StringComparison.OrdinalIgnoreCase))
                {
                    debugMode = true;
                }
            }

            if(inputPath.EndsWith(".igs", StringComparison.OrdinalIgnoreCase) ||
               inputPath.EndsWith(".iges", StringComparison.OrdinalIgnoreCase))
            {
                return IgesProcess.Main(inputPath, outputPath, debugMode);
            }

            if (inputPath.EndsWith(".stp", StringComparison.OrdinalIgnoreCase) ||
               inputPath.EndsWith(".step", StringComparison.OrdinalIgnoreCase))
            {
                return StepProcess.Main(inputPath, outputPath, debugMode);
            }

            return 0;
        }
    }
}