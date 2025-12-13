namespace LaserConvert
{
    internal static class Program
    {
        static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: LaserConvert <input.igs|stp> <output.svg>");
                return 1;
            }

            var inputPath = args[0];
            var outputPath = args[1];

            if(inputPath.EndsWith(".igs", StringComparison.OrdinalIgnoreCase) ||
               inputPath.EndsWith(".iges", StringComparison.OrdinalIgnoreCase))
            {
                return IgesProcess.Main(inputPath, outputPath);
            }

            if (inputPath.EndsWith(".stp", StringComparison.OrdinalIgnoreCase) ||
               inputPath.EndsWith(".step", StringComparison.OrdinalIgnoreCase))
            {
                return HelixProcess.Main(inputPath, outputPath);
            }

            return 0;
        }
    }
}