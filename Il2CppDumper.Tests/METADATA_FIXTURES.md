# Synthetic Metadata Fixture Matrix & Specification

This document specifies the synthetic metadata fixture generator and test harness implemented for `Il2CppDumper.Tests` (resolving Issue #44).

## 1. Supported Metadata Families & Layout Unions

The synthetic metadata generator covers all supported metadata version families from v16 through v39 without checking in any real or copyrighted game binaries.

| Family | Metadata Version | Layout Union / Structural Features | Positive Fixture | Corruption Coverage | Parser Behavior |
|---|---|---|---|---|---|
| `V16` | 16 | Baseline legacy metadata header (~192 B); no `fieldRefs` or `metadataUsage`; RGCTX entries present | 1 class, 1 enum, 1 method, 3 fields, 1 string literal | 5 corruption types | Validated baseline load via `ReadClass<Il2CppGlobalMetadataHeader>` |
| `V21` | 21 | Header expanded to ~240 B; introduced `attributesInfo` (`Il2CppCustomAttributeTypeRange`) and `attributeTypes`; added CCW / GUID indices | 1 class, 1 enum, 1 method, 3 fields, 1 string literal | 5 corruption types | Validated attributes info boundaries and type ranges |
| `V24` | 24 | Header expanded to ~272 B; introduced `exportedTypeDefinitions`, image tokens, and assembly tokens; subversion checks | 1 class, 1 enum, 1 method, 3 fields, 1 string literal | 5 corruption types | Validated token checks, subversion disambiguation |
| `V27` | 27 | Header normalized to 256 B; removed legacy metadata usage; added Windows Runtime strings | 1 class, 1 enum, 1 method, 3 fields, 1 string literal | 5 corruption types | Cleaned metadata usages, winrt string table pointers |
| `V29` | 29 | Replaced custom attribute type ranges with `attributeDataOffset` and `attributeDataRangeOffset` | 1 class, 1 enum, 1 method, 3 fields, 1 string literal | 5 corruption types | Modern custom attribute data range resolution |
| `V31` | 31 | Method return parameter token introduced; 8-byte string literals (`length` + `dataIndex`) | 1 class, 1 enum, 1 method, 3 fields, 1 string literal | 5 corruption types | Explicit length string literal decoding |
| `V35` | 35 | 4-byte string literals (`dataIndex` only); string length inferred from adjacent literal or section end (#23) | 1 class, 1 enum, 1 method, 3 fields, 1 string literal | 5 corruption types | String literal extent bounds verification and safe rejection |
| `V38` | 38 | Section-based metadata architecture (`Il2CppSectionMetadata` × 31); compact variable index sizing (`ReadMetadataIndex`) | 1 class, 1 enum, 1 method, 3 fields, 1 string literal | 5 corruption types | Section-based dynamic reading and compact index sizing |
| `V39` | 39 | Dynamic `parameterIndexSize` derived from parameter count (`GetIndexSize`) | 1 class, 1 enum, 1 method, 3 fields, 1 string literal | 5 corruption types | Adaptive 1/2/4-byte parameter index size resolution |

---

## 2. Corruption Variants & Fault Injection

Every metadata family is exercised against five corruption variants to ensure robust, non-crashing parser boundaries:

| Corruption Variant | Injection Mechanism | Expected Parser Behavior |
|---|---|---|
| `TruncatedSection` | Stream truncated prematurely (e.g. 80 bytes) | Throws `EndOfStreamException` / `InvalidDataException` |
| `WrongMagic` | Header sanity set to `0xDEADBEEF` instead of `0xFAB11BAF` | Throws `InvalidDataException("not valid metadata file")` |
| `OutOfRangeVersion` | Version set to out-of-range value (`999`) | Throws `NotSupportedException` / `InvalidDataException` |
| `LiteralExtentPastSectionEnd` | Literal `dataIndex` or inferred length exceeds section bounds (scenario #23) | Rejects safely without throwing: `TryGetStringLiteral` returns `false` |
| `ZeroedRegistrationRecords` | Image definitions and type definitions counts/sizes set to zero | Safely parsed with empty arrays (`imageDefs.Length == 0`, `typeDefs.Length == 0`) |

---

## 3. Golden Outputs Regeneration

Golden decompiled outputs (`dump.cs` and `dump.json`) for each of the 9 positive fixtures are stored under `Il2CppDumper.Tests/Goldens/{family}/`.

To regenerate golden files when intentional output changes occur:

```bash
UPDATE_GOLDENS=1 dotnet test --filter "PositiveFixture_Decompilation_MatchesGoldenOutputs"
```

In standard CI test runs, `PositiveFixture_Decompilation_MatchesGoldenOutputs` asserts byte-identical output against these golden files to guarantee zero regressions.

---

## 4. Repo Hygiene & Performance Budget

- **Repo Hygiene Invariant**: No binary blobs (`.dat`, `.bin`, `.wasm`, `.so`, `.elf`, `.exe`, `.apk`) are tracked in the repository tree. All test inputs are synthesized deterministically in-memory at test time.
- **Suite Runtime Budget**: The entire corpus matrix (9 families × 6 variants = 54 fixtures generated, parsed, and decompiled) is asserted to execute under **5,000 ms** (measured execution time is typically ~250–350 ms).
