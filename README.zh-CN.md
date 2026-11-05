# Il2CppDumper

[![Build status](https://ci.appveyor.com/api/projects/status/anhqw33vcpmp8ofa?svg=true)](https://ci.appveyor.com/project/Perfare/il2cppdumper/branch/master/artifacts)
[![CI](https://github.com/silvachaves241/Il2CppDumper/actions/workflows/dotnet.yml/badge.svg)](https://github.com/silvachaves241/Il2CppDumper/actions/workflows/dotnet.yml)

Unity il2cpp逆向工程

## 关于此 Fork

此 Fork 针对 Unity/IL2CPP 高版本 metadata 做了实验性适配，包括 Unity 6000.x 使用的 v35/v38/v39。当前主要改动包括高版本 metadata 解析、方法地址恢复、DummyDll 生成修复，以及高版本 `il2cpp.h` 生成。

高版本适配只在少量游戏上做过自测。不同 Unity 版本、平台、项目配置和保护方式都会影响 dump 结果。如果遇到无法 dump 的游戏，需要自行调试并修改源码适配。本项目不承诺对每个游戏或每个保护壳做一一适配。

## 定位

本 Fork 面向逆向工程工作——包括上游明确不欢迎的用例。这是有意为之。

**在范围内：**

* IL2CPP 应用的取证分析与安全研究
* 对所分析游戏的作弊开发与反作弊研究
* 保护与混淆研究（metadata 混淆属于受支持的研究目标，不拒绝处理）
* 协议与客户端重建、模组开发、互操作
* 基于生成产物的自动化与工具链（`dump.cs`、`dump.json`、`il2cpp.h`、DummyDll、`script.json`）

功能请求、修复与改进应围绕这些工作流。询问如何遵守某游戏 EULA 的 issue 不在范围内；关于解析更难目标的 issue 在范围内。

## 法律声明

基于上游的 MIT 许可证。责任在你，司法辖区在你，目标对象也在你。本文不授予违反合同或法律的权利——那是你自己权衡的事。

## 功能

* 还原DLL文件（不包含代码），可用于提取`MonoBehaviour`和`MonoScript`
* 支持ELF, ELF64, Mach-O, PE, NSO和WASM格式
* 支持 Unity 5.3 - 2022.2，并实验性适配 Unity 高版本 metadata（如 v35/v38/v39，Unity 6000.x）
* 生成IDA和Ghidra的脚本，帮助IDA和Ghidra更好的分析il2cpp文件
* 生成结构体头文件
* 支持从内存dump的`libil2cpp.so`文件以绕过保护
* 支持绕过简单的PE保护

## 使用说明

直接运行Il2CppDumper.exe并依次选择il2cpp的可执行文件和global-metadata.dat文件，然后根据提示输入相应信息。

程序运行完成后将在当前运行目录下生成输出文件

### 命令行

```
Il2CppDumper.exe <executable-file> <global-metadata> <output-directory>
```

### 输出文件

#### DummyDll

文件夹，包含所有还原的DLL文件

使用[dnSpy](https://github.com/0xd4d/dnSpy)，[ILSpy](https://github.com/icsharpcode/ILSpy)或者其他.Net反编译工具即可查看具体信息

可用于提取Unity的`MonoBehaviour`和`MonoScript`，适用于[UtinyRipper](https://github.com/mafaca/UtinyRipper)或者[UABE](https://7daystodie.com/forums/showthread.php?22675-Unity-Assets-Bundle-Extractor)等

#### ida.py

用于IDA

#### ida_with_struct.py

用于IDA, 读取il2cpp.h文件并在IDA中应用结构信息

#### il2cpp.h

包含结构体的头文件

#### ghidra.py

用于Ghidra

#### Il2CppBinaryNinja

用于BinaryNinja

#### ghidra_wasm.py

用于Ghidra, 和[ghidra-wasm-plugin](https://github.com/nneonneo/ghidra-wasm-plugin)一起工作

#### script.json

用于IDA和Ghidra脚本

#### stringliteral.json

包含由 `StructGenerator` 生成的所有 stringLiteral 引用信息（`value`、`address`）。保留二进制文件中的所有内存引用和调用点地址。

**Português:** Contém todas as referências de uso de stringLiteral emitidas pelo `StructGenerator` (`value`, `address`). Preserva todas as referências de memória e endereços de chamada no binário.

#### stringliteral-indexed.json

包含由 `StructGenerator` 生成的完整 stringLiteral 取证结构化表（`index`、`value`、`dataIndex`、`length`）。提供 `global-metadata.dat` 中每个字面量的严格顺序 1 对 1 映射表（包含未被引用的字面量和末尾条目）。

**取证用途（Forensic usage）：** 在逆向分析二进制文件或反编译 IL 中通过索引引用字符串字面量的调用点（例如 `GetStringLiteralFromIndex(n)`）时，可以通过匹配 `index = n` 直接定位解析。`dataIndex` 与 `length` 字段提供了在 metadata 字符串字面量数据区段内的精确相对字节偏移与长度，便于与 `global-metadata.dat` 原始字节进行比对验证。若需在混淆游戏版本或补丁更新之间比对字符串表，可通过 `index` 关联条目进行 diff（例如 `jq '.[] | {index, value}' stringliteral-indexed.json`）。

**Português:** Tabela forense completa de literais de string emitida pelo `StructGenerator` como JSON estruturado (`index`, `value`, `dataIndex`, `length`). Fornece uma tabela estritamente sequencial e 1-para-1 de cada literal no `global-metadata.dat` (incluindo literais sem referência e a última entrada).
**Uso forense:** Ao analisar binários ou chamadas de IL descompiladas que referenciam literais de string por índice (ex.: `GetStringLiteralFromIndex(n)`), as entradas podem ser resolvidas diretamente buscando `index = n`. Os campos `dataIndex` e `length` fornecem deslocamentos e comprimentos exatos em bytes dentro da seção de dados de literais para correlação direta com o `global-metadata.dat`. Para comparar tabelas de strings entre versões ofuscadas ou atualizações do jogo, correlacione as entradas por `index` (ex.: `jq '.[] | {index, value}' stringliteral-indexed.json`).

#### dump-metadata.json

每次运行开始时（无论成功或失败）生成的溯源信息文件（provenance sidecar），记录 `tool`（`name`、`version`、`commit`）、`inputs`（`il2cpp` 和 `metadata` 的解析路径 `path`、字节长度 `length` 与流式计算的 `sha256`）、实际检测到的 `metadataVersion`、生效的 `config` 配置以及 `generatedAtUtc`（ISO-8601）。

**Português:** Arquivo lateral de proveniência (`dump-metadata.json`) gravado no início de cada execução (sucesso ou falha), registrando `tool` (`name`, `version`, `commit`), `inputs` (`il2cpp` e `metadata` com `path` resolvido, `length` e `sha256` em streaming), `metadataVersion` resolvida, `config` efetiva e `generatedAtUtc` (ISO-8601).

#### dump.json

机器可读的结构化转储输出（`GenerateJsonDump`），以 JSON 格式捕获与 `dump.cs` 相同的结构化信息（`types`、`fields`、`properties`、`methods`、`enums`、`nested`）。类型、字段、属性与方法均保留声明的 `attributes`（包括标记类的 `[Serializable]`）。

**Português:** Saída de dump legível por máquina (`dump.json`, chave `GenerateJsonDump`), capturando as mesmas informações estruturadas de `dump.cs` em formato JSON (`types`, `fields`, `properties`, `methods`, `enums`, `nested`). Tipos, campos, propriedades e métodos preservam `attributes` declarados (incluindo `[Serializable]` em tipos anotados).

### 关于config.json

* `DumpMethod`，`DumpField`，`DumpProperty`，`DumpAttribute`，`DumpFieldOffset`, `DumpMethodOffset`, `DumpTypeDefIndex`
  * 是否在dump.cs输出相应的内容（`DumpProperty` 和 `DumpAttribute` 默认值为 `true`）

* `GenerateDummyDll`，`GenerateStruct`，`GenerateJsonDump`
  * 是否生成这些内容（`GenerateStruct` 控制 `StructGenerator` 输出的内容：`script.json`、`stringliteral.json`、`stringliteral-indexed.json` 和 `il2cpp.h`；`GenerateJsonDump` 控制 `dump.json` 输出，默认值为 `false`）

* `DummyDllAddToken`
  * 是否在DummyDll中添加token

* `RequireAnyKey`
  * 在程序结束时是否需要按键退出

* `ForceIl2CppVersion`，`ForceVersion`  
  * 当ForceIl2CppVersion为`true`时，程序将根据ForceVersion指定的版本读取il2cpp的可执行文件（Metadata仍然使用header里的版本），在部分低版本的il2cpp中可能会用到（比如安卓20版本下，你可能需要设置ForceVersion为16程序才能正常工作）

* `ForceDump`
  * 强制将文件视为dump文件

* `NoRedirectedPointer`
  * 将dump文件中的指针视为未重定向的, 从某些设备dump出的文件需要设置该项为`true`

## 常见问题

#### `ERROR: Metadata file supplied is not valid metadata file.`

global-metadata.dat已被加密。加密/混淆的 metadata 在本项目中属于受支持的研究目标，但不承诺对具体游戏逐一适配——请自行调试并修改解析器。

如果你的文件是`libil2cpp.so`并且你拥有一台已root的安卓手机，你可以尝试我的另一个项目[Zygisk-Il2CppDumper](https://github.com/Perfare/Zygisk-Il2CppDumper)，它能够无视global-metadata.dat加密

#### `ERROR: Can't use auto mode to process file, try manual mode.`

请注意PC平台的可执行文件是`GameAssembly.dll`或者`*Assembly.dll`

如果是 Unity 高版本或带保护的构建，请自行定位失败的解析路径并修改源码适配。本项目不承诺对具体游戏逐一适配。

#### `ERROR: This file may be protected.`

Il2CppDumper检测到可执行文件已被保护，使用`GameGuardian`从游戏内存中dump `libil2cpp.so`，然后使用Il2CppDumper载入按提示操作，可绕过大部分保护

如果你拥有一台已root的安卓手机，你可以尝试我的另一个项目[Zygisk-Il2CppDumper](https://github.com/Perfare/Zygisk-Il2CppDumper)，它能够绕过几乎所有保护

## 感谢

- Jumboperson - [Il2CppDumper](https://github.com/Jumboperson/Il2CppDumper)
