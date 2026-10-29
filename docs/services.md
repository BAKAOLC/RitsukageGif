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
| `media.optimize(input, output, options)` | `Result<MediaOptimizationResult,ServiceError>`，在指定范围内尝试满足体积预算 |

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

单文件处理指定 APNG 的 `frameIndex` 时，只顺序解码到目标帧，并保留合成所需的画布；不会加载后续帧或同时保留整段动画。前面的帧仍可能影响目标帧，所以提取靠后的帧需要更多解码时间。内存限制按单帧合成工作区计算，长动画不会仅因总帧数而被拒绝；单帧尺寸仍受限制。

直接使用 `media.process` 将 APNG 处理为 APNG 时，逐帧解码、变换和编码，保留原有帧数、精确延时及循环次数，不需要将整段动画保留在内存中。这些流式路径不限制输入文件总字节数，但单帧像素工作区和压缩数据仍有内存限制；每帧压缩数据最多 256 MiB。转换到 GIF 或 WebP 仍使用完整解码流程。

## 保存静态截图

`capture.save(path, options)` 保存当前捕获目标的一帧，返回 `Result<MediaInfo,ServiceError>`。它不启动动画录制。

```rgscript
const shot = capture.save('./snapshot.png', {
    format: StaticImageFormat.Png,
    includeCursor: false,
    overwrite: true
})?;
print(shot.path, shot.width, shot.height, shot.bytes);
```

| 字段 | 含义与默认值 |
| --- | --- |
| `format` | `StaticImageFormat.Png`、`Jpeg` 或 `Bmp`；省略时按 `.png`、`.jpg`、`.jpeg`、`.bmp` 扩展名判断 |
| `quality` | JPEG 质量，`1`～`100`，默认 `90`；PNG、BMP 保持无损 |
| `background` | JPEG、BMP 的透明区域底色，默认 `#ffffff`，必须是不透明颜色；PNG 保留透明度 |
| `overwrite` | 是否覆盖目标，默认 `false` |
| `includeCursor` | 是否包含鼠标；省略时使用当前捕获设置 |
| `scalePercent` | 缩放百分比，`1`～`100`；省略时使用当前捕获设置 |
| `outputName` | 当前多路预设中的输出名称；省略时使用当前捕获目标 |
| `toneMap` | HDR 转 SDR 的映射方式：`HdrToneMapMode.HighlightCompression` 或 `ReferenceWhite`；省略时使用截图设置 |

文件写入成功后才替换目标；失败或取消不会留下部分文件。JPEG 使用完整色度采样，减少彩色文字边缘的失真；BMP 使用常见的 24 位格式。返回值的 `frames` 为 `1`，`duration` 为 `0`。

### 同一帧保存 SDR 和 HDR

`capture.saveOutputs(outputs, options)` 只捕获一次，并将这一帧分别保存到多个目标，返回 `Result<MediaInfo[],ServiceError>`。可以同时保存普通 PNG、JPEG、BMP、静态 WebP，以及保留 HDR 数据的 PNG 或 EXR。

```rgscript
const outputs = capture.saveOutputs([
    { path: './snapshot-sdr.png', color: OutputColorMode.Sdr,
      format: StaticImageFormat.Png },
    { path: './snapshot-hdr.exr', color: OutputColorMode.Hdr,
      hdrFormat: HdrImageFormat.Exr }
], {
    includeCursor: false,
    scalePercent: 100,
    toneMap: HdrToneMapMode.HighlightCompression
})?;
for (const output of outputs) print(output.path, output.bytes);
```

每个目标独立选择 `OutputColorMode.Sdr` 或 `Hdr`，不能用 `Both` 代替两个目标。SDR 目标使用 `format`、`quality` 和 `background`；HDR 目标使用 `hdrFormat`，支持 `HdrImageFormat.Png`、`Exr`。格式省略时按文件扩展名识别。`toneMap` 只影响 SDR 输出，不修改 HDR 原始亮度。

第二个参数支持 `includeCursor`、`scalePercent`、`outputName`、`toneMap`，含义同 `capture.save`。一个调用最多 16 个目标。目标路径必须各不相同且不存在，不支持覆盖；已有文件需要覆盖时，可继续使用单文件 `capture.save` 保存 SDR 图像。

所有目标先完成临时编码，然后发布到目标位置；编码失败或取消会删除本次临时文件，发布失败会回滚本次已发布的文件。文件系统不能保证多个目录中的文件在同一瞬间出现；回滚遇到权限或设备故障时会返回清理错误。未提供浮点 HDR 数据的捕获来源会在输出前返回错误，不会把普通 SDR 像素伪装成 HDR。

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

每次最多 16 个输出，返回顺序与声明顺序一致。输入只解码和合成一次，再依次生成各个输出，减少重复解码和同时驻留的编码工作区。若输入是 APNG，且所有输出都提取指定帧，会按帧号顺序扫描一次，并按原声明顺序返回结果；不保留整段动画。批量输出包含覆盖原输入的操作时，会先完整解码，保证其他输出仍使用原始内容。目标路径必须互不重复。个别输出失败不会撤销其他成功的文件；非法列表或重复路径会在开始前报参数错误。

`media.processMany` 用于转换已有文件。实时采集不同画面和布局使用 [同步多路录制](multi-output.md) 的 `record.startMany`。录制结束后，可从 `status().outputs` 获取已保存文件路径，再交给这些服务处理。

## 按体积决定下一步

`media.optimize` 自动尝试有限组参数，只在达到目标时保存输出：

```rgscript
const result = media.optimize('./input.gif', './small.gif', {
    targetBytes: 30 * 1024 * 1024,
    processing: {colors: 256, overwrite: true},
    minimumColors: 128,
    minimumScale: 0.75,
    maximumAttempts: 8
})?;
if (result.achieved) {
    print(result.info.path, result.info.bytes, result.settings.width);
} else {
    print('未达到目标', result.smallestBytes, result.attempts);
}
```

| 字段 | 含义与默认值 |
| --- | --- |
| `targetBytes` | 必填，输出文件的最大字节数，正整数 |
| `processing` | 基础 `MediaOptions`，默认 `{}` |
| `minimumQuality` | 有损 WebP 允许的最低质量；默认保持基础质量，不会自动切换为有损 |
| `minimumColors` | GIF 允许的最低颜色上限；默认保持基础颜色上限 |
| `minimumScale` | 相对基础输出尺寸的最低比例，范围 `(0, 1]`，默认 `1`；整数尺寸向上保留下限，不会因取整低于该比例 |
| `maximumAttempts` | 最多编码次数，范围 `1`～`32`，默认 `8`；相同参数不会重复编码 |

服务从基础参数逐步走向允许的下限，找到满足预算的第一份结果就停止。多个下限同时指定时同步调整，不保证找到所有可能参数中的最优结果。一次调用内复用原始输入的解码画面，每组参数使用独立画面，不会以前一个候选作为输入。保留帧数与播放时序；只有显式设置 `frameIndex` 时才提取单帧。

解码画面在本次调用成功、失败或取消后释放。完整动画仍需要保留整段解码画面，受解码内存限制；指定 APNG 的 `frameIndex` 时只保留目标帧及其合成工作区。

成功返回值包含 `achieved`、可选的 `info`、`smallestBytes`、`attempts` 和实际 `settings`。未达到预算时 `achieved` 为 `false`，不生成输出，已有目标保持不变；`smallestBytes` 和 `settings` 对应本次最小候选。编码失败返回错误，取消向上传播。临时候选自动清理。允许设置 `processing.overwrite: true` 后原地处理；仅在达到预算时替换原文件。

下面的脚本范例展示自行控制尝试顺序的方式。

完整范例见 [compress-to-budget.rgpreset](../examples/compress-to-budget.rgpreset)。它读取 `params.string('input', 默认路径)` 和 `params.number('targetMB', 30)`，逐步尝试较少的 GIF 颜色，检查字节数，达到目标后复制为最终输出。

每次都从原始输入开始，避免把上一次有损输出再次量化。候选文件复用同一路径，原文件不变；未达到目标时返回失败结果并保留最后一次候选。该策略保持尺寸和播放时序，降低颜色数会影响渐变、文字边缘和图像细节，处理后应检查输出效果。

[media-processing.rgpreset](../examples/media-processing.rgpreset) 使用范例图片演示 PNG 缩略图、GIF、WebP 输出和文件大小查询。

## 检测隐私区域

`privacy.detect(image, language?)` 接受图片路径或 `Bytes`，返回 `Result<PrivacyDetectionResult,ServiceError>`：

```rgscript
const detected = privacy.detect('./capture.png', 'zh-Hans')?;
for (const item of detected.candidates) {
    if (item.isSensitive) print(item.kind, item.bounds);
}
for (const failure of detected.failures) print(failure.provider, failure.message);
```

候选包含 `provider`、`kind`、`text`、`bounds`、`isSensitive` 和 `boundsApproximate`。`kind` 使用 `PrivacyCandidateKind` 枚举，支持普通文本、邮箱、电话、身份证校验号、支付卡校验号和二维码。普通文本不会自动视为隐私；检测结果仍需检查。

坐标对应图片按 EXIF 方向调整后的首帧像素。二维码边界为保守估计，`boundsApproximate` 为 `true`；不根据一维码的两个端点猜测完整范围。服务只返回候选，不修改图片。路径相对脚本目录解析，输入上限为 64 MiB、16 × 1024 × 1024 像素。某个检测服务不可用时，其错误放在 `failures`，其他服务的结果保留；读图或解码失败返回错误，取消向上传播。
