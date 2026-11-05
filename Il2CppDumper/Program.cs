using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using Il2CppDumper.Core;

namespace Il2CppDumper
{
    class Program
    {
        private static Config config;

        [STAThread]
        static int Main(string[] args)
        {
            config = JsonSerializer.Deserialize<Config>(File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + @"config.json"));
            string il2cppPath = null;
            string metadataPath = null;
            string candidateMetadataPath = null;
            string outputDir = null;

            var argList = args.ToList();
            if (argList.Remove("--output-json"))
            {
                config.GenerateJsonDump = true;
                args = argList.ToArray();
            }

            if (args.Length == 1)
            {
                if (args[0] == "-h" || args[0] == "--help" || args[0] == "/?" || args[0] == "/h")
                {
                    ShowHelp();
                    return 0;
                }
            }
            if (args.Length > 3)
            {
                ShowHelp();
                return 1;
            }
            if (args.Length > 1)
            {
                string rawIl2cppArg = null;
                string rawMetadataArg = null;
                string rawCandidateMetadataArg = null;

                foreach (var arg in args)
                {
                    if (File.Exists(arg))
                    {
                        var fullArg = Path.GetFullPath(arg);
                        var file = File.ReadAllBytes(fullArg);
                        if (file.Length >= 4 && BitConverter.ToUInt32(file, 0) == 0xFAB11BAF)
                        {
                            metadataPath = fullArg;
                            rawMetadataArg = arg;
                        }
                        else if (il2cppPath == null)
                        {
                            il2cppPath = fullArg;
                            rawIl2cppArg = arg;
                        }
                        else if (candidateMetadataPath == null)
                        {
                            candidateMetadataPath = fullArg;
                            rawCandidateMetadataArg = arg;
                        }
                    }
                    else if (Directory.Exists(arg))
                    {
                        var fullPath = Path.GetFullPath(arg);
                        if (!fullPath.EndsWith(Path.DirectorySeparatorChar.ToString()) && !fullPath.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
                        {
                            fullPath += Path.DirectorySeparatorChar;
                        }
                        outputDir = fullPath;
                    }
                }
                var effectiveMetadataArg = metadataPath ?? candidateMetadataPath;
                var rawEffectiveMetadataArg = rawMetadataArg ?? rawCandidateMetadataArg;
                if (args.Length == 3 && outputDir == null && il2cppPath != null && effectiveMetadataArg != null)
                {
                    try
                    {
                        var outDirArg = args.First(x => x != rawIl2cppArg && x != rawEffectiveMetadataArg);
                        Directory.CreateDirectory(outDirArg);
                        var fullPath = Path.GetFullPath(outDirArg);
                        if (!fullPath.EndsWith(Path.DirectorySeparatorChar.ToString()) && !fullPath.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
                        {
                            fullPath += Path.DirectorySeparatorChar;
                        }
                        outputDir = fullPath;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"ERROR: Failed to create output directory: {e.Message}");
                        Environment.ExitCode = 1;
                        return 1;
                    }
                }
            }
            outputDir ??= AppDomain.CurrentDomain.BaseDirectory;
            outputDir = Path.GetFullPath(outputDir);
            if (!outputDir.EndsWith(Path.DirectorySeparatorChar.ToString()) && !outputDir.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
            {
                outputDir += Path.DirectorySeparatorChar;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (il2cppPath == null)
                {
                    var ofd = new OpenFileDialog
                    {
                        Filter = "Il2Cpp binary file|*.*"
                    };
                    if (ofd.ShowDialog())
                    {
                        il2cppPath = ofd.FileName;
                        ofd.Filter = "global-metadata|global-metadata.dat";
                        if (ofd.ShowDialog())
                        {
                            metadataPath = ofd.FileName;
                        }
                        else
                        {
                            return 0;
                        }
                    }
                    else
                    {
                        return 0;
                    }
                }
            }
            if (il2cppPath == null)
            {
                ShowHelp();
                return 1;
            }
            var exitCode = 0;
            if (metadataPath == null)
            {
                if (candidateMetadataPath != null)
                {
                    try
                    {
                        DumpMetadataWriter.WriteInitial(outputDir, il2cppPath, candidateMetadataPath, config);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
                Console.WriteLine($"ERROR: Metadata file not found or encrypted.");
                exitCode = 1;
            }
            else
            {
                int? fatIndex = null;
                try
                {
                    var file = File.ReadAllBytes(il2cppPath);
                    if (file.Length >= 4)
                    {
                        var magic = BitConverter.ToUInt32(file, 0);
                        if (magic == 0xCAFEBABE || magic == 0xBEBAFECA)
                        {
                            var machofat = new MachoFat(new MemoryStream(file));
                            if (!TrySelectMachoFatIndex(Console.In, Console.Out, machofat.fats, out var selected))
                            {
                                exitCode = 1;
                                Environment.ExitCode = 1;
                                return 1;
                            }
                            fatIndex = selected;
                        }
                    }
                }
                catch
                {
                    // Let library report init failure
                }

                var request = new DumpRequest
                {
                    ExecutablePath = il2cppPath,
                    MetadataPath = metadataPath,
                    OutputDirectory = outputDir,
                    Config = config,
                    Log = Console.Out,
                    MachoFatIndex = fatIndex
                };

                var result = Dumper.Dump(request);
                if (!result.Success)
                {
                    exitCode = 1;
                }
            }
            if (config.RequireAnyKey && !Console.IsInputRedirected)
            {
                Console.WriteLine("Press any key to exit...");
                try
                {
                    Console.ReadKey(true);
                }
                catch
                {
                    // ignored
                }
            }
            Environment.ExitCode = exitCode;
            return exitCode;
        }

        internal static bool TrySelectMachoFatIndex(TextReader reader, TextWriter writer, Fat[] fats, out int selectedIndex)
        {
            selectedIndex = -1;
            if (fats == null || fats.Length == 0)
            {
                writer.WriteLine("ERROR: No platforms available in FAT Mach-O binary.");
                return false;
            }

            while (true)
            {
                writer.Write("Select Platform: ");
                for (var i = 0; i < fats.Length; i++)
                {
                    var fat = fats[i];
                    writer.Write(fat.magic == 0xFEEDFACF ? $"{i + 1}.64bit " : $"{i + 1}.32bit ");
                }
                writer.WriteLine();

                var line = reader.ReadLine();
                if (line == null)
                {
                    writer.WriteLine("ERROR: Unexpected end of input while selecting platform.");
                    return false;
                }

                if (int.TryParse(line.Trim(), out var choice) && choice >= 1 && choice <= fats.Length)
                {
                    selectedIndex = choice - 1;
                    return true;
                }

                writer.WriteLine($"Invalid selection. Please enter a number between 1 and {fats.Length}.");
            }
        }

        static void ShowHelp()
        {
            Console.WriteLine($"usage: {AppDomain.CurrentDomain.FriendlyName} <executable-file> <global-metadata> <output-directory> [--output-json]");
        }

        private static bool Init(string il2cppPath, string metadataPath, out Metadata metadata, out Il2Cpp il2Cpp, string outputDir = null, DumpMetadata dumpMetadata = null)
        {
            return Dumper.Init(il2cppPath, metadataPath, config, out metadata, out il2Cpp, outputDir, dumpMetadata);
        }

        private static bool Dump(Metadata metadata, Il2Cpp il2Cpp, string outputDir)
        {
            return Dumper.ExecuteDump(metadata, il2Cpp, config, outputDir);
        }
    }
}
