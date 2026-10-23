# 数据处理

这些服务可在独立脚本和录制脚本中调用。失败返回 `Err(ServiceError)`，可用 `isErr`、模式匹配或 `?` 处理。

## JSON

| 调用 | 结果 |
| --- | --- |
| `json.parse(text)` | `Result<any,ServiceError>` |
| `json.stringify(value, pretty = false)` | `Result<string,ServiceError>` |

支持空值、布尔值、有限数字、字符串、数组和对象。循环引用、函数、宿主类型、重复对象键和非有限数字会报错。JSON 数字遵循脚本的双精度数字精度；大整数标识请用字符串。最大嵌套 64 层，读写最多 100000 个值，输入输出最大 16 MiB。

## CSV

`csv.parse(text, delimiter = ",")` 返回 `Result<string[][],ServiceError>`；`csv.stringify(rows, delimiter = ",")` 返回 `Result<string,ServiceError>`。

第一行与其他行一样保留，可自行作为表头。支持引号、双引号转义、字段内换行、CRLF/LF/CR 行尾和空字段。写出时使用 CRLF。分隔符必须是单个字符，不能是引号、换行或空字符。空输入产生空表；空行是一个空字段，不支持零字段行。字段保持字符串，不会隐式推断数字。解析最多 100000 个字段，输入输出最大 16 MiB。

## 二进制

`bytes.fromText(text)`、`bytes.fromBase64(text)`、`bytes.fromHex(text)` 返回 `Result<Bytes,ServiceError>`。

`Bytes` 表示不可变的二进制数据，最大 16 MiB。

| 成员 | 说明 |
| --- | --- |
| `length` | 字节数 |
| `slice(start, end = length)` | `Result<Bytes,ServiceError>`；左闭右开，支持负索引；越界返回错误 |
| `text()` | `Result<string,ServiceError>`；严格 UTF-8 解码 |
| `toBase64()` | Base64 字符串 |
| `toHex()` | 小写十六进制字符串 |
| `sha256()` | 小写 SHA-256 摘要 |

`files.readBytes(path, maxBytes = 1048576)` 返回 `Result<Bytes,ServiceError>`，上限可增至 16 MiB。
`files.writeBytes(path, data, overwrite = false)` 原子写入文件，返回 `Result<string,ServiceError>`。

相对路径以脚本所在目录为基准，已有文件默认不会覆盖。处理图像和动画时，可直接将文件路径传给[媒体服务](services.md#图像与动画-api)。

可运行范例：[数据处理](../examples/data-processing.rgpreset)。
