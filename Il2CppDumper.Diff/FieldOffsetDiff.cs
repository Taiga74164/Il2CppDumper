#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Il2CppDumper.Diff
{
    public static class FieldOffsetDiff
    {
        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public static int Execute(string leftPath, string rightPath, string outputPath, TextWriter? log = null)
        {
            try
            {
                var report = Compare(leftPath, rightPath);

                string targetFile;
                if (outputPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    targetFile = Path.GetFullPath(outputPath);
                }
                else
                {
                    targetFile = Path.Combine(Path.GetFullPath(outputPath), "field-offset-drift.json");
                }

                var parentDir = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                {
                    Directory.CreateDirectory(parentDir);
                }

                var json = JsonSerializer.Serialize(report, JsonOptions);
                File.WriteAllText(targetFile, json, new UTF8Encoding(false));

                if (report.HasDrift)
                {
                    var totalShifted = report.structs.Sum(s => s.verdict.shifted);
                    var totalRemoved = report.structs.Sum(s => s.verdict.removed);
                    var totalAdded = report.structs.Sum(s => s.verdict.added);
                    log?.WriteLine($"Field offset drift detected in {report.structs.Count} struct(s): {totalShifted} shifted, {totalRemoved} removed, {totalAdded} added.");
                    log?.WriteLine($"Report written to: {targetFile}");
                    return 1;
                }
                else
                {
                    log?.WriteLine("No field offset drift found.");
                    log?.WriteLine($"Report written to: {targetFile}");
                    return 0;
                }
            }
            catch (Exception ex)
            {
                log?.WriteLine($"ERROR: {ex.Message}");
                return 2;
            }
        }

        public static FieldOffsetDriftReport Compare(string leftPath, string rightPath)
        {
            var leftDumpPath = ResolveDumpJsonPath(leftPath, "left");
            var rightDumpPath = ResolveDumpJsonPath(rightPath, "right");

            var leftModel = LoadDumpModel(leftDumpPath, "left");
            var rightModel = LoadDumpModel(rightDumpPath, "right");

            return Compare(leftModel, rightModel);
        }

        public static FieldOffsetDriftReport Compare(JsonDumpModel leftModel, JsonDumpModel rightModel)
        {
            var leftTypes = CollectTypes(leftModel);
            var rightTypes = CollectTypes(rightModel);

            var allTypeNames = leftTypes.Keys
                .Union(rightTypes.Keys)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

            var driftedStructs = new List<FieldOffsetStructDrift>();

            foreach (var typeName in allTypeNames)
            {
                leftTypes.TryGetValue(typeName, out var leftType);
                rightTypes.TryGetValue(typeName, out var rightType);

                // Collect instance (non-static) fields
                var leftInstanceFields = (leftType?.fields ?? new List<JsonDumpField>())
                    .Where(f => !f.isStatic)
                    .GroupBy(f => f.name)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

                var rightInstanceFields = (rightType?.fields ?? new List<JsonDumpField>())
                    .Where(f => !f.isStatic)
                    .GroupBy(f => f.name)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

                // Calculate max offset in left instance fields for appended-at-end classification
                ulong? maxLeftOffset = null;
                foreach (var f in leftInstanceFields.Values)
                {
                    if (TryParseHexOffset(f.offset, out var off))
                    {
                        if (!maxLeftOffset.HasValue || off > maxLeftOffset.Value)
                        {
                            maxLeftOffset = off;
                        }
                    }
                }

                var driftedFields = new List<FieldOffsetFieldDrift>();

                // 1. Shifted & Unchanged (common fields)
                var commonFieldNames = leftInstanceFields.Keys
                    .Intersect(rightInstanceFields.Keys)
                    .OrderBy(x => x, StringComparer.Ordinal);

                foreach (var fName in commonFieldNames)
                {
                    var lf = leftInstanceFields[fName];
                    var rf = rightInstanceFields[fName];

                    var leftHasOffset = TryParseHexOffset(lf.offset, out var leftOff);
                    var rightHasOffset = TryParseHexOffset(rf.offset, out var rightOff);

                    if (leftHasOffset && rightHasOffset)
                    {
                        if (leftOff != rightOff)
                        {
                            driftedFields.Add(new FieldOffsetFieldDrift
                            {
                                name = fName,
                                left = lf.offset,
                                right = rf.offset,
                                drift = FieldOffsetDriftType.Shifted,
                                severity = FieldOffsetSeverity.BreaksExternalReaders
                            });
                        }
                        // else: unchanged -> omitted
                    }
                    else if (lf.offset != rf.offset)
                    {
                        driftedFields.Add(new FieldOffsetFieldDrift
                        {
                            name = fName,
                            left = lf.offset,
                            right = rf.offset,
                            drift = FieldOffsetDriftType.Shifted,
                            severity = FieldOffsetSeverity.BreaksExternalReaders
                        });
                    }
                }

                // 2. Removed fields (present in left, missing in right)
                var removedFieldNames = leftInstanceFields.Keys
                    .Except(rightInstanceFields.Keys)
                    .OrderBy(x => x, StringComparer.Ordinal);

                foreach (var fName in removedFieldNames)
                {
                    var lf = leftInstanceFields[fName];
                    driftedFields.Add(new FieldOffsetFieldDrift
                    {
                        name = fName,
                        left = lf.offset,
                        right = null,
                        drift = FieldOffsetDriftType.Removed,
                        severity = FieldOffsetSeverity.BreaksExternalReaders
                    });
                }

                // 3. Added fields (missing in left, present in right)
                var addedFieldNames = rightInstanceFields.Keys
                    .Except(leftInstanceFields.Keys)
                    .OrderBy(x => x, StringComparer.Ordinal);

                foreach (var fName in addedFieldNames)
                {
                    var rf = rightInstanceFields[fName];
                    var rightHasOffset = TryParseHexOffset(rf.offset, out var rightOff);

                    string severity;
                    if (maxLeftOffset.HasValue && rightHasOffset)
                    {
                        severity = rightOff > maxLeftOffset.Value
                            ? FieldOffsetSeverity.LeafConsumersMaySurvive
                            : FieldOffsetSeverity.BreaksExternalReaders;
                    }
                    else if (!maxLeftOffset.HasValue)
                    {
                        // No previous fields existed in left
                        severity = FieldOffsetSeverity.LeafConsumersMaySurvive;
                    }
                    else
                    {
                        severity = FieldOffsetSeverity.BreaksExternalReaders;
                    }

                    driftedFields.Add(new FieldOffsetFieldDrift
                    {
                        name = fName,
                        left = null,
                        right = rf.offset,
                        drift = FieldOffsetDriftType.Added,
                        severity = severity
                    });
                }

                // Deterministic ordering of drifted fields: by field name
                driftedFields = driftedFields
                    .OrderBy(f => f.name, StringComparer.Ordinal)
                    .ToList();

                // Struct size comparison
                var sizeObj = new FieldOffsetSize
                {
                    left = leftType?.size,
                    right = rightType?.size
                };

                var sizeChanged = !AreSizesEqual(leftType?.size, rightType?.size);

                // Include struct if there are drifted fields OR struct size changed
                if (driftedFields.Count > 0 || sizeChanged)
                {
                    driftedStructs.Add(new FieldOffsetStructDrift
                    {
                        @struct = typeName,
                        size = sizeObj,
                        fields = driftedFields,
                        verdict = new FieldOffsetVerdict
                        {
                            shifted = driftedFields.Count(f => f.drift == FieldOffsetDriftType.Shifted),
                            removed = driftedFields.Count(f => f.drift == FieldOffsetDriftType.Removed),
                            added = driftedFields.Count(f => f.drift == FieldOffsetDriftType.Added)
                        }
                    });
                }
            }

            // Deterministic ordering of structs: by struct name
            driftedStructs = driftedStructs
                .OrderBy(s => s.@struct, StringComparer.Ordinal)
                .ToList();

            return new FieldOffsetDriftReport
            {
                schemaVersion = 1,
                structs = driftedStructs
            };
        }

        private static string ResolveDumpJsonPath(string inputPath, string label)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                throw new ArgumentException($"Input {label} path is not specified.");
            }

            if (Directory.Exists(inputPath))
            {
                var dumpJsonPath = Path.Combine(Path.GetFullPath(inputPath), "dump.json");
                if (!File.Exists(dumpJsonPath))
                {
                    throw new FileNotFoundException($"dump.json not found in {label} directory: {dumpJsonPath}");
                }
                return dumpJsonPath;
            }

            if (File.Exists(inputPath))
            {
                return Path.GetFullPath(inputPath);
            }

            throw new FileNotFoundException($"Input {label} path not found: {inputPath}");
        }

        private static JsonDumpModel LoadDumpModel(string dumpJsonPath, string label)
        {
            using var stream = File.OpenRead(dumpJsonPath);
            using var doc = JsonDocument.Parse(stream);
            if (!doc.RootElement.TryGetProperty("types", out var typesEl) ||
                typesEl.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException($"Invalid {label} dump.json: missing 'types' array property.");
            }

            var model = JsonSerializer.Deserialize<JsonDumpModel>(doc.RootElement.GetRawText());
            if (model == null || model.types == null)
            {
                throw new InvalidDataException($"Invalid {label} dump.json: missing 'types' array property.");
            }
            return model;
        }

        private static Dictionary<string, JsonDumpType> CollectTypes(JsonDumpModel model)
        {
            var dict = new Dictionary<string, JsonDumpType>(StringComparer.Ordinal);

            void CollectType(JsonDumpType t)
            {
                if (!string.IsNullOrEmpty(t.fullName))
                {
                    dict.TryAdd(t.fullName, t);
                }
                if (t.nested != null)
                {
                    foreach (var child in t.nested)
                    {
                        CollectType(child);
                    }
                }
            }

            foreach (var t in model.types)
            {
                CollectType(t);
            }

            return dict;
        }

        private static bool TryParseHexOffset(string? offsetStr, out ulong offset)
        {
            offset = 0;
            if (string.IsNullOrWhiteSpace(offsetStr))
                return false;

            var s = offsetStr.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                s = s[2..];

            return ulong.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out offset);
        }

        private static bool AreSizesEqual(string? s1, string? s2)
        {
            if (s1 == null && s2 == null)
                return true;
            if (s1 == null || s2 == null)
                return false;

            if (TryParseHexOffset(s1, out var v1) && TryParseHexOffset(s2, out var v2))
            {
                return v1 == v2;
            }

            return string.Equals(s1.Trim(), s2.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
