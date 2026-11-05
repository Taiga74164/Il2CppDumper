#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Il2CppDumper.Diff
{
    public static class DumpDiffer
    {
        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public static int Execute(string leftPath, string rightPath, string outputPath, TextWriter? log = null)
        {
            try
            {
                var result = Compare(leftPath, rightPath);

                string targetFile;
                if (outputPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    targetFile = Path.GetFullPath(outputPath);
                }
                else
                {
                    targetFile = Path.Combine(Path.GetFullPath(outputPath), "dump-diff.json");
                }

                var parentDir = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                {
                    Directory.CreateDirectory(parentDir);
                }

                var json = JsonSerializer.Serialize(result, JsonOptions);
                File.WriteAllText(targetFile, json, new UTF8Encoding(false));

                if (result.HasDifferences)
                {
                    log?.WriteLine($"Differences found: {result.summary.addedTypes} added types, {result.summary.removedTypes} removed types, {result.types.changed.Count} changed types, {result.summary.addressChanges} address changes, {result.summary.valueChanges} literal value changes.");
                    log?.WriteLine($"Diff written to: {targetFile}");
                    return 1;
                }
                else
                {
                    log?.WriteLine("No differences found.");
                    log?.WriteLine($"Diff written to: {targetFile}");
                    return 0;
                }
            }
            catch (Exception ex)
            {
                log?.WriteLine($"ERROR: {ex.Message}");
                return 2;
            }
        }

        public static DumpDiffResult Compare(string leftPath, string rightPath)
        {
            var leftArtifacts = ResolveArtifacts(leftPath, "left");
            var rightArtifacts = ResolveArtifacts(rightPath, "right");

            var result = new DumpDiffResult
            {
                left = leftArtifacts.Endpoint,
                right = rightArtifacts.Endpoint
            };

            // 1. Compare Types
            var leftTypes = leftArtifacts.Types;
            var rightTypes = rightArtifacts.Types;

            var leftTypeNames = leftTypes.Keys.ToHashSet(StringComparer.Ordinal);
            var rightTypeNames = rightTypes.Keys.ToHashSet(StringComparer.Ordinal);

            result.types.added = rightTypeNames.Except(leftTypeNames).OrderBy(x => x, StringComparer.Ordinal).ToList();
            result.types.removed = leftTypeNames.Except(rightTypeNames).OrderBy(x => x, StringComparer.Ordinal).ToList();

            var commonTypeNames = leftTypeNames.Intersect(rightTypeNames).OrderBy(x => x, StringComparer.Ordinal).ToList();
            foreach (var typeName in commonTypeNames)
            {
                var leftType = leftTypes[typeName];
                var rightType = rightTypes[typeName];

                // Compare fields (offset is passthrough only; only type change triggers changedField)
                var leftFields = (leftType.fields ?? new List<JsonDumpField>())
                    .GroupBy(f => f.name)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
                var rightFields = (rightType.fields ?? new List<JsonDumpField>())
                    .GroupBy(f => f.name)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

                var addedFields = rightFields.Keys.Except(leftFields.Keys).OrderBy(x => x, StringComparer.Ordinal).ToList();
                var removedFields = leftFields.Keys.Except(rightFields.Keys).OrderBy(x => x, StringComparer.Ordinal).ToList();
                var commonFields = leftFields.Keys.Intersect(rightFields.Keys).OrderBy(x => x, StringComparer.Ordinal);

                var changedFields = new List<DumpDiffFieldChange>();
                foreach (var fName in commonFields)
                {
                    var lf = leftFields[fName];
                    var rf = rightFields[fName];
                    if (lf.type != rf.type)
                    {
                        changedFields.Add(new DumpDiffFieldChange
                        {
                            field = fName,
                            left = new DumpDiffFieldDetails { type = lf.type, offset = lf.offset },
                            right = new DumpDiffFieldDetails { type = rf.type, offset = rf.offset }
                        });
                    }
                }

                // Compare methods
                var leftMethods = (leftType.methods ?? new List<JsonDumpMethod>())
                    .GroupBy(m => m.name)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
                var rightMethods = (rightType.methods ?? new List<JsonDumpMethod>())
                    .GroupBy(m => m.name)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

                var addedMethods = rightMethods.Keys.Except(leftMethods.Keys).ToList();
                var removedMethods = leftMethods.Keys.Except(rightMethods.Keys).ToList();
                var commonMethods = leftMethods.Keys.Intersect(rightMethods.Keys).OrderBy(x => x, StringComparer.Ordinal);

                var changedMethods = new List<DumpDiffMethodChange>();
                foreach (var mName in commonMethods)
                {
                    var lList = leftMethods[mName];
                    var rList = rightMethods[mName];
                    if (lList.Count == 1 && rList.Count == 1)
                    {
                        var lm = lList[0];
                        var rm = rList[0];
                        var lp = FormatParams(lm);
                        var rp = FormatParams(rm);
                        if (lm.returnType != rm.returnType || lp != rp)
                        {
                            changedMethods.Add(new DumpDiffMethodChange
                            {
                                method = mName,
                                left = new DumpDiffMethodDetails { returnType = lm.returnType, parameters = lp },
                                right = new DumpDiffMethodDetails { returnType = rm.returnType, parameters = rp }
                            });
                        }
                    }
                    else
                    {
                        // Overloads: compare by formatted signature counts, never throw on duplicate signatures
                        var lSigList = lList.Select(FormatSignature).ToList();
                        var rSigList = rList.Select(FormatSignature).ToList();

                        var lSigCounts = new Dictionary<string, int>(StringComparer.Ordinal);
                        foreach (var s in lSigList)
                            lSigCounts[s] = lSigCounts.GetValueOrDefault(s, 0) + 1;

                        var rSigCounts = new Dictionary<string, int>(StringComparer.Ordinal);
                        foreach (var s in rSigList)
                            rSigCounts[s] = rSigCounts.GetValueOrDefault(s, 0) + 1;

                        foreach (var (sig, rCount) in rSigCounts)
                        {
                            var lCount = lSigCounts.GetValueOrDefault(sig, 0);
                            for (var c = 0; c < rCount - lCount; c++)
                            {
                                addedMethods.Add(sig);
                            }
                        }

                        foreach (var (sig, lCount) in lSigCounts)
                        {
                            var rCount = rSigCounts.GetValueOrDefault(sig, 0);
                            for (var c = 0; c < lCount - rCount; c++)
                            {
                                removedMethods.Add(sig);
                            }
                        }
                    }
                }

                addedMethods.Sort(StringComparer.Ordinal);
                removedMethods.Sort(StringComparer.Ordinal);
                changedMethods = changedMethods.OrderBy(x => x.method, StringComparer.Ordinal).ToList();

                if (addedFields.Count > 0 || removedFields.Count > 0 || changedFields.Count > 0 ||
                    addedMethods.Count > 0 || removedMethods.Count > 0 || changedMethods.Count > 0)
                {
                    result.types.changed.Add(new DumpDiffTypeChange
                    {
                        type = typeName,
                        fields = new DumpDiffFieldChanges
                        {
                            added = addedFields,
                            removed = removedFields,
                            changed = changedFields
                        },
                        methods = new DumpDiffMethodChanges
                        {
                            added = addedMethods,
                            removed = removedMethods,
                            changed = changedMethods
                        }
                    });
                }
            }

            // 2. Compare Method Addresses
            if (leftArtifacts.ScriptMethods != null && rightArtifacts.ScriptMethods != null)
            {
                var addressChanges = new List<DumpDiffAddressChange>();

                var leftGrouped = leftArtifacts.ScriptMethods
                    .GroupBy(m => (m.TypeName, m.MethodName))
                    .ToDictionary(g => g.Key, g => g.ToList());

                var rightGrouped = rightArtifacts.ScriptMethods
                    .GroupBy(m => (m.TypeName, m.MethodName))
                    .ToDictionary(g => g.Key, g => g.ToList());

                var commonKeys = leftGrouped.Keys.Intersect(rightGrouped.Keys)
                    .OrderBy(k => k.TypeName, StringComparer.Ordinal)
                    .ThenBy(k => k.MethodName, StringComparer.Ordinal);

                foreach (var key in commonKeys)
                {
                    var leftEntries = leftGrouped[key];
                    var rightEntries = rightGrouped[key];

                    if (leftEntries.Count == 1 && rightEntries.Count == 1)
                    {
                        var lm = leftEntries[0];
                        var rm = rightEntries[0];
                        if (lm.Address != rm.Address)
                        {
                            addressChanges.Add(new DumpDiffAddressChange
                            {
                                type = key.TypeName,
                                method = key.MethodName,
                                left = $"0x{lm.Address:X}",
                                right = $"0x{rm.Address:X}"
                            });
                        }
                    }
                    else
                    {
                        // Overloads: match identifiable overloads by Signature without guessing
                        var matchedRight = new HashSet<ScriptMethodEntry>();

                        foreach (var lm in leftEntries)
                        {
                            if (!string.IsNullOrEmpty(lm.Signature))
                            {
                                var candidates = rightEntries
                                    .Where(rm => !matchedRight.Contains(rm) && rm.Signature == lm.Signature)
                                    .ToList();

                                if (candidates.Count == 1)
                                {
                                    var rm = candidates[0];
                                    matchedRight.Add(rm);
                                    if (lm.Address != rm.Address)
                                    {
                                        addressChanges.Add(new DumpDiffAddressChange
                                        {
                                            type = key.TypeName,
                                            method = lm.Signature,
                                            left = $"0x{lm.Address:X}",
                                            right = $"0x{rm.Address:X}"
                                        });
                                    }
                                }
                                else if (candidates.Count > 1)
                                {
                                    var sameAddr = candidates.FirstOrDefault(c => c.Address == lm.Address);
                                    if (sameAddr != null)
                                    {
                                        matchedRight.Add(sameAddr);
                                    }
                                }
                            }
                        }
                    }
                }

                result.methods.addressChanged = addressChanges
                    .OrderBy(x => x.type, StringComparer.Ordinal)
                    .ThenBy(x => x.method, StringComparer.Ordinal)
                    .ToList();
                result.methods.note = null;
            }
            else
            {
                result.methods.addressChanged = null;
                if (leftArtifacts.ScriptMethods == null && rightArtifacts.ScriptMethods == null)
                {
                    result.methods.note = "Address comparison omitted: script.json missing on both left and right";
                }
                else if (leftArtifacts.ScriptMethods == null)
                {
                    result.methods.note = "Address comparison omitted: script.json missing on left";
                }
                else
                {
                    result.methods.note = "Address comparison omitted: script.json missing on right";
                }
            }

            // 3. Compare Literals
            if (leftArtifacts.Literals != null && rightArtifacts.Literals != null)
            {
                var leftLit = leftArtifacts.Literals;
                var rightLit = rightArtifacts.Literals;

                var addedLit = rightLit.Keys.Except(leftLit.Keys)
                    .OrderBy(x => x)
                    .Select(idx => new DumpDiffLiteralAdded { index = idx, value = rightLit[idx] })
                    .ToList();

                var changedLit = leftLit.Keys.Intersect(rightLit.Keys)
                    .OrderBy(x => x)
                    .Where(idx => leftLit[idx] != rightLit[idx])
                    .Select(idx => new DumpDiffLiteralChanged { index = idx, left = leftLit[idx], right = rightLit[idx] })
                    .ToList();

                result.literals.addedByIndex = addedLit;
                result.literals.valueChangedByIndex = changedLit;
            }
            else
            {
                result.literals.addedByIndex = new List<DumpDiffLiteralAdded>();
                result.literals.valueChangedByIndex = new List<DumpDiffLiteralChanged>();
                if (leftArtifacts.Literals == null && rightArtifacts.Literals == null)
                {
                    result.literals.note = "Literal comparison omitted: stringliteral-indexed.json missing on both left and right";
                }
                else if (leftArtifacts.Literals == null)
                {
                    result.literals.note = "Literal comparison omitted: stringliteral-indexed.json missing on left";
                }
                else
                {
                    result.literals.note = "Literal comparison omitted: stringliteral-indexed.json missing on right";
                }
            }

            // 4. Summary
            result.summary.addedTypes = result.types.added.Count;
            result.summary.removedTypes = result.types.removed.Count;
            result.summary.addressChanges = result.methods.addressChanged?.Count ?? 0;
            result.summary.valueChanges = result.literals.valueChangedByIndex.Count;

            return result;
        }

        private static string FormatParams(JsonDumpMethod m)
        {
            if (m.parameters == null || m.parameters.Count == 0)
                return string.Empty;
            return string.Join(", ", m.parameters.Select(p => $"{p.type} {p.name}".Trim()));
        }

        private static string FormatSignature(JsonDumpMethod m)
        {
            var pars = m.parameters != null ? string.Join(", ", m.parameters.Select(p => p.type)) : "";
            return $"{m.returnType} {m.name}({pars})";
        }

        private static string ComputeStreamSha256(string filePath)
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: false);
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            int bytesRead;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hasher.AppendData(buffer, 0, bytesRead);
            }
            return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        }

        public class ScriptMethodEntry
        {
            public string TypeName { get; set; } = string.Empty;
            public string MethodName { get; set; } = string.Empty;
            public string Signature { get; set; } = string.Empty;
            public ulong Address { get; set; }
        }

        private class ParsedArtifacts
        {
            public DumpDiffEndpoint Endpoint { get; set; } = new();
            public Dictionary<string, JsonDumpType> Types { get; set; } = new(StringComparer.Ordinal);
            public List<ScriptMethodEntry>? ScriptMethods { get; set; }
            public Dictionary<uint, string>? Literals { get; set; }
        }

        private static ParsedArtifacts ResolveArtifacts(string inputPath, string label)
        {
            string dir;
            string dumpJsonPath;

            if (Directory.Exists(inputPath))
            {
                dir = Path.GetFullPath(inputPath);
                dumpJsonPath = Path.Combine(dir, "dump.json");
            }
            else if (File.Exists(inputPath))
            {
                dumpJsonPath = Path.GetFullPath(inputPath);
                dir = Path.GetDirectoryName(dumpJsonPath)!;
            }
            else
            {
                throw new FileNotFoundException($"Input {label} path not found: {inputPath}");
            }

            if (!File.Exists(dumpJsonPath))
            {
                throw new FileNotFoundException($"dump.json not found in {label} path: {dumpJsonPath}");
            }

            var artifacts = new ParsedArtifacts();

            // 1. Endpoint Source and SHA-256
            var sidecarPath = Path.Combine(dir, "dump-metadata.json");
            var sidecarFound = false;
            if (File.Exists(sidecarPath))
            {
                try
                {
                    using var stream = File.OpenRead(sidecarPath);
                    using var doc = JsonDocument.Parse(stream);
                    if (doc.RootElement.TryGetProperty("inputs", out var inputs) &&
                        inputs.TryGetProperty("il2cpp", out var il2cpp))
                    {
                        var il2cppSha = il2cpp.TryGetProperty("sha256", out var shaEl) ? shaEl.GetString() : null;
                        var il2cppSource = il2cpp.TryGetProperty("path", out var pathEl) ? pathEl.GetString() : null;
                        if (!string.IsNullOrEmpty(il2cppSha))
                        {
                            artifacts.Endpoint.source = il2cppSource ?? dir;
                            artifacts.Endpoint.sha256 = il2cppSha;
                            sidecarFound = true;
                        }
                    }
                }
                catch
                {
                    // Fall back to computing dump.json hash
                }
            }

            if (!sidecarFound)
            {
                artifacts.Endpoint.source = dumpJsonPath;
                artifacts.Endpoint.sha256 = ComputeStreamSha256(dumpJsonPath);
            }

            // 2. Parse dump.json (required input, must have 'types' array)
            using (var stream = File.OpenRead(dumpJsonPath))
            {
                using var doc = JsonDocument.Parse(stream);
                if (!doc.RootElement.TryGetProperty("types", out var typesEl) ||
                    typesEl.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidDataException($"Invalid {label} dump.json: missing 'types' array property.");
                }

                JsonDumpModel? model;
                try
                {
                    model = JsonSerializer.Deserialize<JsonDumpModel>(doc.RootElement.GetRawText());
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException($"Failed to deserialize {label} dump.json: {ex.Message}", ex);
                }

                if (model == null || model.types == null)
                {
                    throw new InvalidDataException($"Invalid {label} dump.json: missing 'types' array property.");
                }

                void CollectType(JsonDumpType t)
                {
                    if (!string.IsNullOrEmpty(t.fullName))
                    {
                        artifacts.Types[t.fullName] = t;
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
            }

            // 3. Parse script.json (optional, but if present must be valid)
            var scriptPath = Path.Combine(dir, "script.json");
            if (File.Exists(scriptPath))
            {
                try
                {
                    using var stream = File.OpenRead(scriptPath);
                    using var doc = JsonDocument.Parse(stream);
                    if (!doc.RootElement.TryGetProperty("ScriptMethod", out var scriptMethods) ||
                        scriptMethods.ValueKind != JsonValueKind.Array)
                    {
                        throw new InvalidDataException($"Failed to parse {label} script.json: missing 'ScriptMethod' array.");
                    }

                    var list = new List<ScriptMethodEntry>();
                    foreach (var item in scriptMethods.EnumerateArray())
                    {
                        if (item.TryGetProperty("Name", out var nameEl) &&
                            item.TryGetProperty("Address", out var addrEl))
                        {
                            var name = nameEl.GetString() ?? "";
                            ulong addr = 0;
                            if (addrEl.ValueKind == JsonValueKind.Number)
                            {
                                addr = addrEl.GetUInt64();
                            }
                            else if (addrEl.ValueKind == JsonValueKind.String)
                            {
                                var str = addrEl.GetString();
                                if (!string.IsNullOrEmpty(str) && str.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                                    ulong.TryParse(str.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out addr);
                                else
                                    ulong.TryParse(str, out addr);
                            }

                            var sig = item.TryGetProperty("Signature", out var sigEl) ? (sigEl.GetString() ?? "") : "";

                            var sep = name.IndexOf("$$");
                            string typeName;
                            string methodName;
                            if (sep >= 0)
                            {
                                typeName = name.Substring(0, sep);
                                methodName = name.Substring(sep + 2);
                            }
                            else
                            {
                                typeName = "";
                                methodName = name;
                            }

                            list.Add(new ScriptMethodEntry
                            {
                                TypeName = typeName,
                                MethodName = methodName,
                                Signature = sig,
                                Address = addr
                            });
                        }
                    }
                    artifacts.ScriptMethods = list;
                }
                catch (Exception ex) when (!(ex is InvalidDataException))
                {
                    throw new InvalidDataException($"Failed to parse {label} script.json: {ex.Message}", ex);
                }
            }

            // 4. Parse literals (optional, only stringliteral-indexed.json; if present must be valid)
            var indexedLiteralsPath = Path.Combine(dir, "stringliteral-indexed.json");
            if (File.Exists(indexedLiteralsPath))
            {
                try
                {
                    using var stream = File.OpenRead(indexedLiteralsPath);
                    using var doc = JsonDocument.Parse(stream);
                    if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    {
                        throw new InvalidDataException($"Failed to parse {label} stringliteral-indexed.json: root must be an array.");
                    }

                    var map = new Dictionary<uint, string>();
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        if (!item.TryGetProperty("index", out var idxEl) || idxEl.ValueKind != JsonValueKind.Number)
                        {
                            throw new InvalidDataException($"Failed to parse {label} stringliteral-indexed.json: entry missing valid numeric 'index'.");
                        }
                        var val = item.TryGetProperty("value", out var valEl) ? (valEl.GetString() ?? "") : "";
                        map[idxEl.GetUInt32()] = val;
                    }
                    artifacts.Literals = map;
                }
                catch (Exception ex) when (!(ex is InvalidDataException))
                {
                    throw new InvalidDataException($"Failed to parse {label} stringliteral-indexed.json: {ex.Message}", ex);
                }
            }

            return artifacts;
        }
    }
}
