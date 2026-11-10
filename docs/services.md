# 文件与媒体服务

这些 API 可用于读写数据文件、查询动图大小、转换格式和重新压缩。相对路径以入口预设文件所在目录为基准，在导入的模块中调用时也相同。

使用下面的媒体示例前，将要处理的动图放在脚本目录，命名为 `input.gif`；也可以把示例中的路径改为自己的文件路径。

## 调用结果

除 `media.processMany` 外，下面的函数均返回 `Result<值类型, ServiceError>`。成功后读取 `value`；失败后读取 `error`，其中包含 `code`、`message` 和 `path`。

```rgscript
let result = files.exists("input.gif");
if (result.isOk) {
    print(result.value);
} else {
    print(result.error.message);
}
```

## 文件与目录

| 调用 | 成功值 |
| --- | --- |
| `files.exists(path)` | 路径是否存在，布尔值 |
| `files.info(path)` | `FileInfo` |
| `files.list(path)` | 当前目录的 `FileInfo[]`，不递归 |
| `files.createDirectory(path)` | 创建后的目录绝对路径 |
| `files.readText(path, maxBytes = 1048576)` | 文本 |
| `files.writeText(path, text, overwrite = false)` | 输出文件绝对路径 |
| `files.readBytes(path, maxBytes = 1048576)` | `Bytes` |
| `files.writeBytes(path, data, overwrite = false)` | 输出文件绝对路径 |
| `files.copy(source, destination, overwrite = false)` | 目标绝对路径 |
| `files.move(source, destination, overwrite = false)` | 目标绝对路径 |

`FileInfo` 包含 `path`、`name`、`extension`、`size`、`isDirectory` 和 `modifiedUtc`。`size` 单位为字节，目录为 0；`modifiedUtc` 是 UTC 时间字符串。

文本默认按 UTF-8 读取，也会识别文件中的编码标记；写入为不带 BOM 的 UTF-8。读取上限可以设为 1～16777216 字节，写入文本最多 16 MiB。目录列表最多返回 10000 项。

写文件前应确保父目录存在；同名文件默认不覆盖。

```rgscript
let directory = files.createDirectory("output");
if (directory.isOk) {
    let saved = files.writeText("output/note.txt", "录制完成", true);
    if (saved.isOk) {
        print(saved.value);
    } else {
        print(saved.error.message);
    }
}
```

## 路径

下面的函数均返回 `Result<string, ServiceError>`。

| 调用 | 用途 |
| --- | --- |
| `paths.join(part1, part2, ...)` | 拼接路径 |
| `paths.absolute(path)` | 转为绝对路径 |
| `paths.directory(path)` | 取得父目录部分 |
| `paths.name(path)` | 取得文件名，含扩展名 |
| `paths.extension(path)` | 取得扩展名，含开头的点 |

## 查看媒体信息

`media.inspect(path)` 返回 `Result<MediaInfo, ServiceError>`。

`MediaInfo` 包含 `path`、`bytes`、`width`、`height`、`frames`、`duration` 和 `format`。尺寸单位为像素，时长单位为秒。

```rgscript
let info = media.inspect("input.gif");
if (info.isOk) {
    print(info.value.bytes, info.value.frames, info.value.duration);
}
```

## 转换与处理

`media.process(input, output, options = {})` 返回 `Result<MediaInfo, ServiceError>`。

| `options` 字段 | 含义与默认值 |
| --- | --- |
| `format` | `MediaFormat.Gif`、`MediaFormat.WebP`、`MediaFormat.Png` 或 `MediaFormat.APng`；省略时由输出扩展名决定 |
| `width`、`height` | 输出尺寸；只给一项时保持比例，两项都给时使用指定尺寸 |
| `cropX`、`cropY`、`cropWidth`、`cropHeight` | 裁剪矩形；使用时四项都要给出，且必须位于原图内 |
| `frameIndex` | 只输出指定帧，从 0 开始；省略时处理全部帧 |
| `flipHorizontal`、`flipVertical` | 水平／垂直翻转，默认 `false` |
| `rotation` | 顺时针旋转 0、90、180 或 270 度，默认 0 |
| `quality` | 1～100，默认 90 |
| `lossless` | WebP 是否无损，默认 `true` |
| `colors` | GIF 色数，2～256，默认 256 |
| `dither` | GIF 是否抖动，默认 `false` |
| `effects` | [像素效果列表](image-processing.md#像素效果) |
| `overwrite` | 是否覆盖已有输出，默认 `false` |

从动图输出静态 PNG 时必须指定 `frameIndex`。

```rgscript
let result = media.process("input.gif", "output.webp", {
    format: MediaFormat.WebP,
    width: 640,
    lossless: false,
    quality: 85
});
if (result.isOk) {
    print(result.value.path, result.value.bytes);
} else {
    print(result.error.message);
}
```

## 同时输出多份文件

`media.processMany(input, outputs)` 返回 `Result<MediaInfo, ServiceError>[]`，每项与输入的输出请求一一对应。每个输出使用 `{path, options}`，一次最多 16 项，输出路径不能重复。

```rgscript
let results = media.processMany("input.gif", [
    {path: "small.webp", options: {width: 320}},
    {path: "first.png", options: {frameIndex: 0}}
]);
for (let item of results) {
    if (item.isOk) {
        print(item.value.path);
    } else {
        print(item.error.message);
    }
}
```

## 按文件大小压缩

`media.optimize(input, output, options)` 返回 `Result<MediaOptimizationResult, ServiceError>`。

| `options` 字段 | 含义 |
| --- | --- |
| `targetBytes` | 目标文件大小，正整数字节数，必填 |
| `processing` | 上述 `media.process` 的选项 |
| `minimumQuality` | 允许降到的最低质量；省略时不降低质量 |
| `minimumColors` | GIF 允许降到的最低色数；省略时不降低色数 |
| `minimumScale` | 允许缩至原定输出尺寸的比例，范围大于 0 且不超过 1，默认 1 |
| `maximumAttempts` | 最多尝试次数，1～32，默认 8 |

质量下降用于有损 WebP，色数下降用于 GIF。要允许有损 WebP，需明确设置 `processing.lossless: false`。

```rgscript
let result = media.optimize("input.gif", "compressed.webp", {
    targetBytes: 30 * 1024 * 1024,
    processing: {format: MediaFormat.WebP, lossless: false, quality: 90},
    minimumQuality: 75,
    minimumScale: 0.8,
    maximumAttempts: 8
});
if (result.isOk) {
    print(result.value.achieved, result.value.smallestBytes);
} else {
    print(result.error.message);
}
```

调用成功不代表一定达标。`achieved` 为 `true` 时，`info` 是已保存文件的信息；为 `false` 时，`info` 为 `null`，不保存未达标文件。`smallestBytes` 表示尝试中最小的大小，`attempts` 是实际尝试次数，`settings` 是对应的处理选项。
