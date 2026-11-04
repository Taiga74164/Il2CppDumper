using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;

namespace Il2CppDumper.Tests
{
    public class BinaryNinjaHeaderConverterTests
    {
        private static string? _pythonExecutable;

        private static string ResolvePythonExecutable()
        {
            if (_pythonExecutable != null)
                return _pythonExecutable;

            var candidates = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? new[] { "python", "python3", "py" }
                : new[] { "python3", "python" };

            foreach (var candidate in candidates)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = candidate,
                        Arguments = "--version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var proc = Process.Start(psi);
                    if (proc != null && proc.WaitForExit(5000) && proc.ExitCode == 0)
                    {
                        _pythonExecutable = candidate;
                        return candidate;
                    }
                }
                catch
                {
                    // Candidate failed or was not found in PATH; try next.
                }
            }

            throw new InvalidOperationException("Python executable (python or python3) was not found in PATH.");
        }

        private static string FindConverterScriptPath()
        {
            var current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                var candidate = Path.Combine(current, "Il2CppDumper", "il2cpp_header_to_binja.py");
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);

                var parent = Directory.GetParent(current);
                if (parent == null)
                    break;

                current = parent.FullName;
            }

            throw new FileNotFoundException($"Could not locate il2cpp_header_to_binja.py from {AppContext.BaseDirectory}");
        }

        private static string RunConverter(string inputHeader)
        {
            var pythonExe = ResolvePythonExecutable();
            var scriptPath = FindConverterScriptPath();
            var tempDir = Path.Combine(Path.GetTempPath(), "binja_conv_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var inputPath = Path.Combine(tempDir, "il2cpp.h");
                File.WriteAllText(inputPath, inputHeader);

                var psi = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    WorkingDirectory = tempDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add(scriptPath);

                using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to launch python process.");
                var stdOut = process.StandardOutput.ReadToEnd();
                var stdErr = process.StandardError.ReadToEnd();
                var exited = process.WaitForExit(30000);

                Assert.True(exited, $"Converter process timed out. StdOut: {stdOut}, StdErr: {stdErr}");
                Assert.True(process.ExitCode == 0, $"Converter process exited with code {process.ExitCode}. StdErr: {stdErr}");

                var outputPath = Path.Combine(tempDir, "il2cpp_binja.h");
                Assert.True(File.Exists(outputPath), "Expected il2cpp_binja.h to be generated.");
                return File.ReadAllText(outputPath);
            }
            finally
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch
                {
                    // Best effort cleanup of temp directory.
                }
            }
        }

        private static string NormalizeNewlines(string text)
        {
            return text.Replace("\r\n", "\n").Trim();
        }

        [Fact]
        public void Convert_NoEmptyStructs_NormalizesInheritanceClause()
        {
            const string input = "struct Base {\n\tint32_t x;\n};\n" +
                                "struct Derived : Base {\n\tint32_t y;\n};\n";

            var output = RunConverter(input);
            var normalized = NormalizeNewlines(output);

            Assert.DoesNotContain(": Base {", normalized);
            Assert.Contains("struct Derived {\n\tBase super;\n\tint32_t y;\n};", normalized);
        }

        [Fact]
        public void Convert_WithSingleEmptyStruct_NormalizesInheritanceAndRemovesEmptyStruct()
        {
            const string input = "struct Base {\n\tint32_t x;\n};\n" +
                                "struct Derived : Base {\n\tint32_t y;\n};\n" +
                                "struct UnrelatedEmpty {\n};\n";

            var output = RunConverter(input);
            var normalized = NormalizeNewlines(output);

            Assert.DoesNotContain(": Base {", normalized);
            Assert.Contains("struct Derived {\n\tBase super;\n\tint32_t y;\n};", normalized);
            Assert.DoesNotContain("struct UnrelatedEmpty", normalized);
        }

        [Fact]
        public void Convert_WithMultipleEmptyStructs_NormalizesInheritanceAndRemovesAllEmptyStructs()
        {
            const string input = "struct Base {\n\tint32_t x;\n};\n" +
                                "struct EmptyAlpha {\n};\n" +
                                "struct Derived : Base {\n\tint32_t y;\n};\n" +
                                "struct EmptyBeta {\n};\n" +
                                "struct EmptyGamma {\n};\n";

            var output = RunConverter(input);
            var normalized = NormalizeNewlines(output);

            Assert.DoesNotContain(": Base {", normalized);
            Assert.Contains("struct Derived {\n\tBase super;\n\tint32_t y;\n};", normalized);
            Assert.DoesNotContain("struct EmptyAlpha", normalized);
            Assert.DoesNotContain("struct EmptyBeta", normalized);
            Assert.DoesNotContain("struct EmptyGamma", normalized);
        }

        [Fact]
        public void Convert_DerivedStructureNormalization_IsIdenticalRegardlessOfEmptyStructs()
        {
            const string baseSnippet = "struct Base {\n\tint32_t x;\n};\n" +
                                      "struct Derived : Base {\n\tint32_t y;\n};\n";

            var outputNoEmpty = NormalizeNewlines(RunConverter(baseSnippet));
            var outputSingleEmpty = NormalizeNewlines(RunConverter(baseSnippet + "struct EmptyOne {\n};\n"));
            var outputMultipleEmpty = NormalizeNewlines(RunConverter(
                "struct EmptyZero {\n};\n" + baseSnippet + "struct EmptyOne {\n};\nstruct EmptyTwo {\n};\n"));

            const string expectedDerivedDefinition = "struct Derived {\n\tBase super;\n\tint32_t y;\n};";

            Assert.Contains(expectedDerivedDefinition, outputNoEmpty);
            Assert.Contains(expectedDerivedDefinition, outputSingleEmpty);
            Assert.Contains(expectedDerivedDefinition, outputMultipleEmpty);
        }

        [Fact]
        public void Convert_EmptyBaseStructWithoutUnrelatedEmptyStruct_NormalizesSuperMemberToVoidPointer()
        {
            const string input = "struct Base {\n};\n" +
                                "struct Derived : Base {\n\tint32_t y;\n};\n";

            var output = RunConverter(input);
            var normalized = NormalizeNewlines(output);

            Assert.DoesNotContain("struct Base", normalized);
            Assert.DoesNotContain("Base super;", normalized);
            Assert.DoesNotContain(": Base {", normalized);
            Assert.Contains("struct Derived {\n\tvoid *super;\n\tint32_t y;\n};", normalized);
        }

        [Fact]
        public void Convert_EmptyBaseStructWithPrecedingUnrelatedEmptyStruct_NormalizesSuperMemberToVoidPointer()
        {
            const string input = "struct Unrelated {\n};\n" +
                                "struct Base {\n};\n" +
                                "struct Derived : Base {\n\tint32_t y;\n};\n";

            var output = RunConverter(input);
            var normalized = NormalizeNewlines(output);

            Assert.DoesNotContain("struct Unrelated", normalized);
            Assert.DoesNotContain("struct Base", normalized);
            Assert.DoesNotContain("Base super;", normalized);
            Assert.DoesNotContain(": Base {", normalized);
            Assert.Contains("struct Derived {\n\tvoid *super;\n\tint32_t y;\n};", normalized);
        }

        [Fact]
        public void Convert_EmptyBaseStructWithSurroundingEmptyStructs_NormalizesSuperMemberToVoidPointer()
        {
            const string input = "struct PrefixEmpty {\n};\n" +
                                "struct Base {\n};\n" +
                                "struct SuffixEmpty {\n};\n" +
                                "struct Derived : Base {\n\tint32_t y;\n};\n";

            var output = RunConverter(input);
            var normalized = NormalizeNewlines(output);

            Assert.DoesNotContain("struct PrefixEmpty", normalized);
            Assert.DoesNotContain("struct Base", normalized);
            Assert.DoesNotContain("struct SuffixEmpty", normalized);
            Assert.DoesNotContain("Base super;", normalized);
            Assert.DoesNotContain(": Base {", normalized);
            Assert.Contains("struct Derived {\n\tvoid *super;\n\tint32_t y;\n};", normalized);
        }

        [Fact]
        public void Convert_MultipleInheritanceHierarchies_NormalizesAllDerivedTypes()
        {
            const string input = "struct GrandParent {\n\tint32_t a;\n};\n" +
                                "struct Parent : GrandParent {\n\tint32_t b;\n};\n" +
                                "struct Child : Parent {\n\tint32_t c;\n};\n" +
                                "struct Sibling : GrandParent {\n\tint32_t d;\n};\n";

            var output = RunConverter(input);
            var normalized = NormalizeNewlines(output);

            Assert.DoesNotContain(": GrandParent {", normalized);
            Assert.DoesNotContain(": Parent {", normalized);
            Assert.Contains("struct Parent {\n\tGrandParent super;\n\tint32_t b;\n};", normalized);
            Assert.Contains("struct Child {\n\tParent super;\n\tint32_t c;\n};", normalized);
            Assert.Contains("struct Sibling {\n\tGrandParent super;\n\tint32_t d;\n};", normalized);
        }

        [Fact]
        public void Convert_EmptyStructMember_ReplacesWithVoidPointer()
        {
            const string input = "struct TargetEmpty {\n};\n" +
                                "struct Consumer {\n" +
                                "\tTargetEmpty field1;\n" +
                                "\t struct TargetEmpty field2;\n" +
                                "};\n";

            var output = RunConverter(input);
            var normalized = NormalizeNewlines(output);

            Assert.DoesNotContain("struct TargetEmpty {\n};", normalized);
            Assert.Contains("\tvoid *field1;", normalized);
            Assert.Contains("\t void *field2;", normalized);
        }

        [Fact]
        public void Convert_UnknownTypeInStruct_GeneratesForwardDeclarationInHeader()
        {
            const string input = "struct Container {\n\tUndefinedType *field;\n};\n";

            var output = RunConverter(input);
            var normalized = NormalizeNewlines(output);

            Assert.StartsWith("struct UndefinedType;\n", normalized);
            Assert.Contains("struct Container {", normalized);
        }
    }
}
