# Il2CppDumper

[![Build status](https://ci.appveyor.com/api/projects/status/anhqw33vcpmp8ofa?svg=true)](https://ci.appveyor.com/project/Perfare/il2cppdumper/branch/master/artifacts)
[![CI](https://github.com/silvachaves241/Il2CppDumper/actions/workflows/dotnet.yml/badge.svg)](https://github.com/silvachaves241/Il2CppDumper/actions/workflows/dotnet.yml)

中文说明请戳[这里](README.zh-CN.md)

Unity il2cpp reverse engineer

## About This Fork

This fork includes experimental adaptations for newer Unity/IL2CPP metadata versions, including v35/v38/v39 used by Unity 6000.x. The changes improve metadata parsing, method address recovery, DummyDll generation, and `il2cpp.h` generation for newer versions.

The newer-version support has only been self-tested on a small number of games. IL2CPP layouts, metadata formats, and protections vary by Unity version, platform, and project. If a specific game cannot be dumped, you need to debug and modify the source code yourself. I do not provide one-by-one adaptation for every game or protected build.

## Positioning

This fork is maintained for reverse engineering work — including the use cases upstream frowns on. That is deliberate.

**In scope:**

* Forensic analysis and security research of IL2CPP applications
* Cheat development and anti-cheat research on games you analyze
* Protection and obfuscation research (metadata obfuscation included — it is on-topic, not rejected)
* Protocol and client reconstruction, mod development, interoperability
* Automation and tooling built on top of the generated outputs (`dump.cs`, `dump.json`, `il2cpp.h`, DummyDll, `script.json`)

Feature requests, fixes, and improvements should target these workflows. Issues asking how to comply with a game's EULA are out of scope; issues about parsing harder targets are in.

## Legal

MIT licensed, inherited from upstream. Your responsibility, your jurisdiction, your target. Nothing here grants a right to break a contract or a law — those are yours to weigh.

## Features

* Complete DLL restore (except code), can be used to extract `MonoBehaviour` and `MonoScript`
* Supports ELF, ELF64, Mach-O, PE, NSO and WASM format
* Supports Unity 5.3 - 2022.2, with experimental support for newer Unity metadata versions such as v35/v38/v39 (Unity 6000.x)
* Supports generate IDA, Ghidra and Binary Ninja scripts to help them better analyze il2cpp files
* Supports generate structures header file
* Supports Android memory dumped `libil2cpp.so` file to bypass protection
* Support bypassing simple PE protection

## Usage

Run `Il2CppDumper.exe` and choose the il2cpp executable file and `global-metadata.dat` file, then enter the information as prompted

The program will then generate all the output files in current working directory

### Command-line

```
Il2CppDumper.exe <executable-file> <global-metadata> <output-directory>
```

### Library usage

`Il2CppDumper.Core` provides headless, in-process invocation without requiring console interaction or process spawning:

```csharp
using Il2CppDumper;
using Il2CppDumper.Core;

var request = new DumpRequest
{
    ExecutablePath = "GameAssembly.dll",
    MetadataPath = "global-metadata.dat",
    OutputDirectory = "output",           // optional; null uses a temporary directory
    Config = new Config(),                // optional; null uses default documented settings
    Log = Console.Out                     // optional; null for silent operation
};

DumpResult result = Dumper.Dump(request);
if (result.Success)
{
    foreach (var (artifact, path) in result.Outputs)
    {
        Console.WriteLine($"{artifact} -> {path}");
    }
}
else
{
    Console.WriteLine($"Dump failed: {result.Error}");
}
```

> **Note on `RequireAnyKey`:** When invoking `Il2CppDumper.Core` in-process, `Config.RequireAnyKey` has **no effect**. The library never blocks or prompts for key presses during unattended or automated runs.

### Outputs

#### DummyDll

Folder, containing all restored dll files

Use [dnSpy](https://github.com/0xd4d/dnSpy), [ILSpy](https://github.com/icsharpcode/ILSpy) or other .Net decompiler tools to view

Can be used to extract Unity `MonoBehaviour` and `MonoScript`, for [UtinyRipper](https://github.com/mafaca/UtinyRipper), [UABE](https://7daystodie.com/forums/showthread.php?22675-Unity-Assets-Bundle-Extractor)

#### ida.py

For IDA

#### ida_with_struct.py

For IDA, read il2cpp.h file and apply structure information in IDA

#### il2cpp.h

structure information header file

#### ghidra.py

For Ghidra

#### Il2CppBinaryNinja

For BinaryNinja

#### ghidra_wasm.py

For Ghidra, work with [ghidra-wasm-plugin](https://github.com/nneonneo/ghidra-wasm-plugin)

#### script.json

For ida.py, ghidra.py and Il2CppBinaryNinja

#### stringliteral.json

Contains all stringLiteral reference usages emitted by `StructGenerator` (`value`, `address`). Preserves all memory references and call-site addresses in the binary.

**Português:** Contém todas as referências de uso de stringLiteral emitidas pelo `StructGenerator` (`value`, `address`). Preserva todas as referências de memória e endereços de chamada no binário.

#### stringliteral-indexed.json

Complete forensic string literal table emitted by `StructGenerator` as structured JSON (`index`, `value`, `dataIndex`, `length`). Provides a strictly sequential, 1-to-1 table of every literal in `global-metadata.dat` (including unreferenced literals and the final entry).

**Forensic usage:** When analyzing binaries or decompiled IL call sites that reference string literals by index (e.g., `GetStringLiteralFromIndex(n)`), entries can be resolved directly by querying `index = n`. The `dataIndex` and `length` fields provide exact section-relative byte offsets and sizes within the metadata string literal data section for byte-level validation against `global-metadata.dat`. To diff string tables across obfuscated game builds or patch updates, correlate entries by `index` (e.g. `jq '.[] | {index, value}' stringliteral-indexed.json`).

**Português:** Tabela forense completa de literais de string emitida pelo `StructGenerator` como JSON estruturado (`index`, `value`, `dataIndex`, `length`). Fornece uma tabela estritamente sequencial e 1-para-1 de cada literal no `global-metadata.dat` (incluindo literais sem referência e a última entrada).
**Uso forense:** Ao analisar binários ou chamadas de IL descompiladas que referenciam literais de string por índice (ex.: `GetStringLiteralFromIndex(n)`), as entradas podem ser resolvidas diretamente buscando `index = n`. Os campos `dataIndex` e `length` fornecem deslocamentos e comprimentos exatos em bytes dentro da seção de dados de literais para correlação direta com o `global-metadata.dat`. Para comparar tabelas de strings entre versões ofuscadas ou atualizações do jogo, correlacione as entradas por `index` (ex.: `jq '.[] | {index, value}' stringliteral-indexed.json`).

#### dump-metadata.json

Provenance sidecar written at the start of every run (success or failure), recording `tool` (`name`, `version`, `commit`), `inputs` (`il2cpp` and `metadata` resolved `path`, `length`, and streamed `sha256`), resolved `metadataVersion`, effective `config`, and `generatedAtUtc` (ISO-8601).

**Português:** Arquivo lateral de proveniência (`dump-metadata.json`) gravado no início de cada execução (sucesso ou falha), registrando `tool` (`name`, `version`, `commit`), `inputs` (`il2cpp` e `metadata` com `path` resolvido, `length` e `sha256` em streaming), `metadataVersion` resolvida, `config` efetiva e `generatedAtUtc` (ISO-8601).

#### dump.json

Machine-readable dump output (`GenerateJsonDump`) capturing the same structured information as `dump.cs` in JSON format (`types`, `fields`, `properties`, `methods`, `enums`, `nested`). Types, fields, properties, and methods preserve declared `attributes` (including `[Serializable]` on decorated types).

**Português:** Saída de dump legível por máquina (`dump.json`, chave `GenerateJsonDump`), capturando as mesmas informações estruturadas de `dump.cs` em formato JSON (`types`, `fields`, `properties`, `methods`, `enums`, `nested`). Tipos, campos, propriedades e métodos preservam `attributes` declarados (incluindo `[Serializable]` em tipos anotados).

### Configuration

All the configuration options are located in `config.json`

Available options:

* `DumpMethod`, `DumpField`, `DumpProperty`, `DumpAttribute`, `DumpFieldOffset`, `DumpMethodOffset`, `DumpTypeDefIndex`
  * Whether to output these information to dump.cs (`DumpProperty` and `DumpAttribute` default to `true`)

* `GenerateDummyDll`, `GenerateStruct`, `GenerateJsonDump`
  * Whether to generate these things (`GenerateStruct` controls `StructGenerator` output: `script.json`, `stringliteral.json`, `stringliteral-indexed.json`, and `il2cpp.h`; `GenerateJsonDump` controls `dump.json` output, defaults to `false`)

* `DummyDllAddToken`
  * Whether to add token in DummyDll

* `RequireAnyKey`
  * Whether to press any key to exit at the end

* `ForceIl2CppVersion`, `ForceVersion`
  * If `ForceIl2CppVersion` is `true`, the program will use the version number specified in `ForceVersion` to choose parser for il2cpp binaries (does not affect the choice of metadata parser). This may be useful on some older il2cpp version (e.g. the program may need to use v16 parser on il2cpp v20 (Android) binaries in order to work properly)

* `ForceDump`
  * Force files to be treated as dumped

* `NoRedirectedPointer`
  * Treat pointers in dumped files as unredirected, This option needs to be `true` for files dumped from some devices

## Common errors

#### `ERROR: Metadata file supplied is not valid metadata file.`  

Make sure you choose the correct file. Games may obfuscate this file for content protection purposes. Obfuscated metadata is a supported research target here, but per-game adaptation is not guaranteed — expect to debug and patch the parser yourself.

If your file is `libil2cpp.so` and you have a rooted Android phone, you can try my other project [Zygisk-Il2CppDumper](https://github.com/Perfare/Zygisk-Il2CppDumper), it can bypass this protection.

#### `ERROR: Can't use auto mode to process file, try manual mode.`

Please note that the executable file for the PC platform is `GameAssembly.dll` or `*Assembly.dll`

For newer Unity versions or protected builds, please investigate the failing parser path and patch the source locally. Per-game adaptation is not guaranteed.

#### `ERROR: This file may be protected.`

Il2CppDumper detected that the executable file has been protected, use `GameGuardian` to dump `libil2cpp.so` from the game memory, then use Il2CppDumper to load and follow the prompts, can bypass most protections.

If you have a rooted Android phone, you can try my other project [Zygisk-Il2CppDumper](https://github.com/Perfare/Zygisk-Il2CppDumper), it can bypass almost all protections.

## Credits

- Jumboperson - [Il2CppDumper](https://github.com/Jumboperson/Il2CppDumper)
