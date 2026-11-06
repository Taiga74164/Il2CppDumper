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
        [Fact]
        public void Diff_OverloadMethodAddressChange_IsDetectedAndDoesNotDropEarlierOverload()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "dump-diff-overload-addr-" + Guid.NewGuid());
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
                            fullName = "Game.Player",
                            methods = new List<JsonDumpMethod>
                            {
                                new() { name = "MethodM", returnType = "int", parameters = new List<JsonDumpParameter> { new() { type = "int", name = "x" } } },
                                new() { name = "MethodM", returnType = "int", parameters = new List<JsonDumpParameter> { new() { type = "string", name = "s" } } }
                            }
                        }
                    }
                };

                var sigInt = "int Game.Player$$MethodM (Game.Player* __this, int x, const MethodInfo* method);";
                var sigStr = "int Game.Player$$MethodM (Game.Player* __this, string s, const MethodInfo* method);";

                var leftScript = new ScriptJson
                {
                    ScriptMethod = new List<ScriptMethod>
                    {
                        new() { Name = "Game.Player$$MethodM", Signature = sigInt, Address = 0x1000 },
                        new() { Name = "Game.Player$$MethodM", Signature = sigStr, Address = 0x2000 }
                    }
                };

                var rightScript = new ScriptJson
                {
                    ScriptMethod = new List<ScriptMethod>
                    {
                        new() { Name = "Game.Player$$MethodM", Signature = sigInt, Address = 0x1800 },
                        new() { Name = "Game.Player$$MethodM", Signature = sigStr, Address = 0x2000 }
                    }
                };

                CreateDumpFixtures(leftDir, model, leftScript);
                CreateDumpFixtures(rightDir, model, rightScript);

                var exitCode = DumpDiffer.Execute(leftDir, rightDir, outPath);

                Assert.Equal(1, exitCode);
                var diff = JsonSerializer.Deserialize<DumpDiffResult>(File.ReadAllText(outPath))!;

                Assert.NotNull(diff.methods.addressChanged);
                Assert.Single(diff.methods.addressChanged);
                var change = diff.methods.addressChanged[0];
                Assert.Equal("Game.Player", change.type);
                Assert.Equal(sigInt, change.method);
                Assert.Equal("0x1000", change.left);
                Assert.Equal("0x1800", change.right);
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
        public void Diff_OverloadMethodReorderingInScriptJson_DoesNotProduceFalseChanges()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "dump-diff-overload-reorder-" + Guid.NewGuid());
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
                            fullName = "Game.Player",
                            methods = new List<JsonDumpMethod>
                            {
                                new() { name = "MethodM", returnType = "int", parameters = new List<JsonDumpParameter> { new() { type = "int", name = "x" } } },
                                new() { name = "MethodM", returnType = "int", parameters = new List<JsonDumpParameter> { new() { type = "string", name = "s" } } }
                            }
                        }
                    }
                };

                var sigInt = "int Game.Player$$MethodM (Game.Player* __this, int x, const MethodInfo* method);";
                var sigStr = "int Game.Player$$MethodM (Game.Player* __this, string s, const MethodInfo* method);";

                var leftScript = new ScriptJson
                {
                    ScriptMethod = new List<ScriptMethod>
                    {
                        new() { Name = "Game.Player$$MethodM", Signature = sigInt, Address = 0x1000 },
                        new() { Name = "Game.Player$$MethodM", Signature = sigStr, Address = 0x2000 }
                    }
                };

                // Opposite order on right: sigStr first (0x2000), then sigInt (0x1000)
                var rightScript = new ScriptJson
                {
                    ScriptMethod = new List<ScriptMethod>
                    {
                        new() { Name = "Game.Player$$MethodM", Signature = sigStr, Address = 0x2000 },
                        new() { Name = "Game.Player$$MethodM", Signature = sigInt, Address = 0x1000 }
                    }
                };

                CreateDumpFixtures(leftDir, model, leftScript);
                CreateDumpFixtures(rightDir, model, rightScript);

                var exitCode = DumpDiffer.Execute(leftDir, rightDir, outPath);

                Assert.Equal(0, exitCode);
                var diff = JsonSerializer.Deserialize<DumpDiffResult>(File.ReadAllText(outPath))!;
                Assert.Empty(diff.methods.addressChanged!);
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
        public void Diff_OverloadNewSignatures_AreSortedOrdinallyInAddedMethods()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "dump-diff-overload-sort-" + Guid.NewGuid());
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
                            fullName = "Game.Player",
                            methods = new List<JsonDumpMethod>
                            {
                                new() { name = "M", returnType = "void", parameters = new List<JsonDumpParameter> { new() { type = "int", name = "x" } } }
                            }
                        }
                    }
                };

                // Right has overloads zType and aType inserted in order zType then aType
                var rightModel = new JsonDumpModel
                {
                    types = new List<JsonDumpType>
                    {
                        new()
                        {
                            fullName = "Game.Player",
                            methods = new List<JsonDumpMethod>
                            {
                                new() { name = "M", returnType = "void", parameters = new List<JsonDumpParameter> { new() { type = "int", name = "x" } } },
                                new() { name = "M", returnType = "void", parameters = new List<JsonDumpParameter> { new() { type = "zType", name = "z" } } },
                                new() { name = "M", returnType = "void", parameters = new List<JsonDumpParameter> { new() { type = "aType", name = "a" } } }
                            }
                        }
                    }
                };

                CreateDumpFixtures(leftDir, leftModel);
                CreateDumpFixtures(rightDir, rightModel);

                var exitCode = DumpDiffer.Execute(leftDir, rightDir, outPath);

                Assert.Equal(1, exitCode);
                var diff = JsonSerializer.Deserialize<DumpDiffResult>(File.ReadAllText(outPath))!;
                var playerChange = diff.types.changed.First(t => t.type == "Game.Player");

                Assert.Equal(2, playerChange.methods.added.Count);
                // Must be ordinally sorted: aType before zType
                Assert.Equal("void M(aType)", playerChange.methods.added[0]);
                Assert.Equal("void M(zType)", playerChange.methods.added[1]);
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
        public void Diff_DuplicateIdenticalSignatures_DoesNotThrowArgumentException()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "dump-diff-dup-sig-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var dir = Path.Combine(tempDir, "target");
                var outPath = Path.Combine(tempDir, "dump-diff.json");

                var model = new JsonDumpModel
                {
                    types = new List<JsonDumpType>
                    {
                        new()
                        {
                            fullName = "Game.Dup",
                            methods = new List<JsonDumpMethod>
                            {
                                new() { name = "M", returnType = "void", parameters = new List<JsonDumpParameter>() },
                                new() { name = "M", returnType = "void", parameters = new List<JsonDumpParameter>() }
                            }
                        }
                    }
                };

                CreateDumpFixtures(dir, model);

                // Comparing dir with itself must succeed with exit 0 and not throw ArgumentException
                var exitCode = DumpDiffer.Execute(dir, dir, outPath);

                Assert.Equal(0, exitCode);
                var diff = JsonSerializer.Deserialize<DumpDiffResult>(File.ReadAllText(outPath))!;
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
        public void Diff_LegacyStringLiteralJson_DoesNotFabricateIndexNorCauseFalseDiffOnReorder()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "dump-diff-legacy-lit-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var leftDir = Path.Combine(tempDir, "left");
                var rightDir = Path.Combine(tempDir, "right");
                var outPath = Path.Combine(tempDir, "dump-diff.json");

                var model = new JsonDumpModel { types = new List<JsonDumpType>() };
                CreateDumpFixtures(leftDir, model);
                CreateDumpFixtures(rightDir, model);

                // Write legacy stringliteral.json (call site references) with reversed order on right
                var leftLegJson = "[{\"value\":\"Alpha\",\"address\":4096},{\"value\":\"Beta\",\"address\":8192}]";
                var rightLegJson = "[{\"value\":\"Beta\",\"address\":8192},{\"value\":\"Alpha\",\"address\":4096}]";
                File.WriteAllText(Path.Combine(leftDir, "stringliteral.json"), leftLegJson, Encoding.UTF8);
                File.WriteAllText(Path.Combine(rightDir, "stringliteral.json"), rightLegJson, Encoding.UTF8);

                var exitCode = DumpDiffer.Execute(leftDir, rightDir, outPath);

                // Because stringliteral-indexed.json is missing, legacy stringliteral.json must NOT be used to fabricate indices
                Assert.Equal(0, exitCode);
                var diff = JsonSerializer.Deserialize<DumpDiffResult>(File.ReadAllText(outPath))!;
                Assert.Empty(diff.literals.valueChangedByIndex);
                Assert.Empty(diff.literals.addedByIndex);
                Assert.NotNull(diff.literals.note);
                Assert.Contains("stringliteral-indexed.json missing on both left and right", diff.literals.note);
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
        public void DiffCli_CorruptedScriptJson_ReturnsExit2WithDiagnostic()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "dump-diff-corrupt-script-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var leftDir = Path.Combine(tempDir, "left");
                var rightDir = Path.Combine(tempDir, "right");
                var outPath = Path.Combine(tempDir, "diff.json");

                var model = new JsonDumpModel { types = new List<JsonDumpType>() };
                CreateDumpFixtures(leftDir, model);
                CreateDumpFixtures(rightDir, model);

                // Corrupted truncated script.json on left
                File.WriteAllText(Path.Combine(leftDir, "script.json"), "{\"ScriptMethod\": [{\"Name\": \"trun", Encoding.UTF8);

                var (exitCode, stdout, _) = RunDiffCli(leftDir, rightDir, outPath);

                Assert.Equal(2, exitCode);
                Assert.Contains("ERROR:", stdout);
                Assert.Contains("script.json", stdout);
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
        public void DiffCli_CorruptedStringLiteralIndexedJson_ReturnsExit2WithDiagnostic()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "dump-diff-corrupt-lit-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var leftDir = Path.Combine(tempDir, "left");
                var rightDir = Path.Combine(tempDir, "right");
                var outPath = Path.Combine(tempDir, "diff.json");

                var model = new JsonDumpModel { types = new List<JsonDumpType>() };
                CreateDumpFixtures(leftDir, model);
                CreateDumpFixtures(rightDir, model);

                // Textual index in stringliteral-indexed.json on left
                File.WriteAllText(Path.Combine(leftDir, "stringliteral-indexed.json"), "[{\"index\": \"not_a_number\", \"value\": \"test\"}]", Encoding.UTF8);

                var (exitCode, stdout, _) = RunDiffCli(leftDir, rightDir, outPath);

                Assert.Equal(2, exitCode);
                Assert.Contains("ERROR:", stdout);
                Assert.Contains("stringliteral-indexed.json", stdout);
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
        public void DiffCli_DumpJsonWithoutTypes_ReturnsExit2WithDiagnostic()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "dump-diff-no-types-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var leftDir = Path.Combine(tempDir, "left");
                var rightDir = Path.Combine(tempDir, "right");
                var outPath = Path.Combine(tempDir, "diff.json");

                Directory.CreateDirectory(leftDir);
                Directory.CreateDirectory(rightDir);
                File.WriteAllText(Path.Combine(leftDir, "dump.json"), "{}", Encoding.UTF8);
                File.WriteAllText(Path.Combine(rightDir, "dump.json"), "{\"types\":[]}", Encoding.UTF8);

                var (exitCode, stdout, _) = RunDiffCli(leftDir, rightDir, outPath);

                Assert.Equal(2, exitCode);
                Assert.Contains("ERROR:", stdout);
                Assert.Contains("dump.json", stdout);
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
        public void Diff_FieldOffsetDriftOnly_DoesNotTriggerFieldChangeNorExit1()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "dump-diff-offset-only-" + Guid.NewGuid());
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
                            fullName = "Game.Entity",
                            fields = new List<JsonDumpField> { new() { name = "count", type = "int", offset = "0x10" } }
                        }
                    }
                };

                // Same type int, but offset drifted 0x10 -> 0x18
                var rightModel = new JsonDumpModel
                {
                    types = new List<JsonDumpType>
                    {
                        new()
                        {
                            fullName = "Game.Entity",
                            fields = new List<JsonDumpField> { new() { name = "count", type = "int", offset = "0x18" } }
                        }
                    }
                };

                CreateDumpFixtures(leftDir, leftModel);
                CreateDumpFixtures(rightDir, rightModel);

                var exitCode = DumpDiffer.Execute(leftDir, rightDir, outPath);

                // B5: Offset drift alone is passthrough only; must exit 0 and not trigger types.changed
                Assert.Equal(0, exitCode);
                var diff = JsonSerializer.Deserialize<DumpDiffResult>(File.ReadAllText(outPath))!;
                Assert.Empty(diff.types.changed);
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
        public void Diff_FieldTypeChange_RetainsOffsetsAsPassthroughDetails()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "dump-diff-type-change-offset-" + Guid.NewGuid());
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
                            fullName = "Game.Entity",
                            fields = new List<JsonDumpField> { new() { name = "count", type = "int", offset = "0x10" } }
                        }
                    }
                };

                // Type changed int -> long with different offsets
                var rightModel = new JsonDumpModel
                {
                    types = new List<JsonDumpType>
                    {
                        new()
                        {
                            fullName = "Game.Entity",
                            fields = new List<JsonDumpField> { new() { name = "count", type = "long", offset = "0x20" } }
                        }
                    }
                };

                CreateDumpFixtures(leftDir, leftModel);
                CreateDumpFixtures(rightDir, rightModel);

                var exitCode = DumpDiffer.Execute(leftDir, rightDir, outPath);

                Assert.Equal(1, exitCode);
                var diff = JsonSerializer.Deserialize<DumpDiffResult>(File.ReadAllText(outPath))!;
                Assert.Single(diff.types.changed);
                var entityChange = diff.types.changed[0];
                Assert.Single(entityChange.fields.changed);
                var fc = entityChange.fields.changed[0];
                Assert.Equal("count", fc.field);
                Assert.Equal("int", fc.left.type);
                Assert.Equal("0x10", fc.left.offset);
                Assert.Equal("long", fc.right.type);
                Assert.Equal("0x20", fc.right.offset);
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
        public void Diff_AmbiguousSignatures_OrderIndependent_EmitsConsistentChange()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "dump-diff-ambig-order-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var left1Dir = Path.Combine(tempDir, "left1");
                var right1Dir = Path.Combine(tempDir, "right1");
                var out1Path = Path.Combine(tempDir, "diff1.json");

                var left2Dir = Path.Combine(tempDir, "left2");
                var right2Dir = Path.Combine(tempDir, "right2");
                var out2Path = Path.Combine(tempDir, "diff2.json");

                var model = new JsonDumpModel
                {
                    types = new List<JsonDumpType>
                    {
                        new()
                        {
                            fullName = "Game.Overloaded",
                            methods = new List<JsonDumpMethod>
                            {
                                new() { name = "Process", returnType = "uint16_t" },
                                new() { name = "Process", returnType = "uint16_t" }
                            }
                        }
                    }
                };

                var sigSame = "uint16_t Game.Overloaded$$Process (Game.Overloaded* __this, uint16_t v, const MethodInfo* method);";

                // Order 1: Left [0x1000, 0x2000], Right [0x2000, 0x3000]
                var leftScript1 = new ScriptJson
                {
                    ScriptMethod = new List<ScriptMethod>
                    {
                        new() { Name = "Game.Overloaded$$Process", Signature = sigSame, Address = 0x1000 },
                        new() { Name = "Game.Overloaded$$Process", Signature = sigSame, Address = 0x2000 }
                    }
                };
                var rightScript1 = new ScriptJson
                {
                    ScriptMethod = new List<ScriptMethod>
                    {
                        new() { Name = "Game.Overloaded$$Process", Signature = sigSame, Address = 0x2000 },
                        new() { Name = "Game.Overloaded$$Process", Signature = sigSame, Address = 0x3000 }
                    }
                };

                CreateDumpFixtures(left1Dir, model, leftScript1);
                CreateDumpFixtures(right1Dir, model, rightScript1);

                var exit1 = DumpDiffer.Execute(left1Dir, right1Dir, out1Path);

                // Order 2: Inverted order on both sides
                var leftScript2 = new ScriptJson
                {
                    ScriptMethod = new List<ScriptMethod>
                    {
                        new() { Name = "Game.Overloaded$$Process", Signature = sigSame, Address = 0x2000 },
                        new() { Name = "Game.Overloaded$$Process", Signature = sigSame, Address = 0x1000 }
                    }
                };
                var rightScript2 = new ScriptJson
                {
                    ScriptMethod = new List<ScriptMethod>
                    {
                        new() { Name = "Game.Overloaded$$Process", Signature = sigSame, Address = 0x3000 },
                        new() { Name = "Game.Overloaded$$Process", Signature = sigSame, Address = 0x2000 }
                    }
                };

                CreateDumpFixtures(left2Dir, model, leftScript2);
                CreateDumpFixtures(right2Dir, model, rightScript2);

                var exit2 = DumpDiffer.Execute(left2Dir, right2Dir, out2Path);

                Assert.Equal(1, exit1);
                Assert.Equal(1, exit2);

                var diff1 = JsonSerializer.Deserialize<DumpDiffResult>(File.ReadAllText(out1Path))!;
                var diff2 = JsonSerializer.Deserialize<DumpDiffResult>(File.ReadAllText(out2Path))!;

                Assert.NotNull(diff1.methods.addressChanged);
                Assert.Single(diff1.methods.addressChanged);
                Assert.Equal("0x1000", diff1.methods.addressChanged[0].left);
                Assert.Equal("0x3000", diff1.methods.addressChanged[0].right);

                Assert.NotNull(diff2.methods.addressChanged);
                Assert.Single(diff2.methods.addressChanged);
                Assert.Equal("0x1000", diff2.methods.addressChanged[0].left);
                Assert.Equal("0x3000", diff2.methods.addressChanged[0].right);
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
        public void Diff_AmbiguousSignatures_AllAddressesChanged_EmitsNoteAndExit1()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "dump-diff-ambig-all-changed-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var leftDir = Path.Combine(tempDir, "left");
                var rightDir = Path.Combine(tempDir, "right");
                var outPath = Path.Combine(tempDir, "diff.json");

                var model = new JsonDumpModel
                {
                    types = new List<JsonDumpType>
                    {
                        new()
                        {
                            fullName = "Game.Overloaded",
                            methods = new List<JsonDumpMethod>
                            {
                                new() { name = "Process", returnType = "uint16_t" },
                                new() { name = "Process", returnType = "uint16_t" }
                            }
                        }
                    }
                };

                var sigSame = "uint16_t Game.Overloaded$$Process (Game.Overloaded* __this, uint16_t v, const MethodInfo* method);";

                // All addresses changed: Left [0x1000, 0x2000] vs Right [0x3000, 0x4000]
                var leftScript = new ScriptJson
                {
                    ScriptMethod = new List<ScriptMethod>
                    {
                        new() { Name = "Game.Overloaded$$Process", Signature = sigSame, Address = 0x1000 },
                        new() { Name = "Game.Overloaded$$Process", Signature = sigSame, Address = 0x2000 }
                    }
                };
                var rightScript = new ScriptJson
                {
                    ScriptMethod = new List<ScriptMethod>
                    {
                        new() { Name = "Game.Overloaded$$Process", Signature = sigSame, Address = 0x3000 },
                        new() { Name = "Game.Overloaded$$Process", Signature = sigSame, Address = 0x4000 }
                    }
                };

                CreateDumpFixtures(leftDir, model, leftScript);
                CreateDumpFixtures(rightDir, model, rightScript);

                var exitCode = DumpDiffer.Execute(leftDir, rightDir, outPath);

                Assert.Equal(1, exitCode);
                var diff = JsonSerializer.Deserialize<DumpDiffResult>(File.ReadAllText(outPath))!;

                Assert.True(diff.HasDifferences);
                Assert.NotNull(diff.methods.addressChanged);
                Assert.Empty(diff.methods.addressChanged); // No arbitrary 1-to-1 matching
                Assert.NotNull(diff.methods.note);
                Assert.Contains("Address comparison has unresolved ambiguous overloads: unable to correlate", diff.methods.note);
                Assert.Contains(sigSame, diff.methods.note);
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
        public void DiffCli_ScriptJson_InvalidAddressString_ReturnsExit2WithDiagnostic()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "dump-diff-script-bad-addr-str-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var leftDir = Path.Combine(tempDir, "left");
                var rightDir = Path.Combine(tempDir, "right");
                var outPath = Path.Combine(tempDir, "diff.json");

                var model = new JsonDumpModel { types = new List<JsonDumpType>() };
                CreateDumpFixtures(leftDir, model);
                CreateDumpFixtures(rightDir, model);

                // script.json with non-numeric string address
                File.WriteAllText(Path.Combine(leftDir, "script.json"), "{\"ScriptMethod\": [{\"Name\": \"Foo$$Bar\", \"Address\": \"not-an-address\"}]}", Encoding.UTF8);

                var (exitCode, stdout, _) = RunDiffCli(leftDir, rightDir, outPath);

                Assert.Equal(2, exitCode);
                Assert.Contains("ERROR:", stdout);
                Assert.Contains("script.json", stdout);
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
        public void DiffCli_ScriptJson_InvalidAddressBoolean_ReturnsExit2WithDiagnostic()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "dump-diff-script-bad-addr-bool-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var leftDir = Path.Combine(tempDir, "left");
                var rightDir = Path.Combine(tempDir, "right");
                var outPath = Path.Combine(tempDir, "diff.json");

                var model = new JsonDumpModel { types = new List<JsonDumpType>() };
                CreateDumpFixtures(leftDir, model);
                CreateDumpFixtures(rightDir, model);

                // script.json with boolean address
                File.WriteAllText(Path.Combine(leftDir, "script.json"), "{\"ScriptMethod\": [{\"Name\": \"Foo$$Bar\", \"Address\": true}]}", Encoding.UTF8);

                var (exitCode, stdout, _) = RunDiffCli(leftDir, rightDir, outPath);

                Assert.Equal(2, exitCode);
                Assert.Contains("ERROR:", stdout);
                Assert.Contains("script.json", stdout);
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
        public void DiffCli_ScriptJson_MissingAddress_ReturnsExit2WithDiagnostic()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "dump-diff-script-missing-addr-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var leftDir = Path.Combine(tempDir, "left");
                var rightDir = Path.Combine(tempDir, "right");
                var outPath = Path.Combine(tempDir, "diff.json");

                var model = new JsonDumpModel { types = new List<JsonDumpType>() };
                CreateDumpFixtures(leftDir, model);
                CreateDumpFixtures(rightDir, model);

                // script.json missing Address property
                File.WriteAllText(Path.Combine(leftDir, "script.json"), "{\"ScriptMethod\": [{\"Name\": \"Foo$$Bar\"}]}", Encoding.UTF8);

                var (exitCode, stdout, _) = RunDiffCli(leftDir, rightDir, outPath);

                Assert.Equal(2, exitCode);
                Assert.Contains("ERROR:", stdout);
                Assert.Contains("script.json", stdout);
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
        public void DiffCli_ScriptJson_MissingName_ReturnsExit2WithDiagnostic()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "dump-diff-script-missing-name-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var leftDir = Path.Combine(tempDir, "left");
                var rightDir = Path.Combine(tempDir, "right");
                var outPath = Path.Combine(tempDir, "diff.json");

                var model = new JsonDumpModel { types = new List<JsonDumpType>() };
                CreateDumpFixtures(leftDir, model);
                CreateDumpFixtures(rightDir, model);

                // script.json missing Name property
                File.WriteAllText(Path.Combine(leftDir, "script.json"), "{\"ScriptMethod\": [{\"Address\": \"0x1000\"}]}", Encoding.UTF8);

                var (exitCode, stdout, _) = RunDiffCli(leftDir, rightDir, outPath);

                Assert.Equal(2, exitCode);
                Assert.Contains("ERROR:", stdout);
                Assert.Contains("script.json", stdout);
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
        public void Diff_ScriptJson_ValidNumericZeroAddress_Accepted()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "dump-diff-script-zero-addr-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var leftDir = Path.Combine(tempDir, "left");
                var rightDir = Path.Combine(tempDir, "right");
                var outPath = Path.Combine(tempDir, "diff.json");

                var model = new JsonDumpModel
                {
                    types = new List<JsonDumpType>
                    {
                        new()
                        {
                            fullName = "Game.Abstract",
                            methods = new List<JsonDumpMethod> { new() { name = "AbstractMethod", returnType = "void" } }
                        }
                    }
                };

                Directory.CreateDirectory(leftDir);
                Directory.CreateDirectory(rightDir);

                File.WriteAllText(Path.Combine(leftDir, "dump.json"), JsonSerializer.Serialize(model), Encoding.UTF8);
                File.WriteAllText(Path.Combine(rightDir, "dump.json"), JsonSerializer.Serialize(model), Encoding.UTF8);

                // Left has numeric 0, Right has string "0x0"
                File.WriteAllText(Path.Combine(leftDir, "script.json"), "{\"ScriptMethod\": [{\"Name\": \"Game.Abstract$$AbstractMethod\", \"Address\": 0}]}", Encoding.UTF8);
                File.WriteAllText(Path.Combine(rightDir, "script.json"), "{\"ScriptMethod\": [{\"Name\": \"Game.Abstract$$AbstractMethod\", \"Address\": \"0x0\"}]}", Encoding.UTF8);

                var exitCode = DumpDiffer.Execute(leftDir, rightDir, outPath);

                Assert.Equal(0, exitCode);
                var diff = JsonSerializer.Deserialize<DumpDiffResult>(File.ReadAllText(outPath))!;
                Assert.False(diff.HasDifferences);
                Assert.NotNull(diff.methods.addressChanged);
                Assert.Empty(diff.methods.addressChanged);
                Assert.Null(diff.methods.note);
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
