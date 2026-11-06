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
    public class FieldOffsetDiffTests
    {
        private static string GetBuildOutputDir()
        {
            var loc = typeof(FieldOffsetDiff).Assembly.Location;
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

        private static void CreateDumpFixture(string dir, JsonDumpModel dumpModel)
        {
            Directory.CreateDirectory(dir);
            var dumpJson = JsonSerializer.Serialize(dumpModel, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(dir, "dump.json"), dumpJson, new UTF8Encoding(false));
        }

        [Fact]
        public void FieldOffsetDiff_SyntheticABPair_AssertsAllFourClassificationsAndSeverities()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "field-offset-ab-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var leftDir = Path.Combine(tempDir, "left");
                var rightDir = Path.Combine(tempDir, "right");
                var outPath = Path.Combine(tempDir, "field-offset-drift.json");

                // Left model
                var leftModel = new JsonDumpModel
                {
                    types = new List<JsonDumpType>
                    {
                        // 1. Unchanged struct: identical fields and size -> must be absent from report
                        new JsonDumpType
                        {
                            fullName = "Game.UnchangedStruct",
                            size = "0x20",
                            fields = new List<JsonDumpField>
                            {
                                new() { name = "f1", type = "int", offset = "0x10" },
                                new() { name = "f2", type = "int", offset = "0x18" }
                            }
                        },
                        // 2. Struct with all drift classifications:
                        // - hp: shifted (0x28 -> 0x2C)
                        // - mp: removed (0x2C -> null)
                        // Max left offset is 0x2C
                        new JsonDumpType
                        {
                            fullName = "Ns.Obf",
                            size = "0x120",
                            fields = new List<JsonDumpField>
                            {
                                new() { name = "hp", type = "int", offset = "0x28" },
                                new() { name = "mp", type = "int", offset = "0x2C" }
                            }
                        }
                    }
                };

                // Right model
                var rightModel = new JsonDumpModel
                {
                    types = new List<JsonDumpType>
                    {
                        new JsonDumpType
                        {
                            fullName = "Game.UnchangedStruct",
                            size = "0x20",
                            fields = new List<JsonDumpField>
                            {
                                new() { name = "f1", type = "int", offset = "0x10" },
                                new() { name = "f2", type = "int", offset = "0x18" }
                            }
                        },
                        new JsonDumpType
                        {
                            fullName = "Ns.Obf",
                            size = "0x128",
                            fields = new List<JsonDumpField>
                            {
                                // middle-insert addition at 0x20 <= maxLeftOffset (0x2C) -> breaks-external-readers
                                new() { name = "stamina", type = "int", offset = "0x20" },
                                // shifted field
                                new() { name = "hp", type = "int", offset = "0x2C" },
                                // mp removed (absent)
                                // appended-at-end addition at 0x38 > maxLeftOffset (0x2C) -> leaf-consumers-may-survive
                                new() { name = "shield", type = "int", offset = "0x38" }
                            }
                        }
                    }
                };

                CreateDumpFixture(leftDir, leftModel);
                CreateDumpFixture(rightDir, rightModel);

                var (exitCode, stdout, _) = RunDiffCli("--drift", leftDir, rightDir, outPath);

                Assert.Equal(1, exitCode);
                Assert.Contains("Field offset drift detected in 1 struct(s)", stdout);
                Assert.True(File.Exists(outPath));

                var json = File.ReadAllText(outPath);
                var report = JsonSerializer.Deserialize<FieldOffsetDriftReport>(json);

                Assert.NotNull(report);
                Assert.Equal(1, report.schemaVersion);

                // Unchanged struct must be ABSENT from report
                Assert.DoesNotContain(report.structs, s => s.@struct == "Game.UnchangedStruct");

                // Exactly 1 struct reported: Ns.Obf
                Assert.Single(report.structs);
                var s = report.structs[0];
                Assert.Equal("Ns.Obf", s.@struct);
                Assert.Equal("0x120", s.size.left);
                Assert.Equal("0x128", s.size.right);

                // Check all four field classifications and severities
                // 1. Shifted: hp (0x28 -> 0x2C, breaks-external-readers)
                var hpField = s.fields.FirstOrDefault(f => f.name == "hp");
                Assert.NotNull(hpField);
                Assert.Equal("0x28", hpField.left);
                Assert.Equal("0x2C", hpField.right);
                Assert.Equal(FieldOffsetDriftType.Shifted, hpField.drift);
                Assert.Equal(FieldOffsetSeverity.BreaksExternalReaders, hpField.severity);

                // 2. Removed: mp (0x2C -> null, breaks-external-readers)
                var mpField = s.fields.FirstOrDefault(f => f.name == "mp");
                Assert.NotNull(mpField);
                Assert.Equal("0x2C", mpField.left);
                Assert.Null(mpField.right);
                Assert.Equal(FieldOffsetDriftType.Removed, mpField.drift);
                Assert.Equal(FieldOffsetSeverity.BreaksExternalReaders, mpField.severity);

                // 3. Middle-insert added: stamina (null -> 0x20, breaks-external-readers)
                var staminaField = s.fields.FirstOrDefault(f => f.name == "stamina");
                Assert.NotNull(staminaField);
                Assert.Null(staminaField.left);
                Assert.Equal("0x20", staminaField.right);
                Assert.Equal(FieldOffsetDriftType.Added, staminaField.drift);
                Assert.Equal(FieldOffsetSeverity.BreaksExternalReaders, staminaField.severity);

                // 4. Appended-at-end added: shield (null -> 0x38, leaf-consumers-may-survive)
                var shieldField = s.fields.FirstOrDefault(f => f.name == "shield");
                Assert.NotNull(shieldField);
                Assert.Null(shieldField.left);
                Assert.Equal("0x38", shieldField.right);
                Assert.Equal(FieldOffsetDriftType.Added, shieldField.drift);
                Assert.Equal(FieldOffsetSeverity.LeafConsumersMaySurvive, shieldField.severity);

                // Assert verdicts
                Assert.Equal(1, s.verdict.shifted);
                Assert.Equal(1, s.verdict.removed);
                Assert.Equal(2, s.verdict.added);
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
        public void FieldOffsetDiff_SizeChangeOnly_ReportsEmptyFieldsAndSizeChange()
        {
            var leftModel = new JsonDumpModel
            {
                types = new List<JsonDumpType>
                {
                    new JsonDumpType
                    {
                        fullName = "Game.SizeOnlyStruct",
                        size = "0x120",
                        fields = new List<JsonDumpField>
                        {
                            new() { name = "a", type = "int", offset = "0x10" },
                            new() { name = "b", type = "int", offset = "0x18" }
                        }
                    }
                }
            };

            var rightModel = new JsonDumpModel
            {
                types = new List<JsonDumpType>
                {
                    new JsonDumpType
                    {
                        fullName = "Game.SizeOnlyStruct",
                        size = "0x128", // Size changed from 0x120 to 0x128
                        fields = new List<JsonDumpField>
                        {
                            new() { name = "a", type = "int", offset = "0x10" },
                            new() { name = "b", type = "int", offset = "0x18" }
                        }
                    }
                }
            };

            var report = FieldOffsetDiff.Compare(leftModel, rightModel);

            Assert.True(report.HasDrift);
            Assert.Single(report.structs);

            var s = report.structs[0];
            Assert.Equal("Game.SizeOnlyStruct", s.@struct);
            Assert.Equal("0x120", s.size.left);
            Assert.Equal("0x128", s.size.right);
            Assert.Empty(s.fields);
            Assert.Equal(0, s.verdict.shifted);
            Assert.Equal(0, s.verdict.removed);
            Assert.Equal(0, s.verdict.added);
        }

        [Fact]
        public void FieldOffsetDiff_DeterministicOrdering_StructNameThenFieldName()
        {
            var leftModel = new JsonDumpModel
            {
                types = new List<JsonDumpType>
                {
                    new JsonDumpType
                    {
                        fullName = "Z.Type",
                        size = "0x10",
                        fields = new List<JsonDumpField>
                        {
                            new() { name = "z_field", type = "int", offset = "0x0" },
                            new() { name = "a_field", type = "int", offset = "0x4" }
                        }
                    },
                    new JsonDumpType
                    {
                        fullName = "A.Type",
                        size = "0x10",
                        fields = new List<JsonDumpField>
                        {
                            new() { name = "z_field", type = "int", offset = "0x0" },
                            new() { name = "m_field", type = "int", offset = "0x4" }
                        }
                    },
                    new JsonDumpType
                    {
                        fullName = "M.Type",
                        size = "0x10",
                        fields = new List<JsonDumpField>
                        {
                            new() { name = "m_field", type = "int", offset = "0x0" }
                        }
                    }
                }
            };

            var rightModel = new JsonDumpModel
            {
                types = new List<JsonDumpType>
                {
                    new JsonDumpType
                    {
                        fullName = "Z.Type",
                        size = "0x20",
                        fields = new List<JsonDumpField>
                        {
                            new() { name = "z_field", type = "int", offset = "0x8" }, // shifted
                            new() { name = "a_field", type = "int", offset = "0x10" } // shifted
                        }
                    },
                    new JsonDumpType
                    {
                        fullName = "A.Type",
                        size = "0x20",
                        fields = new List<JsonDumpField>
                        {
                            new() { name = "z_field", type = "int", offset = "0x8" }, // shifted
                            new() { name = "m_field", type = "int", offset = "0x10" } // shifted
                        }
                    },
                    new JsonDumpType
                    {
                        fullName = "M.Type",
                        size = "0x20",
                        fields = new List<JsonDumpField>
                        {
                            new() { name = "m_field", type = "int", offset = "0x8" } // shifted
                        }
                    }
                }
            };

            var report = FieldOffsetDiff.Compare(leftModel, rightModel);

            Assert.Equal(1, report.schemaVersion);
            Assert.Equal(3, report.structs.Count);

            // Structs ordered deterministically by struct name: A.Type, M.Type, Z.Type
            Assert.Equal("A.Type", report.structs[0].@struct);
            Assert.Equal("M.Type", report.structs[1].@struct);
            Assert.Equal("Z.Type", report.structs[2].@struct);

            // Fields inside A.Type ordered deterministically by field name: m_field, z_field
            Assert.Equal("m_field", report.structs[0].fields[0].name);
            Assert.Equal("z_field", report.structs[0].fields[1].name);

            // Fields inside Z.Type ordered deterministically by field name: a_field, z_field
            Assert.Equal("a_field", report.structs[2].fields[0].name);
            Assert.Equal("z_field", report.structs[2].fields[1].name);
        }

        [Fact]
        public void FieldOffsetDiff_SameInputs_ProducesEmptyReportAndExitZero()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "field-offset-same-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var inputDir = Path.Combine(tempDir, "baseline");
                var outPath = Path.Combine(tempDir, "field-offset-drift.json");

                var model = new JsonDumpModel
                {
                    types = new List<JsonDumpType>
                    {
                        new JsonDumpType
                        {
                            fullName = "Game.Player",
                            size = "0x40",
                            fields = new List<JsonDumpField>
                            {
                                new() { name = "id", type = "int", offset = "0x10" },
                                new() { name = "name", type = "string", offset = "0x18" }
                            }
                        }
                    }
                };

                CreateDumpFixture(inputDir, model);

                // Run CLI with same input for left and right
                var (exitCode, stdout, stderr) = RunDiffCli("--drift", inputDir, inputDir, outPath);

                Assert.Equal(0, exitCode);
                Assert.Contains("No field offset drift found.", stdout);
                Assert.True(File.Exists(outPath));

                var json = File.ReadAllText(outPath);
                using var doc = JsonDocument.Parse(json);
                Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
                Assert.Equal(0, doc.RootElement.GetProperty("structs").GetArrayLength());
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
        public void FieldOffsetDiff_UsageOrInputError_ReturnsExitCodeTwo()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "field-offset-err-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var nonExistentLeft = Path.Combine(tempDir, "non-existent-left");
                var nonExistentRight = Path.Combine(tempDir, "non-existent-right");
                var outPath = Path.Combine(tempDir, "out.json");

                // Missing files -> exit code 2
                var (exitCode, stdout, _) = RunDiffCli("--drift", nonExistentLeft, nonExistentRight, outPath);
                Assert.Equal(2, exitCode);
                Assert.Contains("ERROR:", stdout);

                // Invalid arguments count -> exit code 2
                var (exitCodeArg, stdoutArg, _) = RunDiffCli("--drift", "one-arg-only");
                Assert.Equal(2, exitCodeArg);
                Assert.Contains("usage:", stdoutArg);
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
        public void FieldOffsetDiff_OutputPathVariations_DirectoryAndExplicitFile()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "field-offset-out-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var leftDir = Path.Combine(tempDir, "left");
                var rightDir = Path.Combine(tempDir, "right");
                var outDir = Path.Combine(tempDir, "report-dir");
                var explicitFile = Path.Combine(tempDir, "custom-name.json");

                var modelLeft = new JsonDumpModel
                {
                    types = new List<JsonDumpType>
                    {
                        new JsonDumpType
                        {
                            fullName = "Game.Actor",
                            size = "0x20",
                            fields = new List<JsonDumpField> { new() { name = "x", type = "int", offset = "0x10" } }
                        }
                    }
                };

                var modelRight = new JsonDumpModel
                {
                    types = new List<JsonDumpType>
                    {
                        new JsonDumpType
                        {
                            fullName = "Game.Actor",
                            size = "0x28", // Size changed
                            fields = new List<JsonDumpField> { new() { name = "x", type = "int", offset = "0x10" } }
                        }
                    }
                };

                CreateDumpFixture(leftDir, modelLeft);
                CreateDumpFixture(rightDir, modelRight);

                // 1. Output as directory -> creates field-offset-drift.json inside directory
                var exit1 = FieldOffsetDiff.Execute(leftDir, rightDir, outDir);
                Assert.Equal(1, exit1);
                Assert.True(File.Exists(Path.Combine(outDir, "field-offset-drift.json")));

                // 2. Output as explicit file path -> writes directly to custom-name.json
                var exit2 = FieldOffsetDiff.Execute(leftDir, rightDir, explicitFile);
                Assert.Equal(1, exit2);
                Assert.True(File.Exists(explicitFile));
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
        public void FieldOffsetDiff_NestedTypes_AreIncludedAndCompared()
        {
            var leftModel = new JsonDumpModel
            {
                types = new List<JsonDumpType>
                {
                    new JsonDumpType
                    {
                        fullName = "OuterClass",
                        size = "0x40",
                        nested = new List<JsonDumpType>
                        {
                            new JsonDumpType
                            {
                                fullName = "OuterClass.NestedStruct",
                                size = "0x10",
                                fields = new List<JsonDumpField>
                                {
                                    new() { name = "innerVal", type = "int", offset = "0x0" }
                                }
                            }
                        }
                    }
                }
            };

            var rightModel = new JsonDumpModel
            {
                types = new List<JsonDumpType>
                {
                    new JsonDumpType
                    {
                        fullName = "OuterClass",
                        size = "0x40",
                        nested = new List<JsonDumpType>
                        {
                            new JsonDumpType
                            {
                                fullName = "OuterClass.NestedStruct",
                                size = "0x10",
                                fields = new List<JsonDumpField>
                                {
                                    new() { name = "innerVal", type = "int", offset = "0x4" } // shifted from 0x0 to 0x4
                                }
                            }
                        }
                    }
                }
            };

            var report = FieldOffsetDiff.Compare(leftModel, rightModel);

            Assert.Single(report.structs);
            Assert.Equal("OuterClass.NestedStruct", report.structs[0].@struct);
            Assert.Single(report.structs[0].fields);
            Assert.Equal("innerVal", report.structs[0].fields[0].name);
            Assert.Equal("0x0", report.structs[0].fields[0].left);
            Assert.Equal("0x4", report.structs[0].fields[0].right);
            Assert.Equal(FieldOffsetDriftType.Shifted, report.structs[0].fields[0].drift);
            Assert.Equal(FieldOffsetSeverity.BreaksExternalReaders, report.structs[0].fields[0].severity);
        }

        [Fact]
        public void FieldOffsetDiff_StaticFields_AreIgnoredInStructMemoryLayout()
        {
            var leftModel = new JsonDumpModel
            {
                types = new List<JsonDumpType>
                {
                    new JsonDumpType
                    {
                        fullName = "Game.Settings",
                        size = "0x20",
                        fields = new List<JsonDumpField>
                        {
                            new() { name = "instanceVal", type = "int", offset = "0x10", isStatic = false },
                            new() { name = "staticCounter", type = "int", offset = "0x0", isStatic = true }
                        }
                    }
                }
            };

            var rightModel = new JsonDumpModel
            {
                types = new List<JsonDumpType>
                {
                    new JsonDumpType
                    {
                        fullName = "Game.Settings",
                        size = "0x20",
                        fields = new List<JsonDumpField>
                        {
                            new() { name = "instanceVal", type = "int", offset = "0x10", isStatic = false },
                            new() { name = "staticCounter", type = "int", offset = "0x100", isStatic = true } // static offset changed
                        }
                    }
                }
            };

            var report = FieldOffsetDiff.Compare(leftModel, rightModel);

            // Static field offset changes do NOT affect instance struct layout stability -> absent from report
            Assert.False(report.HasDrift);
            Assert.Empty(report.structs);
        }

        [Fact]
        public void FieldOffsetDiff_HexNumberEquivalence_RecognizesIdenticalOffsets()
        {
            var leftModel = new JsonDumpModel
            {
                types = new List<JsonDumpType>
                {
                    new JsonDumpType
                    {
                        fullName = "Game.Entity",
                        size = "0x0120",
                        fields = new List<JsonDumpField>
                        {
                            new() { name = "f", type = "int", offset = "0x010" }
                        }
                    }
                }
            };

            var rightModel = new JsonDumpModel
            {
                types = new List<JsonDumpType>
                {
                    new JsonDumpType
                    {
                        fullName = "Game.Entity",
                        size = "0x120",
                        fields = new List<JsonDumpField>
                        {
                            new() { name = "f", type = "int", offset = "0x10" }
                        }
                    }
                }
            };

            var report = FieldOffsetDiff.Compare(leftModel, rightModel);

            // Both size and offset are numerically equivalent -> absent from report
            Assert.False(report.HasDrift);
            Assert.Empty(report.structs);
        }
    }
}
