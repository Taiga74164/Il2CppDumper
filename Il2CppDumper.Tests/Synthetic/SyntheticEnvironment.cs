using System;
using System.IO;
using Il2CppDumper;

namespace Il2CppDumper.Tests.Synthetic
{
    public sealed class SyntheticIl2Cpp : Il2Cpp
    {
        public SyntheticIl2Cpp(double version = 29) : base(new MemoryStream(new byte[4096]))
        {
            Version = version;
            types = new Il2CppType[]
            {
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_OBJECT, attrs = 0 }, // 0: object / SyntheticClass
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE, attrs = 0 }, // 1: SyntheticEnum
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_I4, attrs = (uint)Il2CppConstants.FIELD_ATTRIBUTE_PUBLIC }, // 2: int
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_I4, attrs = (uint)(Il2CppConstants.FIELD_ATTRIBUTE_PUBLIC | Il2CppConstants.FIELD_ATTRIBUTE_LITERAL) }, // 3: const int
                new Il2CppType { type = Il2CppTypeEnum.IL2CPP_TYPE_VOID, attrs = 0 }, // 4: void
            };
            methodPointers = new ulong[] { 0x1000 };
            customAttributeGenerators = Array.Empty<ulong>();
        }

        public override ulong MapVATR(ulong addr) => addr;
        public override ulong MapRTVA(ulong addr) => addr;
        public override bool Search() => true;
        public override bool PlusSearch(int methodCount, int typeDefinitionsCount, int imageCount) => true;
        public override bool SymbolSearch() => true;
        public override SectionHelper GetSectionHelper(int methodCount, int typeDefinitionsCount, int imageCount) => null!;
        public override bool CheckDump() => false;
    }

    public static class SyntheticEnvironment
    {
        public static (Metadata metadata, SyntheticIl2Cpp il2Cpp, Il2CppExecutor executor) Create(
            MetadataVersionFamily family,
            int seed = 42)
        {
            var bytes = SyntheticMetadataGenerator.Generate(family, seed);
            var metadata = new Metadata(new MemoryStream(bytes));
            var il2Cpp = new SyntheticIl2Cpp((double)family);
            var executor = new Il2CppExecutor(metadata, il2Cpp);
            return (metadata, il2Cpp, executor);
        }

        public static void DecompileToDirectory(
            Il2CppExecutor executor,
            Config config,
            string outputDir)
        {
            if (!outputDir.EndsWith(Path.DirectorySeparatorChar.ToString()) && !outputDir.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
            {
                outputDir += Path.DirectorySeparatorChar;
            }
            Directory.CreateDirectory(outputDir);
            var decompiler = new Il2CppDecompiler(executor);
            decompiler.Decompile(config, outputDir);
            JsonDumpGenerator.Write(outputDir, decompiler, executor, config);
        }
    }
}
