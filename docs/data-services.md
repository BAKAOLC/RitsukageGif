# JSON、CSV 与字节数据

脚本可以解析文本、生成数据文件，以及在文本、Base64、十六进制和字节数据之间转换。

## 处理调用结果

解析和转换函数通常返回 `Result<值类型, ServiceError>`。先检查 `isOk`，再读取 `value`；失败时读取 `error.code`、`error.message` 和 `error.path`。

```rgscript
let parsed = json.parse('{"name":"demo","fps":20}');
if (parsed.isOk) {
    print(parsed.value);
} else {
    print(parsed.error.message);
}
```

## JSON

| 调用 | 返回值 | 用途 |
| --- | --- | --- |
| `json.parse(text)` | `Result<any, ServiceError>` | 将 JSON 文本转为脚本值 |
| `json.stringify(value, pretty = false)` | `Result<string, ServiceError>` | 将脚本值转为 JSON 文本 |

JSON 支持对象、数组、字符串、有限数值、布尔值和 `null`。函数等值不能转成 JSON，循环引用也会失败。

```rgscript
let text = json.stringify({name: "demo", sizes: [320, 640]}, true);
if (text.isOk) {
    print(text.value);
}
```

JSON 文本上限为 16 MiB，嵌套深度最多 64 层。

## CSV

| 调用 | 返回值 |
| --- | --- |
| `csv.parse(text, delimiter = ",")` | `Result<string[][], ServiceError>` |
| `csv.stringify(rows, delimiter = ",")` | `Result<string, ServiceError>` |

每行是一个字符串数组。解析不会自动把数字文本转换为数值。字段中的分隔符、双引号和换行可使用 CSV 双引号规则表示。

```rgscript
let encoded = csv.stringify([
    ["名称", "帧率"],
    ["示例, 一", "20"]
]);
if (encoded.isOk) {
    let decoded = csv.parse(encoded.value);
    if (decoded.isOk) {
        print(decoded.value);
    }
}
```

用 `"\t"` 作为分隔符可处理制表符分隔文本。分隔符必须是一个字符，不能是双引号、换行或空字符。CSV 文本上限为 16 MiB，解析最多接受 100000 个字段。

## 字节数据

| 调用 | 返回值 |
| --- | --- |
| `bytes.fromText(text)` | `Result<Bytes, ServiceError>` |
| `bytes.fromBase64(text)` | `Result<Bytes, ServiceError>` |
| `bytes.fromHex(text)` | `Result<Bytes, ServiceError>` |

`fromText` 使用 UTF-8。这三个转换函数生成的字节数据最多为 16 MiB；其他服务返回的 `Bytes` 按各自接口的限制处理。

取得 `Bytes` 后可以使用：

| 成员 | 返回值与含义 |
| --- | --- |
| `length` | 字节数 |
| `text()` | `Result<string, ServiceError>`，按 UTF-8 解码 |
| `toBase64()` | Base64 字符串 |
| `toHex()` | 小写十六进制字符串 |
| `sha256()` | 小写十六进制 SHA-256 摘要 |
| `slice(start, end)` | `Result<Bytes, ServiceError>`，截取字节范围 |

`slice` 包含起点、不包含终点；省略 `end` 表示截到末尾。负数索引从末尾计算，例如 `-1` 表示最后一个字节的位置。索引必须是有效范围内的整数，终点不能小于起点。

```rgscript
let encoded = bytes.fromText("hello");
if (encoded.isOk) {
    print(encoded.value.toHex());
    print(encoded.value.toBase64());
    let prefix = encoded.value.slice(0, 2);
    if (prefix.isOk) {
        let text = prefix.value.text();
        if (text.isOk) {
            print(text.value);
        }
    }
}
```

字节切片可能落在一个 UTF-8 字符的中间，此时 `text()` 会返回失败。读写字节文件见[文件与媒体服务](services.md)。
