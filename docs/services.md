# 文件与图像服务

脚本可以读取文件大小、转换已有图像、裁剪或缩放，并组合这些操作。编辑器提供这些 API 的补全和类型提示。

## 从一次转换开始

下面是放在 `script { ... }` 内的片段：

```rgscript
const output = media.process('./input.gif', './output.webp', {
    format: MediaFormat.WebP,
    lossless: false,
    quality: 90
})?;
print(output.bytes / 1000000, 'MB');
return result.ok(null);
```

相对路径以入口 `.rgpreset` 所在目录为基准，包括导入模块内的服务调用。文件大小使用字节；`1 MB = 1000000` 字节，`1 MiB = 1048576` 字节。服务调用会等待处理结束，不需要额外的 `await`。

默认不覆盖现有输出。需要覆盖时明确指定 `overwrite: true`。使用不同的输入和输出路径，保留原文件便于重试和比较。

## 检查结果与错误

单个文件或媒体操作返回 `Result<T,ServiceError>`。错误包含 `code`、`message`、`path`：

```rgscript
const outcome = files.info('./output.gif');
if (outcome.isOk) {
    print(outcome.value.size);
} else {
    print(outcome.error.code, outcome.error.path, outcome.error.message);
}
```

`?` 取出成功值，失败时从当前函数或脚本返回该错误；使用它的函数应返回对应的 `Result`。取消操作会向上传播取消信号，不包装成普通文件错误。常见错误码包括 `notFound`、`accessDenied`、`invalidArgument`、`invalidPath`、`ioError`、`limitExceeded`、`operationFailed`。

## 文件与路径 API

以下调用均返回 `Result<返回类型,ServiceError>`。

| 调用 | 返回值与用途 |
| --- | --- |
| `files.info(path)` | `FileInfo`：`path`、`name`、`extension`、`size`、`isDirectory`、`modifiedUtc` |
| `files.exists(path)` | 是否存在；不存在为成功的 `false`，权限错误仍为失败 |
| `files.readText(path, maxBytes = 1048576)` | 读取文本，识别 BOM；默认 UTF-8，最多允许 16 MiB |
| `files.writeText(path, text, overwrite = false)` | 写入 UTF-8 文本，返回绝对路径；最多 16 MiB |
| `files.copy(source, destination, overwrite = false)` | 复制文件，返回目标绝对路径 |
| `files.move(source, destination, overwrite = false)` | 移动文件，返回目标绝对路径 |
| `files.createDirectory(path)` | 创建目录及所需父目录，返回绝对路径 |
| `files.list(path)` | 非递归的 `FileInfo[]`，最多 10000 项，不保证排序 |
| `paths.join(...parts)` | 按系统规则拼接路径 |
| `paths.absolute(path)` | 解析为绝对路径 |
| `paths.directory(path)` | 获取父目录部分 |
| `paths.name(path)` | 获取文件名，含扩展名 |
| `paths.extension(path)` | 获取带点的扩展名 |

`FileInfo.size` 对文件表示字节数，对目录为零，不统计目录内容。`modifiedUtc` 是 UTC 时间字符串。复制和文本写入仅在成功后替换目标文件，取消时保留原目标文件。目标父目录须事先存在。

## 图像与动画 API

| 调用 | 返回值 |
| --- | --- |
| `media.inspect(path)` | `Result<MediaInfo,ServiceError>`，读取图像或动画信息 |
| `media.process(input, output, options = {})` | `Result<MediaInfo,ServiceError>`，处理并保存 |
| `media.processMany(input, outputs)` | `Result<MediaInfo,ServiceError>[]`，每个输出分别报告成功或失败 |

`MediaInfo` 包含 `path`、`bytes`、`width`、`height`、`frames`、`duration`、`format`。`duration` 单位为秒，静态图像为零。

`MediaOptions` 的字段：

| 字段 | 含义与默认值 |
| --- | --- |
| `format` | `MediaFormat.Gif`、`WebP`、`Png`、`APng`；省略时从输出扩展名判断 |
| `width`、`height` | 输出尺寸；只给一个时按比例计算另一个，两个都给时使用指定尺寸 |
| `cropX`、`cropY`、`cropWidth`、`cropHeight` | 输入像素坐标裁剪；必须一起给出并位于画面内 |
| `flipHorizontal`、`flipVertical` | 水平或垂直翻转，默认 `false` |
| `rotation` | 顺时针旋转 `0`、`90`、`180`、`270` 度，默认 `0` |
| `frameIndex` | 提取从零开始的某一帧；动画转静态 PNG 时必须指定 |
| `colors` | GIF 每帧调色板颜色上限，`2`～`256`，默认 `256` |
| `dither` | GIF 是否使用误差扩散抖动，默认 `false` |
| `quality` | 编码质量 `1`～`100`，默认 `90`；用于有损 WebP 等编码器设置，GIF 调色板颜色上限由 `colors` 指定 |
| `lossless` | WebP 无损编码，默认 `true`；有损 WebP 请设为 `false` |
| `effects` | 有序局部效果列表，见[图层局部效果](image-processing.md#图层局部效果) |
| `overwrite` | 是否覆盖目标，默认 `false` |

基础操作顺序是还原动画完整帧、可选提取一帧、裁剪、翻转、旋转、缩放、编码；局部效果按 Source／Output 空间在对应阶段处理。未提取单帧时保留动画帧时序；跨格式转换受目标格式时间精度限制。GIF 颜色量化会影响颜色精度，转换后的文件大小取决于内容和编码参数。服务会限制图像尺寸和解码内存需求，超限返回错误。

## 一份输入，多个输出

```rgscript
const results = media.processMany('./input.gif', [
    {path: './small.gif', options: {width: 320, colors: 128}},
    {path: './preview.webp', options: {lossless: false, quality: 90}},
    {path: './cover.png', options: {frameIndex: 0, width: 160}}
]);
for (const item of results) {
    if (item.isOk) print(item.value.path, item.value.bytes);
    else print(item.error.path, item.error.message);
}
```

每次最多 16 个输出，最多两个编码任务并行，返回顺序与声明顺序一致。目标路径必须互不重复。个别输出失败不会撤销其他成功的文件；非法列表或重复路径会在开始前报参数错误。

`media.processMany` 用于转换已有文件。实时采集不同画面和布局使用 [同步多路录制](multi-output.md) 的 `record.startMany`。录制结束后，可从 `status().outputs` 获取已保存文件路径，再交给这些服务处理。

## 按体积决定下一步

完整范例见 [compress-to-budget.rgpreset](../examples/compress-to-budget.rgpreset)。它读取 `params.string('input', 默认路径)` 和 `params.number('targetMB', 30)`，逐步尝试较少的 GIF 颜色，检查字节数，达到目标后复制为最终输出。

每次都从原始输入开始，避免把上一次有损输出再次量化。候选文件复用同一路径，原文件不变；未达到目标时返回失败结果并保留最后一次候选。该策略保持尺寸和播放时序，降低颜色数会影响渐变、文字边缘和图像细节，处理后应检查输出效果。

[media-processing.rgpreset](../examples/media-processing.rgpreset) 使用范例图片演示 PNG 缩略图、GIF、WebP 输出和文件大小查询。
