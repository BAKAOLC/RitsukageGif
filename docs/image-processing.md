# 图像合成与动画总览

`image` 服务处理已有文件，不需要开始录制。相对路径以脚本所在目录为基准。操作返回 `Result<MediaInfo, ServiceError>`；成功结果包含 `path`、`bytes`、`width`、`height`、`frames`、`duration` 和 `format`。

## 拼图、叠加与透明度

```rgscript
let result = image.compose("../captures/composite.png", {
    width: 800,
    height: 600,
    background: "#20242c",
    layers: [
        { path: "../captures/game.gif", frameIndex: 30,
          x: 0, y: 0, width: 800, height: 600 },
        { path: "assets/source-offline.png",
          x: 560, y: 400, width: 240, height: 180, opacity: 0.7 }
    ]
});
if (result.isOk) log(result.value.bytes);
else log(result.error);
```

图层按数组顺序绘制，后面的覆盖前面的。`x`、`y` 是画布中的左上角，可以为负数，超出画布的部分会被裁掉。动画输入通过 `frameIndex` 选择完整呈现的帧，从 0 开始；默认第 0 帧。

| 字段 | 含义 |
| --- | --- |
| `cropX / cropY / cropWidth / cropHeight` | 源图像素裁剪范围，使用时四个字段都要提供 |
| `rotation` | 顺时针旋转 0、90、180 或 270 度，默认 0 |
| `width / height` | 旋转后的输出尺寸，只提供一个时保持比例；都提供时按指定大小缩放 |
| `opacity` | 图层不透明度，0 到 1，默认 1 |
| `effects` | 局部处理列表，结构与 `media.process` 和场景图层使用的 `PixelEffect` 相同 |

`effects` 的 `space: EffectSpace.Source` 使用裁剪后的源图坐标，在旋转与缩放前处理；`EffectSpace.Output` 使用缩放后图层内的局部坐标，在叠加到画布前处理。处理可以用于遮挡、马赛克、模糊、灰度、反色或亮度对比度调整。

```rgscript
let result = image.compose("../captures/redacted.webp", {
    width: 640, height: 480,
    layers: [{
        path: "../captures/screenshot.png", width: 640, height: 480,
        effects: [{ kind: EffectKind.Mosaic, space: EffectSpace.Output,
                    region: { x: 10, y: 10, width: 120, height: 40 }, blockSize: 12 }]
    }],
    lossless: true
});
```

输出支持静态 PNG、WebP 和 GIF。默认从扩展名判断，也可以用 `format: MediaFormat.Png` 指定。`quality` 为 1 到 100，默认 90；`lossless` 控制 WebP 无损编码，默认 `true`。GIF 需要量化，无法保留完整真彩色。需要无损保存合成结果时使用 PNG 或无损 WebP。

默认不覆盖已有文件；需要覆盖时设置 `overwrite: true`。仅在合成成功后写入目标路径；取消或失败时保留原目标文件。

## 动画帧总览

```rgscript
let sheet = image.contactSheet("../captures/game.gif", "../captures/overview.png", {
    columns: 6,
    thumbnailWidth: 160,
    thumbnailHeight: 120,
    firstFrame: 0,
    frameStep: 10,
    maxFrames: 36,
    background: "white"
});
```

每隔 10 帧取一帧，最多 36 帧，组成 6 列的图像。缩略图保持原始比例并在格子里居中。默认 4 列，每格 160 × 120，从第 0 帧开始逐帧选取，最多 64 帧。输出同样支持 PNG、WebP、GIF，默认不覆盖。

合成最多 128 个图层；总览最多 1024 帧、128 列。最多同时执行两个图像任务。解码像素量超过限制时返回错误，可降低输入尺寸或减少选取帧数后重试。

## 图层局部效果

`scene.effects(name, effects)` 设置某个图层的处理列表；多路时使用 `scene.effectsFor(output, name, effects)`。`scene.set` / `setFor` 的 `effects` 字段可与裁剪和位置一次更新。空数组 `[]` 清除效果，每层最多 32 项，按列表顺序处理。

```rgscript
scene.effects('detail', [{
    kind: EffectKind.Mosaic,
    region: {x: 0, y: 0, width: 120, height: 40},
    blockSize: 12
}]);
```

| 字段 | 值与默认值 |
| --- | --- |
| `kind` | `EffectKind.Mosaic`、`Blur`、`Fill`、`Grayscale`、`Invert`、`BrightnessContrast` |
| `space` | `EffectSpace.Source`（默认）或 `EffectSpace.Output` |
| `region` | `{x, y, width, height}`，省略时处理整个图层 |
| `amount` | 混合强度 0～1，默认 1 |
| `blockSize` | 马赛克块边长 1～4096，默认 8 |
| `radius` | 模糊半径 1～256，默认 4 |
| `color` | 填充颜色 `#RRGGBB` 或 `#AARRGGBB`，默认黑色 |
| `brightness` | 亮度 -1～1，默认 0 |
| `contrast` | 对比度 0～8，默认 1 |

实时图层的 Source 坐标以裁剪后的源画面左上角为原点；Output 坐标以该层输出位置的左上角为原点，单位为应用最终录制缩放倍率前的布局像素。

同一个 `PixelEffect[]` 结构也可传给 `media.process` 的 `options.effects`，以及 `image.compose` 每个图层的 `effects`。离线合成的坐标阶段见上文。

可运行范例：[静态图像合成](../examples/image-composition.rgpreset)、[文件动画与动态效果](../examples/file-animation.rgpreset)。后者使用范例附带的动画文件作为输入。
