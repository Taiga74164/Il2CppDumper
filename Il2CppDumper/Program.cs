using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;

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
            string outputDir = null;

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
                foreach (var arg in args)
                {
                    if (File.Exists(arg))
                    {
                        var file = File.ReadAllBytes(arg);
                        if (file.Length >= 4 && BitConverter.ToUInt32(file, 0) == 0xFAB11BAF)
                        {
                            metadataPath = arg;
                        }
                        else if (il2cppPath == null)
                        {
                            il2cppPath = arg;
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
                if (args.Length == 3 && outputDir == null && il2cppPath != null && metadataPath != null)
                {
                    var outDirArg = args.First(x => x != il2cppPath && x != metadataPath);
                    try
                    {
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
                Console.WriteLine($"ERROR: Metadata file not found or encrypted.");
                exitCode = 1;
            }
            else
            {
                try
                {
                    var dumpMetadata = DumpMetadataWriter.WriteInitial(outputDir, il2cppPath, metadataPath, config);
                    if (Init(il2cppPath, metadataPath, out var metadata, out var il2Cpp, outputDir, dumpMetadata))
                    {
                        if (!Dump(metadata, il2Cpp, outputDir))
                        {
                            exitCode = 1;
                        }
                    }
                    else
                    {
                        exitCode = 1;
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
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

        static void ShowHelp()
        {
            Console.WriteLine($"usage: {AppDomain.CurrentDomain.FriendlyName} <executable-file> <global-metadata> <output-directory>");
        }

        private static bool Init(string il2cppPath, string metadataPath, out Metadata metadata, out Il2Cpp il2Cpp, string outputDir = null, DumpMetadata dumpMetadata = null)
        {
            Console.WriteLine("Initializing metadata...");
            var metadataBytes = File.ReadAllBytes(metadataPath);
            metadata = new Metadata(new MemoryStream(metadataBytes));
            Console.WriteLine($"Metadata Version: {metadata.Version}");
            if (outputDir != null && dumpMetadata != null)
            {
                DumpMetadataWriter.UpdateMetadataVersion(outputDir, dumpMetadata, metadata.Version);
            }

            Console.WriteLine("Initializing il2cpp file...");
            var il2cppBytes = File.ReadAllBytes(il2cppPath);
            var il2cppMagic = BitConverter.ToUInt32(il2cppBytes, 0);
            var il2CppMemory = new MemoryStream(il2cppBytes);
            switch (il2cppMagic)
            {
                default:
                    throw new NotSupportedException("ERROR: il2cpp file not supported.");
                case 0x6D736100:
                    var web = new WebAssembly(il2CppMemory);
                    il2Cpp = web.CreateMemory();
                    break;
                case 0x304F534E:
                    var nso = new NSO(il2CppMemory);
                    il2Cpp = nso.UnCompress();
                    break;
                case uint peMagic when (peMagic & 0xFFFF) == 0x5A4D: //PE
                    il2Cpp = new PE(il2CppMemory);
                    break;
                case 0x464c457f: //ELF
                    if (il2cppBytes[4] == 2) //ELF64
                    {
                        il2Cpp = new Elf64(il2CppMemory);
                    }
                    else
                    {
                        il2Cpp = new Elf(il2CppMemory);
                    }
                    break;
                case 0xCAFEBABE: //FAT Mach-O
                case 0xBEBAFECA:
                    var machofat = new MachoFat(new MemoryStream(il2cppBytes));
                    Console.Write("Select Platform: ");
                    for (var i = 0; i < machofat.fats.Length; i++)
                    {
                        var fat = machofat.fats[i];
                        Console.Write(fat.magic == 0xFEEDFACF ? $"{i + 1}.64bit " : $"{i + 1}.32bit ");
                    }
                    Console.WriteLine();
                    var key = Console.ReadKey(true);
                    var index = int.Parse(key.KeyChar.ToString()) - 1;
                    var magic = machofat.fats[index % 2].magic;
                    il2cppBytes = machofat.GetMacho(index % 2);
                    il2CppMemory = new MemoryStream(il2cppBytes);
                    if (magic == 0xFEEDFACF)
                        goto case 0xFEEDFACF;
                    else
                        goto case 0xFEEDFACE;
                case 0xFEEDFACF: // 64bit Mach-O
                    il2Cpp = new Macho64(il2CppMemory);
                    break;
                case 0xFEEDFACE: // 32bit Mach-O
                    il2Cpp = new Macho(il2CppMemory);
                    break;
            }
            var version = config.ForceIl2CppVersion ? config.ForceVersion : metadata.Version;
            il2Cpp.SetProperties(version, metadata.metadataUsagesCount, metadata);
            Console.WriteLine($"Il2Cpp Version: {il2Cpp.Version}");
            if (config.ForceDump || il2Cpp.CheckDump())
            {
                if (il2Cpp is ElfBase elf)
                {
                    Console.WriteLine("Detected this may be a dump file.");
                    Console.WriteLine("Input il2cpp dump address or input 0 to force continue:");
                    var DumpAddr = Convert.ToUInt64(Console.ReadLine(), 16);
                    if (DumpAddr != 0)
                    {
                        il2Cpp.ImageBase = DumpAddr;
                        il2Cpp.IsDumped = true;
                        if (!config.NoRedirectedPointer)
                        {
                            elf.Reload();
                        }
                    }
                }
                else
                {
                    il2Cpp.IsDumped = true;
                }
            }

            Console.WriteLine("Searching...");
            try
            {
                var flag = il2Cpp.PlusSearch(metadata.methodDefs.Count(x => x.methodIndex >= 0), metadata.typeDefs.Length, metadata.imageDefs.Length);
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    if (!flag && il2Cpp is PE)
                    {
                        Console.WriteLine("Use custom PE loader");
                        il2Cpp = PELoader.Load(il2cppPath);
                        il2Cpp.SetProperties(version, metadata.metadataUsagesCount, metadata);
                        flag = il2Cpp.PlusSearch(metadata.methodDefs.Count(x => x.methodIndex >= 0), metadata.typeDefs.Length, metadata.imageDefs.Length);
                    }
                }
                if (!flag)
                {
                    flag = il2Cpp.Search();
                }
                if (!flag)
                {
                    flag = il2Cpp.SymbolSearch();
                }
                if (!flag)
                {
                    // Try to use Il2CppInspectorRedux's script.json
                    var bridgeResult = Il2CppInspectorBridge.AutoDetect(il2cppPath);
                    if (bridgeResult.HasValue)
                    {
                        var (codeRegistration, metadataRegistration) = bridgeResult.Value;
                        il2Cpp.Init(codeRegistration, metadataRegistration);
                        flag = true;
                    }
                }

                if (!flag)
                {
                    Console.WriteLine("ERROR: Can't use auto mode to process file, try manual mode.");
                    Console.WriteLine("TIP: You can run Il2CppInspectorRedux first to generate script.json");
                    Console.Write("Input CodeRegistration: ");
                    var codeRegistration = Convert.ToUInt64(Console.ReadLine(), 16);
                    Console.Write("Input MetadataRegistration: ");
                    var metadataRegistration = Convert.ToUInt64(Console.ReadLine(), 16);
                    il2Cpp.Init(codeRegistration, metadataRegistration);
                }
                if (il2Cpp.Version >= 27 && il2Cpp.IsDumped)
                {
                    var typeDef = metadata.typeDefs[0];
                    var il2CppType = il2Cpp.types[typeDef.byvalTypeIndex];
                    var typeDefinitionsOffset = metadata.Version >= 38
                        ? (ulong)metadata.header.typeDefinitions.offset
                        : metadata.header.typeDefinitionsOffset;
                    metadata.ImageBase = il2CppType.data.typeHandle - typeDefinitionsOffset;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                Console.WriteLine("ERROR: An error occurred while processing.");
                return false;
            }
            return true;
        }

        private static bool Dump(Metadata metadata, Il2Cpp il2Cpp, string outputDir)
        {
            var success = true;
            Console.WriteLine("Dumping...");
            var executor = new Il2CppExecutor(metadata, il2Cpp);
            var decompiler = new Il2CppDecompiler(executor);
            if (!decompiler.Decompile(config, outputDir))
            {
                success = false;
            }
            Console.WriteLine("Done!");
            if (config.GenerateStruct)
            {
                Console.WriteLine("Generate struct...");
                try
                {
                    var scriptGenerator = new StructGenerator(executor);
                    scriptGenerator.WriteScript(outputDir);
                    Console.WriteLine("Done!");
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    Console.WriteLine("ERROR: Some errors in generating struct");
                    success = false;
                }
            }
            if (config.GenerateDummyDll)
            {
                Console.WriteLine("Generate dummy dll...");
                try
                {
                    DummyAssemblyExporter.Export(executor, outputDir, config.DummyDllAddToken);
                    Console.WriteLine("Done!");
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    Console.WriteLine("ERROR: Some errors in generating dummy dll");
                    success = false;
                }
            }
            return success;
        }
    }
}
