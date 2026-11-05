#nullable enable
using System;

namespace Il2CppDumper.Diff
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length != 3)
            {
                ShowHelp();
                Environment.ExitCode = 2;
                return 2;
            }

            var left = args[0];
            var right = args[1];
            var output = args[2];

            var exitCode = DumpDiffer.Execute(left, right, output, Console.Out);
            Environment.ExitCode = exitCode;
            return exitCode;
        }

        static void ShowHelp()
        {
            Console.WriteLine("usage: Il2CppDumper.Diff <left> <right> <output>");
            Console.WriteLine();
            Console.WriteLine("Arguments:");
            Console.WriteLine("  <left>    Directory or dump.json file of the baseline build.");
            Console.WriteLine("  <right>   Directory or dump.json file of the updated build.");
            Console.WriteLine("  <output>  Destination path or directory for dump-diff.json.");
            Console.WriteLine();
            Console.WriteLine("Exit codes:");
            Console.WriteLine("  0 = identical");
            Console.WriteLine("  1 = differences found");
            Console.WriteLine("  2 = usage/input error");
        }
    }
}
