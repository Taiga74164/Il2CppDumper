#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Il2CppDumper.Diff;
using Xunit;

namespace Il2CppDumper.Tests
{
    public class DumpDiffTests
    {
        private static string GetBuildOutputDir()
        {
            var loc = typeof(DumpDiffer).Assembly.Location;
            return Path.GetDirectoryName(loc)!;
        }

        private static (int exitCode, string stdout, string stderr) RunDiffCli(params string[] cliArgs)
        {
            var dllPath = Path.Combine(GetBuildOutputDir(), "Il2CppDumper.Diff.dll");
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add(dllPath);
            foreach (var arg in cliArgs)
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(30000);
            return (process.ExitCode, stdout, stderr);
        }

        private static void CreateDumpFixtures(
            string dir,
            JsonDumpModel dumpModel,
            ScriptJson? script = null,
            List<StringLiteralRecord>? literals = null,
            DumpMetadata? metadata = null)
        {
            Directory.CreateDirectory(dir);
            var dumpJson = JsonSerializer.Serialize(dumpModel, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(dir, "dump.json"), dumpJson, new UTF8Encoding(false));

            if (script != null)
            {
                var scriptJson = JsonSerializer.Serialize(script, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true });
                File.WriteAllText(Path.Combine(dir, "script.json"), scriptJson, new UTF8Encoding(false));
            }

            if (literals != null)
            {
                var litJson = JsonSerializer.Serialize(literals, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(dir, "stringliteral-indexed.json"), litJson, new UTF8Encoding(false));
            }

            if (metadata != null)
            {
                var metaJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(dir, "dump-metadata.json"), metaJson, new UTF8Encoding(false));
            }
        }

        [Fact]
        public void Diff_SyntheticABPair_AssertsAllSevenChangeCategories()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "dump-diff-ab-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var leftDir = Path.Combine(tempDir, "left");
                var rightDir = Path.Combine(tempDir, "right");
                var outPath = Path.Combine(tempDir, "dump-diff.json");

                // 1. Build Left
                var leftModel = new JsonDumpModel
                {
                    types = new List<JsonDumpType>
                    {
                        new JsonDumpType
                        {
                            fullName = "Game.RemovedType",
                            fields = new List<JsonDumpField> { new() { name = "x", type = "int", offset = "0x0" } }
                        },
                        new JsonDumpType
                        {
                            fullName = "Game.UnchangedType",
                            fields = new List<JsonDumpField> { new() { name = "u", type = "int", offset = "0x0" } },
                            methods = new List<JsonDumpMethod> { new() { name = "MethodU", returnType = "void" } }
                        },
                        new JsonDumpType
                        {
                            fullName = "Game.Player",
                            fields = new List<JsonDumpField>
                            {
                                new() { name = "unchangedField", type = "int", offset = "0x10" },
                                new() { name = "changedField", type = "int", offset = "0x14" },
                                new() { name = "removedField", type = "string", offset = "0x18" }
                            },
                            methods = new List<JsonDumpMethod>
                            {
                                new() { name = "MethodUnchanged", returnType = "void" },
                                new() { name = "MethodAddressChanged", returnType = "void" },
                                new() { name = "MethodRemoved", returnType = "void" }
                            }
                        }
                    }
                };

                var leftScript = new ScriptJson
                {
                    ScriptMethod = new List<ScriptMethod>
                    {
                        new() { Name = "Game.UnchangedType$$MethodU", Address = 0x1000 },
                        new() { Name = "Game.Player$$MethodUnchanged", Address = 0x2000 },
                        new() { Name = "Game.Player$$MethodAddressChanged", Address = 0x3000 },
                        new() { Name = "Game.Player$$MethodRemoved", Address = 0x4000 }
                    }
                };

                var leftLiterals = new List<StringLiteralRecord>
                {
                    new() { index = 0, value = "ConstantValue" },
                    new() { index = 1, value = "OldValue" }
                };

                CreateDumpFixtures(leftDir, leftModel, leftScript, leftLiterals);

                // 2. Build Right
                var rightModel = new JsonDumpModel
                {
                    types = new List<JsonDumpType>
                    {
                        new JsonDumpType
                        {
                            fullName = "Game.AddedType",
                            fields = new List<JsonDumpField> { new() { name = "y", type = "int", offset = "0x0" } }
                        },
                        new JsonDumpType
                        {
                            fullName = "Game.UnchangedType",
                            fields = new List<JsonDumpField> { new() { name = "u", type = "int", offset = "0x0" } },
                            methods = new List<JsonDumpMethod> { new() { name = "MethodU", returnType = "void" } }
                        },
                        new JsonDumpType
                        {
                            fullName = "Game.Player",
                            fields = new List<JsonDumpField>
                            {
                                new() { name = "unchangedField", type = "int", offset = "0x10" },
                                new() { name = "changedField", type = "long", offset = "0x20" },
                                new() { name = "addedField", type = "float", offset = "0x28" }
                            },
                            methods = new List<JsonDumpMethod>
                            {
                                new() { name = "MethodUnchanged", returnType = "void" },
                                new() { name = "MethodAddressChanged", returnType = "void" },
                                new() { name = "MethodAdded", returnType = "int" }
                            }
                        }
                    }
                };

                var rightScript = new ScriptJson
                {
                    ScriptMethod = new List<ScriptMethod>
                    {
                        new() { Name = "Game.UnchangedType$$MethodU", Address = 0x1000 },
                        new() { Name = "Game.Player$$MethodUnchanged", Address = 0x2000 },
                        new() { Name = "Game.Player$$MethodAddressChanged", Address = 0x3500 },
                        new() { Name = "Game.Player$$MethodAdded", Address = 0x5000 }
                    }
                };

                var rightLiterals = new List<StringLiteralRecord>
                {
                    new() { index = 0, value = "ConstantValue" },
                    new() { index = 1, value = "NewValue" },
                    new() { index = 2, value = "BrandNewLiteral" }
                };

                CreateDumpFixtures(rightDir, rightModel, rightScript, rightLiterals);

                // 3. Run comparison
                var exitCode = DumpDiffer.Execute(leftDir, rightDir, outPath);

                Assert.Equal(1, exitCode);
                Assert.True(File.Exists(outPath));

                var jsonText = File.ReadAllText(outPath);
                var diff = JsonSerializer.Deserialize<DumpDiffResult>(jsonText)!;

                // Category 1: Added type
                Assert.Contains("Game.AddedType", diff.types.added);
                Assert.DoesNotContain("Game.UnchangedType", diff.types.added);

                // Category 2: Removed type
                Assert.Contains("Game.RemovedType", diff.types.removed);
                Assert.DoesNotContain("Game.UnchangedType", diff.types.removed);

                // Changed type Game.Player
                var playerChange = diff.types.changed.FirstOrDefault(t => t.type == "Game.Player");
                Assert.NotNull(playerChange);

                // Category 3: Added method
                Assert.Contains("MethodAdded", playerChange.methods.added);

                // Category 4: Removed method
                Assert.Contains("MethodRemoved", playerChange.methods.removed);

                // Category 5: Changed field (and added/removed fields)
                Assert.Contains("addedField", playerChange.fields.added);
                Assert.Contains("removedField", playerChange.fields.removed);
                var fieldChange = playerChange.fields.changed.FirstOrDefault(f => f.field == "changedField");
                Assert.NotNull(fieldChange);
                Assert.Equal("int", fieldChange.left.type);
                Assert.Equal("0x14", fieldChange.left.offset);
                Assert.Equal("long", fieldChange.right.type);
                Assert.Equal("0x20", fieldChange.right.offset);

                // Category 6: Changed method address
                Assert.NotNull(diff.methods.addressChanged);
                var addrChange = diff.methods.addressChanged.FirstOrDefault(a => a.type == "Game.Player" && a.method == "MethodAddressChanged");
                Assert.NotNull(addrChange);
                Assert.Equal("0x3000", addrChange.left);
                Assert.Equal("0x3500", addrChange.right);

                // Unchanged method must not appear in addressChanged
                Assert.DoesNotContain(diff.methods.addressChanged, a => a.method == "MethodUnchanged");
                Assert.DoesNotContain(diff.methods.addressChanged, a => a.method == "MethodU");

                // Category 7: Changed literal value and added literal by index
                var litChange = diff.literals.valueChangedByIndex.FirstOrDefault(l => l.index == 1);
                Assert.NotNull(litChange);
                Assert.Equal("OldValue", litChange.left);
                Assert.Equal("NewValue", litChange.right);

                var litAdded = diff.literals.addedByIndex.FirstOrDefault(l => l.index == 2);
                Assert.NotNull(litAdded);
                Assert.Equal("BrandNewLiteral", litAdded.value);

                // Unchanged literal must not appear
                Assert.DoesNotContain(diff.literals.valueChangedByIndex, l => l.index == 0);

                // Summary verification
                Assert.Equal(1, diff.summary.addedTypes);
                Assert.Equal(1, diff.summary.removedTypes);
                Assert.Equal(1, diff.summary.addressChanges);
                Assert.Equal(1, diff.summary.valueChanges);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void Diff_IdenticalInputs_ExitsZeroWithEmptyArraysAndNoNoise()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "dump-diff-same-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var dumpDir = Path.Combine(tempDir, "baseline");
                var outPath = Path.Combine(tempDir, "dump-diff.json");

                var model = new JsonDumpModel
                {
                    types = new List<JsonDumpType>
                    {
                        new JsonDumpType
                        {
                            fullName = "Game.StableType",
                            fields = new List<JsonDumpField> { new() { name = "health", type = "int", offset = "0x10" } },
                            methods = new List<JsonDumpMethod> { new() { name = "GetHealth", returnType = "int" } }
                        }
                    }
                };

                var script = new ScriptJson
                {
                    ScriptMethod = new List<ScriptMethod>
                    {
                        new() { Name = "Game.StableType$$GetHealth", Address = 0x4000 }
                    }
                };

                var literals = new List<StringLiteralRecord>
                {
                    new() { index = 0, value = "Identical" }
                };

                CreateDumpFixtures(dumpDir, model, script, literals);

                var exitCode = DumpDiffer.Execute(dumpDir, dumpDir, outPath);

                Assert.Equal(0, exitCode);
                Assert.True(File.Exists(outPath));

                var jsonText = File.ReadAllText(outPath);
                var diff = JsonSerializer.Deserialize<DumpDiffResult>(jsonText)!;

                Assert.Empty(diff.types.added);
                Assert.Empty(diff.types.removed);
                Assert.Empty(diff.types.changed);
                Assert.NotNull(diff.methods.addressChanged);
                Assert.Empty(diff.methods.addressChanged);
                Assert.Empty(diff.literals.addedByIndex);
                Assert.Empty(diff.literals.valueChangedByIndex);

                Assert.Equal(0, diff.summary.addedTypes);
                Assert.Equal(0, diff.summary.removedTypes);
                Assert.Equal(0, diff.summary.addressChanges);
                Assert.Equal(0, diff.summary.valueChanges);
                Assert.False(diff.HasDifferences);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void Diff_DeterministicOutput_ProducesByteIdenticalOutputAcrossRuns()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "dump-diff-det-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var leftDir = Path.Combine(tempDir, "left");
                var rightDir = Path.Combine(tempDir, "right");
                var outPath1 = Path.Combine(tempDir, "diff1.json");
                var outPath2 = Path.Combine(tempDir, "diff2.json");

                var leftModel = new JsonDumpModel
                {
                    types = new List<JsonDumpType>
                    {
                        new() { fullName = "Z.Type", fields = new List<JsonDumpField> { new() { name = "z", type = "int" } } },
                        new() { fullName = "A.Type", fields = new List<JsonDumpField> { new() { name = "a", type = "int" } } }
                    }
                };
                var rightModel = new JsonDumpModel
                {
                    types = new List<JsonDumpType>
                    {
                        new() { fullName = "A.Type", fields = new List<JsonDumpField> { new() { name = "a", type = "long" } } },
                        new() { fullName = "B.NewType" }
                    }
                };

                CreateDumpFixtures(leftDir, leftModel);
                CreateDumpFixtures(rightDir, rightModel);

                var exit1 = DumpDiffer.Execute(leftDir, rightDir, outPath1);
                var exit2 = DumpDiffer.Execute(leftDir, rightDir, outPath2);

                Assert.Equal(1, exit1);
                Assert.Equal(1, exit2);

                var bytes1 = File.ReadAllBytes(outPath1);
                var bytes2 = File.ReadAllBytes(outPath2);

                Assert.Equal(bytes1, bytes2);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void Diff_ObfuscatedRenamedTypes_DoesNotCrashAndEmitsOnlyStructuralDeltas()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "dump-diff-obf-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var leftDir = Path.Combine(tempDir, "left");
                var rightDir = Path.Combine(tempDir, "right");
                var outPath = Path.Combine(tempDir, "dump-diff.json");

                // Identical internal structure, but type renamed from ObfA to ObfB
                var leftModel = new JsonDumpModel
                {
                    types = new List<JsonDumpType>
                    {
                        new()
                        {
                            fullName = "ObfA",
                            fields = new List<JsonDumpField> { new() { name = "fld1", type = "int", offset = "0x8" } },
                            methods = new List<JsonDumpMethod> { new() { name = "m1", returnType = "void" } }
                        }
                    }
                };

                var rightModel = new JsonDumpModel
                {
                    types = new List<JsonDumpType>
                    {
                        new()
                        {
                            fullName = "ObfB",
                            fields = new List<JsonDumpField> { new() { name = "fld1", type = "int", offset = "0x8" } },
                            methods = new List<JsonDumpMethod> { new() { name = "m1", returnType = "void" } }
                        }
                    }
                };

                CreateDumpFixtures(leftDir, leftModel);
                CreateDumpFixtures(rightDir, rightModel);

                var exitCode = DumpDiffer.Execute(leftDir, rightDir, outPath);

                Assert.Equal(1, exitCode);
                var jsonText = File.ReadAllText(outPath);
                var diff = JsonSerializer.Deserialize<DumpDiffResult>(jsonText)!;

                // Rename matching is out of scope: ObfA is removed, ObfB is added
                Assert.Contains("ObfA", diff.types.removed);
                Assert.Contains("ObfB", diff.types.added);
                Assert.Empty(diff.types.changed);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void Diff_MissingScriptJsonOnOneSide_OmitsAddressSectionWithNote()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "dump-diff-noscript-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var leftDir = Path.Combine(tempDir, "left");
                var rightDir = Path.Combine(tempDir, "right");
                var outPath = Path.Combine(tempDir, "dump-diff.json");

                var model = new JsonDumpModel
                {
                    types = new List<JsonDumpType>
                    {
                        new()
                        {
                            fullName = "Game.Entity",
                            fields = new List<JsonDumpField> { new() { name = "id", type = "int" } }
                        }
                    }
                };

                var script = new ScriptJson
                {
                    ScriptMethod = new List<ScriptMethod> { new() { Name = "Game.Entity$$Init", Address = 0x5000 } }
                };

                // Left has script.json; Right does NOT
                CreateDumpFixtures(leftDir, model, script: script);
                CreateDumpFixtures(rightDir, model, script: null);

                var exitCode = DumpDiffer.Execute(leftDir, rightDir, outPath);

                Assert.Equal(0, exitCode); // Types identical, address comparison omitted
                var jsonText = File.ReadAllText(outPath);
                var diff = JsonSerializer.Deserialize<DumpDiffResult>(jsonText)!;

                Assert.Null(diff.methods.addressChanged);
                Assert.NotNull(diff.methods.note);
                Assert.Contains("script.json missing on right", diff.methods.note);
                Assert.Equal(0, diff.summary.addressChanges);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void Diff_Sha256_UsesSidecarWhenPresent_ComputesOtherwise()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "dump-diff-sha-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var leftDir = Path.Combine(tempDir, "left");
                var rightDir = Path.Combine(tempDir, "right");
                var outPath = Path.Combine(tempDir, "dump-diff.json");

                var model = new JsonDumpModel { types = new List<JsonDumpType>() };

                // Left has dump-metadata.json sidecar with custom sha256
                var sidecar = new DumpMetadata
                {
                    Inputs = new DumpMetadataInputs
                    {
                        Il2Cpp = new DumpMetadataInputFile
                        {
                            Path = @"C:\Games\GameAssembly.dll",
                            Sha256 = "111122223333444455556666777788889999aaaabbbbccccddddeeeeffff0000"
                        }
                    }
                };

                CreateDumpFixtures(leftDir, model, metadata: sidecar);
                CreateDumpFixtures(rightDir, model); // No sidecar, sha256 computed from dump.json

                var exitCode = DumpDiffer.Execute(leftDir, rightDir, outPath);

                Assert.Equal(0, exitCode);
                var diff = JsonSerializer.Deserialize<DumpDiffResult>(File.ReadAllText(outPath))!;

                Assert.Equal(@"C:\Games\GameAssembly.dll", diff.left.source);
                Assert.Equal("111122223333444455556666777788889999aaaabbbbccccddddeeeeffff0000", diff.left.sha256);

                Assert.EndsWith("dump.json", diff.right.source);
                Assert.Equal(64, diff.right.sha256.Length); // Valid sha256 hex string
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void Diff_RecursiveNestedTypes_AreCollectedAndCompared()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "dump-diff-nested-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var leftDir = Path.Combine(tempDir, "left");
                var rightDir = Path.Combine(tempDir, "right");
                var outPath = Path.Combine(tempDir, "dump-diff.json");

                var leftModel = new JsonDumpModel
                {
                    types = new List<JsonDumpType>
                    {
                        new()
                        {
                            fullName = "OuterClass",
                            nested = new List<JsonDumpType>
                            {
                                new()
                                {
                                    fullName = "OuterClass.InnerClass",
                                    fields = new List<JsonDumpField> { new() { name = "val", type = "int", offset = "0x0" } }
                                }
                            }
                        }
                    }
                };

                var rightModel = new JsonDumpModel
                {
                    types = new List<JsonDumpType>
                    {
                        new()
                        {
                            fullName = "OuterClass",
                            nested = new List<JsonDumpType>
                            {
                                new()
                                {
                                    fullName = "OuterClass.InnerClass",
                                    fields = new List<JsonDumpField> { new() { name = "val", type = "long", offset = "0x8" } }
                                }
                            }
                        }
                    }
                };

                CreateDumpFixtures(leftDir, leftModel);
                CreateDumpFixtures(rightDir, rightModel);

                var exitCode = DumpDiffer.Execute(leftDir, rightDir, outPath);

                Assert.Equal(1, exitCode);
                var diff = JsonSerializer.Deserialize<DumpDiffResult>(File.ReadAllText(outPath))!;

                var innerChange = diff.types.changed.FirstOrDefault(t => t.type == "OuterClass.InnerClass");
                Assert.NotNull(innerChange);
                Assert.Equal("long", innerChange.fields.changed[0].right.type);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void DiffCli_ExitCodeDiscipline_EnforcesCodes0_1_2()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "dump-diff-cli-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var leftDir = Path.Combine(tempDir, "left");
                var rightDir = Path.Combine(tempDir, "right");
                var diffOut = Path.Combine(tempDir, "diff.json");

                // Test Exit Code 2: Missing arguments / non-existent directory
                var (codeMissingArgs, outMissing, _) = RunDiffCli("only-one-arg");
                Assert.Equal(2, codeMissingArgs);
                Assert.Contains("usage: Il2CppDumper.Diff", outMissing);

                var (codeNonExistent, outNonExistent, _) = RunDiffCli(Path.Combine(tempDir, "bogus1"), Path.Combine(tempDir, "bogus2"), diffOut);
                Assert.Equal(2, codeNonExistent);
                Assert.Contains("ERROR:", outNonExistent);

                // Setup valid fixtures
                var model1 = new JsonDumpModel
                {
                    types = new List<JsonDumpType> { new() { fullName = "T1", fields = new List<JsonDumpField> { new() { name = "x", type = "int" } } } }
                };
                var model2 = new JsonDumpModel
                {
                    types = new List<JsonDumpType> { new() { fullName = "T2" } }
                };

                CreateDumpFixtures(leftDir, model1);
                CreateDumpFixtures(rightDir, model2);

                // Test Exit Code 1: Differences found
                var (codeDiff, outDiff, _) = RunDiffCli(leftDir, rightDir, diffOut);
                Assert.Equal(1, codeDiff);
                Assert.Contains("Differences found:", outDiff);

                // Test Exit Code 0: Identical
                var (codeSame, outSame, _) = RunDiffCli(leftDir, leftDir, diffOut);
                Assert.Equal(0, codeSame);
                Assert.Contains("No differences found.", outSame);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }
    }
}
