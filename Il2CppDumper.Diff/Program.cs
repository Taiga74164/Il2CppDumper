#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Il2CppDumper.Diff
{
    class Program
    {
        static int Main(string[] args)
        {
            var isDriftMode = false;
            var positionalArgs = new List<string>();

            foreach (var arg in args)
            {
                if (string.Equals(arg, "--drift", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "-d", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "drift", StringComparison.OrdinalIgnoreCase))
                {
                    isDriftMode = true;
                }
                else
                {
                    positionalArgs.Add(arg);
                }
            }

            if (positionalArgs.Count != 3)
            {
                ShowHelp();
                Environment.ExitCode = 2;
                return 2;
            }

            var left = positionalArgs[0];
            var right = positionalArgs[1];
            var output = positionalArgs[2];

            int exitCode;
            if (isDriftMode)
            {
                exitCode = FieldOffsetDiff.Execute(left, right, output, Console.Out);
            }
            else
            {
                exitCode = DumpDiffer.Execute(left, right, output, Console.Out);
            }

            Environment.ExitCode = exitCode;
            return exitCode;
        }

        static void ShowHelp()
        {
            Console.WriteLine("usage: Il2CppDumper.Diff <left> <right> <output>");
            Console.WriteLine("       Il2CppDumper.Diff --drift <left> <right> <output>");
            Console.WriteLine();
            Console.WriteLine("Modes:");
            Console.WriteLine("  default   Full dump diff (types, methods, strings, addresses -> dump-diff.json)");
            Console.WriteLine("  --drift   Native struct field-offset drift report (FieldOffsetDiff -> field-offset-drift.json)");
            Console.WriteLine();
            Console.WriteLine("Arguments:");
            Console.WriteLine("  <left>    Directory or dump.json file of the baseline build.");
            Console.WriteLine("  <right>   Directory or dump.json file of the updated build.");
            Console.WriteLine("  <output>  Destination path or directory for output JSON.");
            Console.WriteLine();
            Console.WriteLine("Exit codes:");
            Console.WriteLine("  0 = identical / no drift");
            Console.WriteLine("  1 = differences / drift found");
            Console.WriteLine("  2 = usage/input error");
        }
    }
}
