# 图像合成与像素效果

使用 `image` 可以把多张图片合成一张图、生成动图帧预览，或修复指定区域。处理已有文件的尺寸、裁剪和格式时，使用[媒体服务](services.md#转换与处理)。

以下三个 `image` 函数均返回 `Result<MediaInfo, ServiceError>`。成功时读取 `value`，失败时读取 `error.message`。相对路径以入口预设文件所在目录为基准，在导入的模块中调用时也相同。

## 合成图片

`image.compose(output, options)` 创建指定大小的画布，并按 `layers` 的顺序叠加图片，后面的图层覆盖在前面的图层上。

| `options` 字段 | 含义 |
| --- | --- |
| `width`、`height` | 画布宽高，正整数，必填 |
| `background` | 背景颜色，默认 `"transparent"` |
| `layers` | 图层数组，默认空数组，最多 128 层 |
| `format` | `ImageFormat.Png`、`ImageFormat.WebP`、`ImageFormat.Gif`、`ImageFormat.Jpeg` 或 `ImageFormat.Bmp`；省略时由输出扩展名决定 |
| `quality` | 1～100，默认 90 |
| `lossless` | WebP 是否无损，默认 `true` |
| `overwrite` | 是否覆盖已有输出，默认 `false` |

每个图层可设置：

| 字段 | 含义 |
| --- | --- |
| `path` | 图片或动图路径，必填 |
| `frameIndex` | 使用第几帧，从 0 开始，默认 0 |
| `x`、`y` | 图层在画布中的左上角坐标，默认 0 |
| `width`、`height` | 图层尺寸；只给一项时保持比例，省略时使用图片尺寸 |
| `cropX`、`cropY`、`cropWidth`、`cropHeight` | 图层裁剪范围；使用时四项都要给出，且必须位于图片内 |
| `rotation` | 顺时针旋转 0、90、180 或 270 度，默认 0 |
| `opacity` | 不透明度，0～1，默认 1 |
| `effects` | 像素效果列表 |

下面的例子不需要输入图片，会生成一张纯色 PNG：

```rgscript
let result = image.compose("background.png", {
    width: 640,
    height: 360,
    background: "#e8eef8"
});
if (result.isOk) {
    print(result.value.path);
} else {
    print(result.error.message);
}
```

准备好 `photo.png` 和 `logo.png` 后，可将徽标叠加到图片右下方：

```rgscript
let result = image.compose("combined.png", {
    width: 640,
    height: 360,
    layers: [
        {path: "photo.png", width: 640, height: 360},
        {path: "logo.png", x: 520, y: 280, width: 100, opacity: 0.8}
    ]
});
if (result.isErr) {
    print(result.error.message);
}
```

## 像素效果

`media.process` 的 `effects` 作用于输入帧；`image.compose` 中每个图层的 `effects` 只作用于该图层。同一坐标阶段中的效果按列表顺序应用，每个列表最多 32 项。

| `kind` | 效果 | 常用字段 |
| --- | --- | --- |
| `EffectKind.Mosaic` | 马赛克 | `blockSize`，默认 8，范围 1～4096 |
| `EffectKind.Blur` | 模糊 | `radius`，默认 4，范围 1～256 |
| `EffectKind.Fill` | 纯色填充 | `color`，默认黑色 |
| `EffectKind.Grayscale` | 灰度 | `amount` |
| `EffectKind.Invert` | 反色 | `amount` |
| `EffectKind.BrightnessContrast` | 亮度与对比度 | `brightness`、`contrast` |

公共字段：

| 字段 | 含义 |
| --- | --- |
| `region` | `{x, y, width, height}`；省略时处理整张图，宽高必须为正整数 |
| `space` | 默认 `EffectSpace.Source`，在裁剪后、旋转和缩放前的图中定位；`EffectSpace.Output` 在旋转和缩放后的图中定位 |
| `amount` | 效果强度，0～1，默认 1 |
| `color` | `"#RRGGBB"` 或 `"#AARRGGBB"` |
| `brightness` | 亮度调整，-1～1，默认 0 |
| `contrast` | 对比度，0～8，默认 1 |

输出坐标相对于变换后的单张图片或图层，不是合成画布。坐标和尺寸均以像素为单位。

```rgscript
let result = media.process("input.gif", "masked.gif", {
    effects: [{
        kind: EffectKind.Mosaic,
        region: {x: 20, y: 30, width: 120, height: 60},
        blockSize: 12
    }]
});
if (result.isErr) {
    print(result.error.message);
}
```

## 动图帧预览

`image.contactSheet(input, output, options = {})` 将抽取的帧排列成一张图片。每帧保持比例缩放，并放在自己的缩略图格子中央。

| 字段 | 默认值与含义 |
| --- | --- |
| `columns` | 4，每行格数，1～128 |
| `thumbnailWidth`、`thumbnailHeight` | 160、120，每格的像素尺寸 |
| `firstFrame` | 0，从哪帧开始 |
| `frameStep` | 1，每隔多少帧取一帧，至少为 1 |
| `maxFrames` | 64，最多取多少帧，1～1024 |
| `background` | `"transparent"`，格子背景 |
| `overwrite` | `false` |

```rgscript
let result = image.contactSheet("input.gif", "frames.png", {
    columns: 4,
    frameStep: 5,
    maxFrames: 16
});
if (result.isErr) {
    print(result.error.message);
}
```

## 修复图像区域

`image.inpaint(input, output, options)` 用于修复图片中的小范围遮挡。结果取决于周围内容，不保证还原被遮住的原始细节。

`options` 至少提供 `regions` 或 `mask`：

| 字段 | 含义 |
| --- | --- |
| `regions` | `{x, y, width, height}` 数组，最多 128 个矩形 |
| `mask` | 遮罩图片路径，尺寸必须与输入图片相同 |
| `maskChannel` | 默认 `ImageMaskChannel.Luminance`；也可用 `ImageMaskChannel.Alpha` |
| `radius` | 修复半径，1～32，默认 4 |
| `frameIndex` | 取输入中的哪一帧，默认 0；输出单张图片 |
| `format`、`quality`、`lossless`、`overwrite` | 与合成图片的对应选项相同 |

亮度遮罩中，白色表示修复、黑色表示保留，透明部分不修复。选择 Alpha 通道时，以不透明度决定修复范围。同时指定 `regions` 和 `mask` 时，只修复遮罩在矩形内的部分。

```rgscript
let result = image.inpaint("photo.png", "restored.png", {
    regions: [{x: 20, y: 20, width: 30, height: 12}],
    radius: 4
});
if (result.isErr) {
    print(result.error.message);
}
```
