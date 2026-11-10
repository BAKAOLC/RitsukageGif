# 脚本类型与对象字段参考

本页列出脚本可用的基础类型、对象字段和枚举。函数与方法的参数和返回值见[API 参考](api-reference.md)。

- `T[]` 表示元素类型为 T 的数组；`T?` 表示 T 或 `null`；`A|B` 表示两种类型均可。
- “可省略”表示构造参数对象时可以不提供该字段；它与“可为 null”不同。
- 普通对象字段可读写；修改服务返回的状态对象不会替代相应的操作调用。需要改变场景或录制时，应调用对应 API。
- 以下字段表只列数据成员；同一类型可调用的方法在 API 参考中按类型列出。

## 基础类型

| 类型 | 用途 |
| --- | --- |
| `any` | 可保存任意脚本值。 |
| `unknown` | 尚未确定具体类型的值。 |
| `never` | 没有可取值的类型，用于不可能出现的结果分支。 |
| `number` | 有限数值。 |
| `string` | 文本。 |
| `boolean` | true 或 false。 |
| `null` | 空值。 |
| `void` | 没有返回内容。 |
| `function` | 可调用的函数值。 |
| `array` | 按顺序保存元素的数组；可用 T[] 指定元素类型。 |
| `object` | 以字符串为键的对象；字段由创建对象的脚本决定。 |

数组的 `length` 和字符串的 `length` 为只读；数组元素及对象字段可按相应值的类型读写。

## 对象与值类型

### `ActionInfo`

可执行功能动作的标识、名称、启用状态与文件参数需求。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `id` | `string?` | 可读写 | 是 |
| `kind` | `string` | 可读写 | 否 |
| `name` | `string` | 可读写 | 否 |
| `enabled` | `boolean` | 可读写 | 否 |
| `fileParameter` | `boolean` | 可读写 | 否 |

### `ActionOptions`

执行功能动作时的参数、预设名称与预设参数。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `parameter` | `string` | 可读写 | 是 |
| `preset` | `string` | 可读写 | 是 |
| `arguments` | `object` | 可读写 | 是 |

### `ActionResult`

本次功能动作产生的文件路径与选区；没有对应输出时为 [] 或 null。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `paths` | `string[]` | 可读写 | 否 |
| `region` | `Rectangle?` | 可读写 | 否 |

### `AnimationDocument`

可以修改帧、时长、画面并导出的动图文档。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `canRedo` | `boolean` | 只读 | 否 |
| `canUndo` | `boolean` | 只读 | 否 |
| `closed` | `boolean` | 只读 | 否 |
| `frames` | `AnimationFrame[]` | 只读 | 否 |
| `height` | `number` | 只读 | 否 |
| `iterations` | `number` | 只读 | 否 |
| `width` | `number` | 只读 | 否 |

### `AnimationExportOptions`

动图导出的格式、调色、质量与覆盖选项。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `format` | `AnimationFormat` | 可读写 | 是 |
| `colors` | `number` | 可读写 | 是 |
| `quality` | `number` | 可读写 | 是 |
| `lossless` | `boolean` | 可读写 | 是 |
| `dither` | `boolean` | 可读写 | 是 |
| `overwrite` | `boolean` | 可读写 | 是 |

### `AnimationFrame`

动图中一帧的宽高与显示时长。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `width` | `number` | 可读写 | 否 |
| `height` | `number` | 可读写 | 否 |
| `delay` | `number` | 可读写 | 否 |

### `AnimationTransformOptions`

动图的缩放、裁剪、翻转、旋转和像素效果。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `width` | `number` | 可读写 | 是 |
| `height` | `number` | 可读写 | 是 |
| `cropX` | `number` | 可读写 | 是 |
| `cropY` | `number` | 可读写 | 是 |
| `cropWidth` | `number` | 可读写 | 是 |
| `cropHeight` | `number` | 可读写 | 是 |
| `flipHorizontal` | `boolean` | 可读写 | 是 |
| `flipVertical` | `boolean` | 可读写 | 是 |
| `rotation` | `number` | 可读写 | 是 |
| `effects` | `PixelEffect[]` | 可读写 | 是 |

### `AnnotationCanvas`

可以添加、修改、撤销并导出标注的图像画布。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `canRedo` | `boolean` | 只读 | 否 |
| `canUndo` | `boolean` | 只读 | 否 |
| `count` | `number` | 只读 | 否 |
| `height` | `number` | 只读 | 否 |
| `isClosed` | `boolean` | 只读 | 否 |
| `width` | `number` | 只读 | 否 |

### `AnnotationExportOptions`

标注画布的静态图导出格式、质量、背景和覆盖选项。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `format` | `StaticImageFormat` | 可读写 | 是 |
| `quality` | `number` | 可读写 | 是 |
| `background` | `string` | 可读写 | 是 |
| `overwrite` | `boolean` | 可读写 | 是 |

### `AnnotationHdrExportOptions`

标注画布 HDR 图像的导出格式与覆盖选项。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `format` | `HdrImageFormat` | 可读写 | 是 |
| `overwrite` | `boolean` | 可读写 | 是 |

### `AnnotationPoint`

标注画布上的二维坐标。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `x` | `number` | 可读写 | 否 |
| `y` | `number` | 可读写 | 否 |

### `AnnotationProperties`

几何、文字、编号、放大镜、水印等标注工具的详细属性。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `filled` | `boolean` | 可读写 | 是 |
| `geometryShape` | `AnnotationGeometryShape` | 可读写 | 是 |
| `strokeStyle` | `AnnotationStrokeStyle` | 可读写 | 是 |
| `cornerRadius` | `number` | 可读写 | 是 |
| `curved` | `boolean` | 可读写 | 是 |
| `arrowShape` | `AnnotationArrowShape` | 可读写 | 是 |
| `startEndpoint` | `AnnotationEndpointStyle` | 可读写 | 是 |
| `endEndpoint` | `AnnotationEndpointStyle` | 可读写 | 是 |
| `spotlightEllipse` | `boolean` | 可读写 | 是 |
| `spotlightStroke` | `boolean` | 可读写 | 是 |
| `bold` | `boolean` | 可读写 | 是 |
| `italic` | `boolean` | 可读写 | 是 |
| `strikeThrough` | `boolean` | 可读写 | 是 |
| `textBackground` | `string` | 可读写 | 是 |
| `textOutline` | `string` | 可读写 | 是 |
| `textOutlineWidth` | `number` | 可读写 | 是 |
| `wrapWidth` | `number` | 可读写 | 是 |
| `textPadding` | `number` | 可读写 | 是 |
| `opacity` | `number` | 可读写 | 是 |
| `number` | `number` | 可读写 | 是 |
| `tiled` | `boolean` | 可读写 | 是 |
| `nineGrid` | `boolean` | 可读写 | 是 |
| `anchor` | `number` | 可读写 | 是 |
| `imageWidth` | `number` | 可读写 | 是 |
| `blurRadius` | `number` | 可读写 | 是 |
| `fontFamily` | `string` | 可读写 | 是 |
| `magnification` | `number` | 可读写 | 是 |
| `magnifierEllipse` | `boolean` | 可读写 | 是 |
| `magnifierAntialias` | `boolean` | 可读写 | 是 |
| `magnifierShadow` | `boolean` | 可读写 | 是 |
| `magnifierShowAnnotations` | `boolean` | 可读写 | 是 |
| `magnifierLink` | `AnnotationMagnifierLink` | 可读写 | 是 |
| `markerPath` | `AnnotationMarkerPath` | 可读写 | 是 |
| `markerBlend` | `AnnotationMarkerBlend` | 可读写 | 是 |
| `numberStyle` | `AnnotationNumberStyle` | 可读写 | 是 |
| `numberFormat` | `AnnotationNumberFormat` | 可读写 | 是 |
| `numberCallout` | `boolean` | 可读写 | 是 |
| `numberCaptionGuide` | `boolean` | 可读写 | 是 |
| `numberCaptionScale` | `number` | 可读写 | 是 |
| `numberCaptionOutline` | `boolean` | 可读写 | 是 |
| `pixelPath` | `AnnotationPixelPath` | 可读写 | 是 |
| `mosaicMode` | `AnnotationMosaicMode` | 可读写 | 是 |
| `autoMosaicSynchronous` | `boolean` | 可读写 | 是 |
| `inpaintingRadius` | `number` | 可读写 | 是 |
| `textOutlineEnabled` | `boolean` | 可读写 | 是 |
| `textBackgroundEnabled` | `boolean` | 可读写 | 是 |
| `textBackgroundOpacity` | `number` | 可读写 | 是 |
| `textBackgroundRadius` | `number` | 可读写 | 是 |
| `watermarkText` | `string` | 可读写 | 是 |
| `watermarkImagePath` | `string?` | 可读写 | 是 |
| `watermarkSpacing` | `number` | 可读写 | 是 |
| `watermarkAngle` | `number` | 可读写 | 是 |
| `watermarkTimeFormat` | `string` | 可读写 | 是 |

### `AnnotationStyle`

新增或修改标注时的颜色、粗细、文字、图像和工具属性。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `color` | `string` | 可读写 | 是 |
| `thickness` | `number` | 可读写 | 是 |
| `fontSize` | `number` | 可读写 | 是 |
| `mosaicSize` | `number` | 可读写 | 是 |
| `text` | `string` | 可读写 | 是 |
| `properties` | `AnnotationProperties` | 可读写 | 是 |
| `image` | `string\|Bytes` | 可读写 | 是 |
| `magnifierOffset` | `AnnotationPoint` | 可读写 | 是 |

### `array`

按顺序保存元素的数组；可用 T[] 指定元素类型。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `length` | `number` | 只读 | 否 |

### `BarcodePoint`

条码在图像中的一个顶点。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `x` | `number` | 可读写 | 否 |
| `y` | `number` | 可读写 | 否 |

### `BarcodeResult`

条码内容、格式及其顶点。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `text` | `string` | 可读写 | 否 |
| `format` | `string` | 可读写 | 否 |
| `points` | `BarcodePoint[]` | 可读写 | 否 |

### `Bytes`

字节数据，可转换为文本、Base64 或十六进制。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `length` | `number` | 只读 | 否 |

### `DocumentInfo`

已输出文档的路径、文件大小和页数。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `path` | `string` | 可读写 | 否 |
| `bytes` | `number` | 可读写 | 否 |
| `pages` | `number` | 可读写 | 否 |

### `DocumentMargins`

文档页面四周的边距。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `left` | `number` | 可读写 | 是 |
| `top` | `number` | 可读写 | 是 |
| `right` | `number` | 可读写 | 是 |
| `bottom` | `number` | 可读写 | 是 |

### `DocumentOutputOptions`

PDF 输出的纸张、边距、图像质量和覆盖选项。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `paperSize` | `DocumentPaperSize` | 可读写 | 是 |
| `orientation` | `DocumentOrientation` | 可读写 | 是 |
| `pageMode` | `DocumentPageMode` | 可读写 | 是 |
| `paginationDirection` | `DocumentPaginationDirection` | 可读写 | 是 |
| `widthMillimeters` | `number` | 可读写 | 是 |
| `heightMillimeters` | `number` | 可读写 | 是 |
| `margins` | `DocumentMargins` | 可读写 | 是 |
| `dpi` | `number` | 可读写 | 是 |
| `background` | `string` | 可读写 | 是 |
| `lossless` | `boolean` | 可读写 | 是 |
| `quality` | `number` | 可读写 | 是 |
| `overwrite` | `boolean` | 可读写 | 是 |

### `EffectRect`

像素效果或图像修复使用的矩形范围。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `x` | `number` | 可读写 | 否 |
| `y` | `number` | 可读写 | 否 |
| `width` | `number` | 可读写 | 否 |
| `height` | `number` | 可读写 | 否 |

### `FileInfo`

文件或目录的路径、大小及修改时间。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `path` | `string` | 可读写 | 否 |
| `name` | `string` | 可读写 | 否 |
| `extension` | `string` | 可读写 | 否 |
| `size` | `number` | 可读写 | 否 |
| `isDirectory` | `boolean` | 可读写 | 否 |
| `modifiedUtc` | `string` | 可读写 | 否 |

### `FormulaRecognitionResult`

图像中的公式列表。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `width` | `number` | 可读写 | 否 |
| `height` | `number` | 可读写 | 否 |
| `formulas` | `RecognizedFormula[]` | 可读写 | 否 |

### `ImageCompositionOptions`

合成画布尺寸、背景、图层和保存选项。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `width` | `number` | 可读写 | 否 |
| `height` | `number` | 可读写 | 否 |
| `layers` | `ImageLayer[]` | 可读写 | 是 |
| `background` | `string` | 可读写 | 是 |
| `format` | `ImageFormat` | 可读写 | 是 |
| `quality` | `number` | 可读写 | 是 |
| `lossless` | `boolean` | 可读写 | 是 |
| `overwrite` | `boolean` | 可读写 | 是 |

### `ImageContactSheetOptions`

动图帧预览的抽帧间隔、格数与缩略图尺寸。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `columns` | `number` | 可读写 | 是 |
| `thumbnailWidth` | `number` | 可读写 | 是 |
| `thumbnailHeight` | `number` | 可读写 | 是 |
| `firstFrame` | `number` | 可读写 | 是 |
| `frameStep` | `number` | 可读写 | 是 |
| `maxFrames` | `number` | 可读写 | 是 |
| `background` | `string` | 可读写 | 是 |
| `overwrite` | `boolean` | 可读写 | 是 |

### `ImageInpaintingOptions`

图像修复的区域、遮罩、选帧和输出选项。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `regions` | `EffectRect[]` | 可读写 | 是 |
| `mask` | `string` | 可读写 | 是 |
| `maskChannel` | `ImageMaskChannel` | 可读写 | 是 |
| `radius` | `number` | 可读写 | 是 |
| `frameIndex` | `number` | 可读写 | 是 |
| `format` | `ImageFormat` | 可读写 | 是 |
| `quality` | `number` | 可读写 | 是 |
| `lossless` | `boolean` | 可读写 | 是 |
| `overwrite` | `boolean` | 可读写 | 是 |

### `ImageLayer`

参与合成的图片、裁剪范围及画布位置。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `path` | `string` | 可读写 | 否 |
| `frameIndex` | `number` | 可读写 | 是 |
| `x` | `number` | 可读写 | 是 |
| `y` | `number` | 可读写 | 是 |
| `width` | `number` | 可读写 | 是 |
| `height` | `number` | 可读写 | 是 |
| `cropX` | `number` | 可读写 | 是 |
| `cropY` | `number` | 可读写 | 是 |
| `cropWidth` | `number` | 可读写 | 是 |
| `cropHeight` | `number` | 可读写 | 是 |
| `opacity` | `number` | 可读写 | 是 |
| `rotation` | `number` | 可读写 | 是 |
| `effects` | `PixelEffect[]` | 可读写 | 是 |

### `InputData`

外部输入信道的值、版本、时间与连接状态。value 的结构由发送方决定。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `value` | `any` | 可读写 | 否 |
| `version` | `number` | 可读写 | 否 |
| `timestamp` | `number` | 可读写 | 否 |
| `connected` | `boolean` | 可读写 | 否 |

### `LongCaptureColorRegion`

长截图中一个矩形范围的 HDR 来源与 SDR 白色亮度。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `x` | `number` | 可读写 | 否 |
| `y` | `number` | 可读写 | 否 |
| `width` | `number` | 可读写 | 否 |
| `height` | `number` | 可读写 | 否 |
| `isHdrSource` | `boolean` | 可读写 | 否 |
| `sdrWhiteNits` | `number` | 可读写 | 否 |

### `LongCaptureHdrFrame`

长截图保留的 HDR 像素、尺寸、行跨度和色彩信息。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `alphaMode` | `string` | 只读 | 否 |
| `colorRegions` | `LongCaptureColorRegion[]` | 只读 | 否 |
| `height` | `number` | 只读 | 否 |
| `isHdrSource` | `boolean` | 只读 | 否 |
| `pixelFormat` | `string` | 只读 | 否 |
| `pixels` | `Bytes` | 只读 | 否 |
| `referenceWhiteNits` | `number` | 只读 | 否 |
| `sdrWhiteNits` | `number` | 只读 | 否 |
| `stride` | `number` | 只读 | 否 |
| `width` | `number` | 只读 | 否 |

### `LongCaptureImage`

长截图的像素数据及可选 HDR 图像。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `alphaMode` | `string` | 只读 | 否 |
| `hdr` | `LongCaptureHdrFrame?` | 只读 | 否 |
| `height` | `number` | 只读 | 否 |
| `pixelFormat` | `string` | 只读 | 否 |
| `pixels` | `Bytes` | 只读 | 否 |
| `stride` | `number` | 只读 | 否 |
| `width` | `number` | 只读 | 否 |

### `LongCaptureMatch`

长截图一次匹配的结果、偏移、新增长度和置信度。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `status` | `ScrollCaptureStatus` | 可读写 | 否 |
| `offset` | `number` | 可读写 | 否 |
| `addedPixels` | `number` | 可读写 | 否 |
| `confidence` | `number` | 可读写 | 否 |
| `width` | `number` | 可读写 | 否 |
| `height` | `number` | 可读写 | 否 |

### `LongCaptureOptions`

长截图的输入来源、区域、方向、匹配和图像大小限制。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `x` | `number` | 可读写 | 是 |
| `y` | `number` | 可读写 | 是 |
| `width` | `number` | 可读写 | 否 |
| `height` | `number` | 可读写 | 否 |
| `source` | `LongCaptureSource` | 可读写 | 是 |
| `windowId` | `number` | 可读写 | 是 |
| `axis` | `ScrollCaptureAxis` | 可读写 | 是 |
| `interval` | `number` | 可读写 | 是 |
| `autoCropOnReverse` | `boolean` | 可读写 | 是 |
| `retainHdr` | `boolean` | 可读写 | 是 |
| `leadingInset` | `number` | 可读写 | 是 |
| `trailingInset` | `number` | 可读写 | 是 |
| `maximumLength` | `number` | 可读写 | 是 |
| `maximumBytes` | `number` | 可读写 | 是 |
| `minimumOverlap` | `number` | 可读写 | 是 |
| `toneMap` | `HdrToneMapMode` | 可读写 | 是 |

### `LongCaptureSaveOptions`

长截图导出的 SDR/HDR 颜色模式、格式和质量选项。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `color` | `OutputColorMode` | 可读写 | 是 |
| `format` | `StaticImageFormat` | 可读写 | 是 |
| `hdrFormat` | `HdrImageFormat` | 可读写 | 是 |
| `quality` | `number` | 可读写 | 是 |
| `background` | `string` | 可读写 | 是 |
| `overwrite` | `boolean` | 可读写 | 是 |

### `LongCaptureSession`

可送入画面、移动区域、查询状态并导出的长截图会话。

没有固定的数据字段。

### `LongCaptureState`

长截图会话的运行状态、区域、当前图像尺寸与最近匹配结果。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `running` | `boolean` | 可读写 | 否 |
| `stopped` | `boolean` | 可读写 | 否 |
| `x` | `number` | 可读写 | 否 |
| `y` | `number` | 可读写 | 否 |
| `regionWidth` | `number` | 可读写 | 否 |
| `regionHeight` | `number` | 可读写 | 否 |
| `axis` | `ScrollCaptureAxis` | 可读写 | 否 |
| `width` | `number` | 可读写 | 否 |
| `height` | `number` | 可读写 | 否 |
| `last` | `LongCaptureMatch?` | 可读写 | 否 |
| `error` | `string?` | 可读写 | 否 |

### `Map`

以键关联值的集合。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `size` | `number` | 只读 | 否 |

### `MediaInfo`

媒体文件的尺寸、帧数、时长和文件大小。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `path` | `string` | 可读写 | 否 |
| `bytes` | `number` | 可读写 | 否 |
| `width` | `number` | 可读写 | 否 |
| `height` | `number` | 可读写 | 否 |
| `frames` | `number` | 可读写 | 否 |
| `duration` | `number` | 可读写 | 否 |
| `format` | `string` | 可读写 | 否 |

### `MediaOptimizationOptions`

按目标文件大小压缩时允许的质量、色数及缩放范围。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `targetBytes` | `number` | 可读写 | 否 |
| `processing` | `MediaOptions` | 可读写 | 是 |
| `minimumQuality` | `number` | 可读写 | 是 |
| `minimumColors` | `number` | 可读写 | 是 |
| `minimumScale` | `number` | 可读写 | 是 |
| `maximumAttempts` | `number` | 可读写 | 是 |

### `MediaOptimizationResult`

文件大小优化是否达标、实际尝试和对应输出信息。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `achieved` | `boolean` | 可读写 | 否 |
| `info` | `MediaInfo?` | 可读写 | 是 |
| `smallestBytes` | `number` | 可读写 | 否 |
| `attempts` | `number` | 可读写 | 否 |
| `settings` | `MediaOptions` | 可读写 | 否 |

### `MediaOptions`

媒体转换、裁剪、缩放及像素效果的选项。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `format` | `MediaFormat` | 可读写 | 是 |
| `colors` | `number` | 可读写 | 是 |
| `width` | `number` | 可读写 | 是 |
| `height` | `number` | 可读写 | 是 |
| `quality` | `number` | 可读写 | 是 |
| `lossless` | `boolean` | 可读写 | 是 |
| `dither` | `boolean` | 可读写 | 是 |
| `overwrite` | `boolean` | 可读写 | 是 |
| `cropX` | `number` | 可读写 | 是 |
| `cropY` | `number` | 可读写 | 是 |
| `cropWidth` | `number` | 可读写 | 是 |
| `cropHeight` | `number` | 可读写 | 是 |
| `flipHorizontal` | `boolean` | 可读写 | 是 |
| `flipVertical` | `boolean` | 可读写 | 是 |
| `rotation` | `number` | 可读写 | 是 |
| `frameIndex` | `number` | 可读写 | 是 |
| `effects` | `PixelEffect[]` | 可读写 | 是 |

### `MediaOutput`

一份媒体输出的目标路径与处理选项。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `path` | `string` | 可读写 | 否 |
| `options` | `MediaOptions` | 可读写 | 是 |

### `OcrBounds`

文字识别结果在图像中的矩形范围。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `x` | `number` | 可读写 | 否 |
| `y` | `number` | 可读写 | 否 |
| `width` | `number` | 可读写 | 否 |
| `height` | `number` | 可读写 | 否 |

### `OcrCapabilities`

文字识别的可用状态、图像尺寸限制与语言列表。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `available` | `boolean` | 可读写 | 否 |
| `maxImageDimension` | `number` | 可读写 | 否 |
| `languages` | `OcrLanguage[]` | 可读写 | 否 |
| `failureReason` | `string?` | 可读写 | 否 |

### `OcrLanguage`

文字识别语言的标记与显示名称。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `tag` | `string` | 可读写 | 否 |
| `displayName` | `string` | 可读写 | 否 |
| `nativeName` | `string` | 可读写 | 否 |

### `OcrLine`

一行文字、行范围及其文字片段。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `text` | `string` | 可读写 | 否 |
| `bounds` | `OcrBounds` | 可读写 | 否 |
| `words` | `OcrWord[]` | 可读写 | 否 |

### `OcrResult`

整张图像的识别文字、语言、方向与行信息。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `text` | `string` | 可读写 | 否 |
| `language` | `string` | 可读写 | 否 |
| `width` | `number` | 可读写 | 否 |
| `height` | `number` | 可读写 | 否 |
| `textAngle` | `number?` | 可读写 | 否 |
| `lines` | `OcrLine[]` | 可读写 | 否 |

### `OcrWord`

一个文字片段及其位置。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `text` | `string` | 可读写 | 否 |
| `bounds` | `OcrBounds` | 可读写 | 否 |

### `Option<T>`

可能有值或无值的结果；Option<T> 的值为 T。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `isNone` | `boolean` | 只读 | 否 |
| `isSome` | `boolean` | 只读 | 否 |
| `value` | `T` | 只读 | 否 |

`value` 仅在 `isSome` 为 true 时可读。模式匹配可使用 `Some(value)` 或 `None`。

### `ParameterState`

外部脚本参数的版本与快照；values 的字段由传入参数决定。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `revision` | `number` | 可读写 | 否 |
| `values` | `object` | 可读写 | 否 |

### `PinClosedInfo`

可恢复贴图的稳定标识、类别、分组和标题。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `id` | `string` | 可读写 | 否 |
| `kind` | `string` | 可读写 | 否 |
| `group` | `string` | 可读写 | 否 |
| `title` | `string` | 可读写 | 否 |

### `PinGroupInfo`

贴图分组的标识、名称、颜色和贴图数量。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `id` | `string` | 可读写 | 否 |
| `name` | `string` | 可读写 | 否 |
| `color` | `string?` | 可读写 | 否 |
| `active` | `boolean` | 可读写 | 否 |
| `open` | `number` | 可读写 | 否 |
| `total` | `number` | 可读写 | 否 |

### `PinInfo`

一张贴图的稳定标识、屏幕位置、尺寸、缩放和交互状态。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `id` | `string` | 可读写 | 否 |
| `kind` | `string` | 可读写 | 否 |
| `group` | `string` | 可读写 | 否 |
| `title` | `string` | 可读写 | 否 |
| `x` | `number` | 可读写 | 否 |
| `y` | `number` | 可读写 | 否 |
| `width` | `number` | 可读写 | 否 |
| `height` | `number` | 可读写 | 否 |
| `zoom` | `number` | 可读写 | 否 |
| `opacity` | `number` | 可读写 | 否 |
| `visible` | `boolean` | 可读写 | 否 |
| `topmost` | `boolean` | 可读写 | 否 |
| `locked` | `boolean` | 可读写 | 否 |
| `mouseThrough` | `boolean` | 可读写 | 否 |
| `canMouseThrough` | `boolean` | 可读写 | 否 |
| `thumbnail` | `boolean` | 可读写 | 否 |
| `shadow` | `boolean` | 可读写 | 否 |
| `annotationsVisible` | `boolean` | 可读写 | 否 |
| `selectable` | `boolean?` | 可读写 | 否 |
| `ignoreFormat` | `boolean?` | 可读写 | 否 |
| `scrollX` | `number` | 可读写 | 否 |
| `scrollY` | `number` | 可读写 | 否 |
| `transparentLayer` | `PinTransparentLayer?` | 可读写 | 否 |
| `canIgnoreTransparentInput` | `boolean` | 可读写 | 否 |

### `PinPlaybackState`

动图贴图的播放状态、速度、当前帧和总帧数。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `playing` | `boolean` | 可读写 | 否 |
| `speed` | `number` | 可读写 | 否 |
| `frameIndex` | `number` | 可读写 | 否 |
| `frames` | `number` | 可读写 | 否 |

### `PinSnapshot`

贴图输出的 PNG 字节、可选 HDR PNG 字节和实际颜色模式。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `image` | `Bytes` | 可读写 | 否 |
| `hdr` | `Bytes?` | 可读写 | 否 |
| `color` | `OutputColorMode` | 可读写 | 否 |

### `PinUpdate`

按稳定标识修改贴图时提供的字段。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `x` | `number` | 可读写 | 是 |
| `y` | `number` | 可读写 | 是 |
| `zoom` | `number` | 可读写 | 是 |
| `opacity` | `number` | 可读写 | 是 |
| `visible` | `boolean` | 可读写 | 是 |
| `topmost` | `boolean` | 可读写 | 是 |
| `locked` | `boolean` | 可读写 | 是 |
| `mouseThrough` | `boolean` | 可读写 | 是 |
| `group` | `string` | 可读写 | 是 |
| `title` | `string` | 可读写 | 是 |
| `thumbnail` | `boolean` | 可读写 | 是 |
| `shadow` | `boolean` | 可读写 | 是 |
| `annotationsVisible` | `boolean` | 可读写 | 是 |
| `selectable` | `boolean` | 可读写 | 是 |
| `ignoreFormat` | `boolean` | 可读写 | 是 |
| `scrollX` | `number` | 可读写 | 是 |
| `scrollY` | `number` | 可读写 | 是 |
| `transparentLayer` | `PinTransparentLayer` | 可读写 | 是 |

### `PixelEffect`

一种像素效果及其区域、强度和效果参数。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `kind` | `EffectKind` | 可读写 | 否 |
| `space` | `EffectSpace` | 可读写 | 是 |
| `region` | `EffectRect?` | 可读写 | 是 |
| `amount` | `number` | 可读写 | 是 |
| `blockSize` | `number` | 可读写 | 是 |
| `radius` | `number` | 可读写 | 是 |
| `color` | `string` | 可读写 | 是 |
| `brightness` | `number` | 可读写 | 是 |
| `contrast` | `number` | 可读写 | 是 |

### `PlaybackOptions`

修改媒体源的播放位置、速度、暂停和循环状态。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `position` | `number` | 可读写 | 是 |
| `speed` | `number` | 可读写 | 是 |
| `paused` | `boolean` | 可读写 | 是 |
| `loop` | `boolean` | 可读写 | 是 |
| `holdLast` | `boolean` | 可读写 | 是 |

### `PlaybackState`

媒体源当前的播放状态。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `position` | `number` | 可读写 | 否 |
| `speed` | `number` | 可读写 | 否 |
| `paused` | `boolean` | 可读写 | 否 |
| `loop` | `boolean` | 可读写 | 否 |
| `holdLast` | `boolean` | 可读写 | 否 |

### `PrivacyBounds`

隐私候选在图像中的矩形范围。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `x` | `number` | 可读写 | 否 |
| `y` | `number` | 可读写 | 否 |
| `width` | `number` | 可读写 | 否 |
| `height` | `number` | 可读写 | 否 |

### `PrivacyCandidate`

一处隐私检测候选及其类别、位置、文字和敏感标记。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `provider` | `string` | 可读写 | 否 |
| `kind` | `PrivacyCandidateKind` | 可读写 | 否 |
| `bounds` | `PrivacyBounds` | 可读写 | 否 |
| `text` | `string` | 可读写 | 否 |
| `boundsApproximate` | `boolean` | 可读写 | 否 |
| `isSensitive` | `boolean` | 可读写 | 否 |
| `appearanceSignature` | `string?` | 可读写 | 否 |

### `PrivacyDetectionResult`

隐私候选列表及各识别项目的失败信息。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `candidates` | `PrivacyCandidate[]` | 可读写 | 否 |
| `failures` | `PrivacyProviderFailure[]` | 可读写 | 否 |

### `PrivacyProviderFailure`

隐私检测中某一项识别不可用或失败的信息。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `provider` | `string` | 可读写 | 否 |
| `message` | `string` | 可读写 | 否 |

### `RecognitionProviderCapabilities`

表格或公式识别是否可用及不可用原因。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `available` | `boolean` | 可读写 | 否 |
| `unavailableReason` | `string?` | 可读写 | 否 |

### `RecognizedFormula`

一个识别到的 LaTeX 公式及其位置与置信度。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `latex` | `string` | 可读写 | 否 |
| `bounds` | `OcrBounds` | 可读写 | 否 |
| `detectionConfidence` | `number?` | 可读写 | 否 |

### `RecordingOptions`

启动录制时覆盖的帧率、缩小倍数、格式和捕获选项。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `fps` | `number` | 可读写 | 是 |
| `scale` | `number` | 可读写 | 是 |
| `format` | `OutputFormat` | 可读写 | 是 |
| `quality` | `GifQuality` | 可读写 | 是 |
| `cursor` | `boolean` | 可读写 | 是 |
| `sound` | `boolean` | 可读写 | 是 |
| `ffmpeg` | `boolean` | 可读写 | 是 |
| `copy` | `boolean` | 可读写 | 是 |

### `RecordingOutput`

一个录制输出的名称、路径、预设与选项。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `name` | `string` | 可读写 | 否 |
| `path` | `string` | 可读写 | 否 |
| `presetFile` | `string` | 可读写 | 是 |
| `presetName` | `string` | 可读写 | 是 |
| `options` | `RecordingOptions` | 可读写 | 是 |

### `RecordingStatus`

当前目标和录制状态，以及输出与多路录制状态。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `exists` | `boolean` | 可读写 | 否 |
| `recording` | `boolean` | 可读写 | 否 |
| `busy` | `boolean` | 可读写 | 否 |
| `idle` | `boolean` | 可读写 | 否 |
| `title` | `string` | 可读写 | 否 |
| `width` | `number` | 可读写 | 否 |
| `height` | `number` | 可读写 | 否 |
| `elapsed` | `number` | 可读写 | 否 |
| `outputs` | `string[]` | 可读写 | 否 |
| `streams` | `RecordingStreamStatus[]` | 可读写 | 否 |

### `RecordingStreamStatus`

一路录制输出的状态、帧数、时长、路径和错误。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `name` | `string` | 可读写 | 否 |
| `state` | `string` | 可读写 | 否 |
| `captured` | `number` | 可读写 | 否 |
| `encoded` | `number` | 可读写 | 否 |
| `elapsed` | `number` | 可读写 | 否 |
| `path` | `string` | 可读写 | 否 |
| `error` | `string?` | 可读写 | 否 |

### `Rectangle`

以像素为单位的矩形位置和尺寸。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `x` | `number` | 可读写 | 否 |
| `y` | `number` | 可读写 | 否 |
| `width` | `number` | 可读写 | 否 |
| `height` | `number` | 可读写 | 否 |

### `Result<T, E>`

可能成功或失败的结果；Result<T,E> 的成功值为 T，错误为 E。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `error` | `E` | 只读 | 否 |
| `isErr` | `boolean` | 只读 | 否 |
| `isOk` | `boolean` | 只读 | 否 |
| `value` | `T` | 只读 | 否 |

`value` 仅在 `isOk` 为 true 时可读；`error` 仅在 `isErr` 为 true 时可读。模式匹配可使用 `Ok(value)` 或 `Err(error)`。

### `SceneLayer`

场景中一个图层的当前位置、尺寸、显示和播放状态。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `x` | `number` | 可读写 | 否 |
| `y` | `number` | 可读写 | 否 |
| `width` | `number` | 可读写 | 否 |
| `height` | `number` | 可读写 | 否 |
| `visible` | `boolean` | 可读写 | 否 |
| `opacity` | `number` | 可读写 | 否 |
| `effects` | `PixelEffect[]` | 可读写 | 否 |
| `playback` | `PlaybackState?` | 可读写 | 否 |

### `SceneRect`

场景放置或裁剪使用的矩形。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `x` | `number` | 可读写 | 否 |
| `y` | `number` | 可读写 | 否 |
| `width` | `number` | 可读写 | 否 |
| `height` | `number` | 可读写 | 否 |

### `SceneUpdate`

批量修改一个场景图层时提供的字段。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `placement` | `SceneRect` | 可读写 | 是 |
| `crop` | `SceneRect` | 可读写 | 是 |
| `visible` | `boolean` | 可读写 | 是 |
| `opacity` | `number` | 可读写 | 是 |
| `effects` | `PixelEffect[]` | 可读写 | 是 |
| `playback` | `PlaybackOptions` | 可读写 | 是 |

### `ServiceError`

文件、媒体、识别等服务调用失败的错误信息。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `code` | `string` | 可读写 | 否 |
| `message` | `string` | 可读写 | 否 |
| `path` | `string` | 可读写 | 否 |

### `Set`

不重复值的集合。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `size` | `number` | 只读 | 否 |

### `SettingsApplyOptions`

写入或导入设置时额外提供的代理密码。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `proxyPassword` | `string` | 可读写 | 是 |

### `StaticCaptureBatchOptions`

多份静态截图共用的光标、缩放与色调映射选项。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `includeCursor` | `boolean` | 可读写 | 是 |
| `scalePercent` | `number` | 可读写 | 是 |
| `outputName` | `string` | 可读写 | 是 |
| `toneMap` | `HdrToneMapMode` | 可读写 | 是 |

### `StaticCaptureOptions`

静态截图的格式、光标、缩放与保存选项。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `format` | `StaticImageFormat` | 可读写 | 是 |
| `quality` | `number` | 可读写 | 是 |
| `background` | `string` | 可读写 | 是 |
| `overwrite` | `boolean` | 可读写 | 是 |
| `includeCursor` | `boolean` | 可读写 | 是 |
| `scalePercent` | `number` | 可读写 | 是 |
| `outputName` | `string` | 可读写 | 是 |
| `toneMap` | `HdrToneMapMode` | 可读写 | 是 |

### `StaticCaptureOutput`

一份静态截图的路径、颜色模式和格式。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `path` | `string` | 可读写 | 否 |
| `color` | `OutputColorMode` | 可读写 | 是 |
| `format` | `StaticImageFormat` | 可读写 | 是 |
| `hdrFormat` | `HdrImageFormat` | 可读写 | 是 |
| `quality` | `number` | 可读写 | 是 |
| `background` | `string` | 可读写 | 是 |

### `string`

文本。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `length` | `number` | 只读 | 否 |

### `TableCell`

识别到的表格单元格，包括跨行跨列范围、文字与类型值。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `row` | `number` | 可读写 | 否 |
| `column` | `number` | 可读写 | 否 |
| `rowSpan` | `number` | 可读写 | 否 |
| `columnSpan` | `number` | 可读写 | 否 |
| `text` | `string` | 可读写 | 否 |
| `bounds` | `OcrBounds` | 可读写 | 否 |
| `value` | `TableCellValue` | 可读写 | 是 |

### `TableCellValue`

表格单元格的布尔值、整数文本或数值。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `kind` | `TableCellValueKind` | 可读写 | 否 |
| `boolean` | `boolean` | 可读写 | 是 |
| `integer` | `string` | 可读写 | 是 |
| `number` | `number` | 可读写 | 是 |

### `TableExportCell`

手动构造待导出表格时的一格内容与合并范围。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `row` | `number` | 可读写 | 否 |
| `column` | `number` | 可读写 | 否 |
| `text` | `string\|number\|boolean` | 可读写 | 否 |
| `rowSpan` | `number` | 可读写 | 是 |
| `columnSpan` | `number` | 可读写 | 是 |
| `value` | `TableCellValue` | 可读写 | 是 |

### `TableExportData`

待导出的表格行列数、单元格与样式。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `rows` | `number` | 可读写 | 否 |
| `columns` | `number` | 可读写 | 否 |
| `cells` | `TableExportCell[]` | 可读写 | 否 |
| `outputStyle` | `TableOutputStyle` | 可读写 | 是 |

### `TableOutputStyle`

表格输出的边框与内容对齐选项。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `borders` | `TableBorderStyle` | 可读写 | 是 |
| `alignment` | `TableContentAlignment` | 可读写 | 是 |

### `TableRecognitionResult`

表格行列、单元格、识别置信度与输出样式。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `rows` | `number` | 可读写 | 否 |
| `columns` | `number` | 可读写 | 否 |
| `width` | `number` | 可读写 | 否 |
| `height` | `number` | 可读写 | 否 |
| `cells` | `TableCell[]` | 可读写 | 否 |
| `structureConfidence` | `number?` | 可读写 | 否 |
| `outputStyle` | `TableOutputStyle` | 可读写 | 是 |

### `TranslationCapabilities`

翻译服务的可用状态、语言、文本长度限制和不可用原因。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `available` | `boolean` | 可读写 | 否 |
| `configured` | `boolean` | 可读写 | 否 |
| `provider` | `string` | 可读写 | 否 |
| `sourceLanguage` | `string` | 可读写 | 否 |
| `targetLanguage` | `string` | 可读写 | 否 |
| `maximumTextLength` | `number` | 可读写 | 否 |
| `failureReason` | `string?` | 可读写 | 否 |

### `TranslationOptions`

一次翻译使用的来源语言、目标语言与超时时间。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `sourceLanguage` | `string` | 可读写 | 是 |
| `targetLanguage` | `string` | 可读写 | 是 |
| `timeoutSeconds` | `number` | 可读写 | 是 |

### `TranslationResult`

翻译后的文字与识别到的来源语言。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `text` | `string` | 可读写 | 否 |
| `detectedSourceLanguage` | `string?` | 可读写 | 否 |

### `VideoAudioInput`

额外音频输入的地址及可选输入格式。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `input` | `string` | 可读写 | 否 |
| `format` | `string` | 可读写 | 是 |

### `VideoCapabilities`

当前环境可用的视频格式、音频与 HDR 输出能力。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `available` | `boolean` | 可读写 | 否 |
| `mp4` | `boolean` | 可读写 | 否 |
| `webm` | `boolean` | 可读写 | 否 |
| `mp4Audio` | `boolean` | 可读写 | 否 |
| `webmAudio` | `boolean` | 可读写 | 否 |
| `nativeAudio` | `boolean` | 可读写 | 否 |
| `audioInputFormats` | `string[]` | 可读写 | 否 |
| `mp4Hdr` | `boolean` | 可读写 | 否 |
| `webmHdr` | `boolean` | 可读写 | 否 |

### `VideoHandle`

视频录制会话的标识、路径、取消状态及输出列表。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `id` | `string` | 可读写 | 否 |
| `path` | `string` | 可读写 | 否 |
| `cancelled` | `boolean` | 可读写 | 否 |
| `outputs` | `string[]` | 可读写 | 否 |

### `VideoOptions`

视频录制的帧率、画面、音频和保存选项。

| 字段 | 类型 | 访问 | 可省略 |
| --- | --- | --- | --- |
| `format` | `VideoFormat` | 可读写 | 是 |
| `fps` | `number` | 可读写 | 是 |
| `divisor` | `number` | 可读写 | 是 |
| `includeCursor` | `boolean` | 可读写 | 是 |
| `quality` | `number` | 可读写 | 是 |
| `color` | `OutputColorMode` | 可读写 | 是 |
| `toneMap` | `HdrToneMapMode` | 可读写 | 是 |
| `overwrite` | `boolean` | 可读写 | 是 |
| `systemAudio` | `boolean` | 可读写 | 是 |
| `systemDevice` | `string` | 可读写 | 是 |
| `microphone` | `boolean` | 可读写 | 是 |
| `microphoneDevice` | `string` | 可读写 | 是 |
| `audioInputs` | `VideoAudioInput[]` | 可读写 | 是 |

## 枚举

### `ActionKind`

功能动作脚本可以调用的动作。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `ActionKind.CaptureActiveWindow` | `CaptureActiveWindow` |
| `ActionKind.CaptureLastRegion` | `CaptureLastRegion` |
| `ActionKind.ClosePinnedImages` | `ClosePinnedImages` |
| `ActionKind.HideMainWindow` | `HideMainWindow` |
| `ActionKind.HidePinnedImages` | `HidePinnedImages` |
| `ActionKind.OpenAnimationEditor` | `OpenAnimationEditor` |
| `ActionKind.OpenImageEditor` | `OpenImageEditor` |
| `ActionKind.OpenOutputDirectory` | `OpenOutputDirectory` |
| `ActionKind.OpenScriptEditor` | `OpenScriptEditor` |
| `ActionKind.OpenVideoRecorder` | `OpenVideoRecorder` |
| `ActionKind.PinClipboard` | `PinClipboard` |
| `ActionKind.PinFile` | `PinFile` |
| `ActionKind.PinSelectedFiles` | `PinSelectedFiles` |
| `ActionKind.RestoreLastPinnedImage` | `RestoreLastPinnedImage` |
| `ActionKind.RunPreset` | `RunPreset` |
| `ActionKind.Screenshot` | `Screenshot` |
| `ActionKind.ScreenshotAndCopy` | `ScreenshotAndCopy` |
| `ActionKind.ScreenshotAndCopyCodeText` | `ScreenshotAndCopyCodeText` |
| `ActionKind.ScreenshotAndCopyText` | `ScreenshotAndCopyText` |
| `ActionKind.ScreenshotAndPin` | `ScreenshotAndPin` |
| `ActionKind.ScreenshotAndRecognizeFormula` | `ScreenshotAndRecognizeFormula` |
| `ActionKind.ScreenshotAndRecognizeTable` | `ScreenshotAndRecognizeTable` |
| `ActionKind.ScreenshotAndRecognizeText` | `ScreenshotAndRecognizeText` |
| `ActionKind.ScreenshotAndRecord` | `ScreenshotAndRecord` |
| `ActionKind.ScreenshotAndSave` | `ScreenshotAndSave` |
| `ActionKind.ScreenshotAndScroll` | `ScreenshotAndScroll` |
| `ActionKind.ScreenshotAndTranslate` | `ScreenshotAndTranslate` |
| `ActionKind.ScreenshotQuickSave` | `ScreenshotQuickSave` |
| `ActionKind.SelectRegion` | `SelectRegion` |
| `ActionKind.ShowMainWindow` | `ShowMainWindow` |
| `ActionKind.ShowPinnedImages` | `ShowPinnedImages` |
| `ActionKind.StartRecording` | `StartRecording` |
| `ActionKind.StopRecording` | `StopRecording` |
| `ActionKind.SwitchPinnedGroup` | `SwitchPinnedGroup` |
| `ActionKind.ToggleGlobalShortcuts` | `ToggleGlobalShortcuts` |
| `ActionKind.ToggleOtherPinnedImages` | `ToggleOtherPinnedImages` |
| `ActionKind.TogglePinnedImages` | `TogglePinnedImages` |
| `ActionKind.TogglePinnedMouseThrough` | `TogglePinnedMouseThrough` |
| `ActionKind.ToggleRecording` | `ToggleRecording` |
| `ActionKind.ToggleWindowTopmostAtPointer` | `ToggleWindowTopmostAtPointer` |
| `ActionKind.TranslateClipboard` | `TranslateClipboard` |

### `AnimationFormat`

动图导出的 APNG、GIF 或 WebP 格式。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `AnimationFormat.APng` | `apng` |
| `AnimationFormat.Gif` | `gif` |
| `AnimationFormat.WebP` | `webp` |

### `AnnotationArrowShape`

箭头或尺寸线的外形。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `AnnotationArrowShape.DimensionArrow` | `DimensionArrow` |
| `AnnotationArrowShape.DimensionLine` | `DimensionLine` |
| `AnnotationArrowShape.Fill` | `Fill` |
| `AnnotationArrowShape.Line` | `Line` |
| `AnnotationArrowShape.LineBilateral` | `LineBilateral` |
| `AnnotationArrowShape.Outline` | `Outline` |
| `AnnotationArrowShape.Solid` | `Solid` |
| `AnnotationArrowShape.SolidBilateral` | `SolidBilateral` |
| `AnnotationArrowShape.Straight` | `Straight` |
| `AnnotationArrowShape.StraightBilateral` | `StraightBilateral` |

### `AnnotationEndpointStyle`

线条起点和终点的端点样式。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `AnnotationEndpointStyle.ArchitecturalTick` | `ArchitecturalTick` |
| `AnnotationEndpointStyle.ClosedFilledArrow` | `ClosedFilledArrow` |
| `AnnotationEndpointStyle.Cross` | `Cross` |
| `AnnotationEndpointStyle.LineArrow` | `LineArrow` |
| `AnnotationEndpointStyle.None` | `None` |
| `AnnotationEndpointStyle.NotchedArrow` | `NotchedArrow` |
| `AnnotationEndpointStyle.OpenArrow` | `OpenArrow` |
| `AnnotationEndpointStyle.OpenCircle` | `OpenCircle` |
| `AnnotationEndpointStyle.OpenDiamond` | `OpenDiamond` |
| `AnnotationEndpointStyle.SolidArrow` | `SolidArrow` |
| `AnnotationEndpointStyle.SolidDiamond` | `SolidDiamond` |
| `AnnotationEndpointStyle.SolidDot` | `SolidDot` |
| `AnnotationEndpointStyle.UnfilledArrow` | `UnfilledArrow` |

### `AnnotationGeometryShape`

几何标注使用矩形或椭圆。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `AnnotationGeometryShape.Ellipse` | `Ellipse` |
| `AnnotationGeometryShape.Rectangle` | `Rectangle` |

### `AnnotationMagnifierLink`

放大镜与原位置之间的连接样式。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `AnnotationMagnifierLink.DotLine` | `DotLine` |
| `AnnotationMagnifierLink.Line` | `Line` |
| `AnnotationMagnifierLink.None` | `None` |
| `AnnotationMagnifierLink.Outline` | `Outline` |

### `AnnotationMarkerBlend`

荧光笔的叠色方式。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `AnnotationMarkerBlend.Multiply` | `Multiply` |
| `AnnotationMarkerBlend.Translucent` | `Translucent` |

### `AnnotationMarkerPath`

荧光笔使用线条或矩形。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `AnnotationMarkerPath.Line` | `Line` |
| `AnnotationMarkerPath.Rectangle` | `Rectangle` |

### `AnnotationMosaicMode`

像素处理使用马赛克、模糊或智能擦除。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `AnnotationMosaicMode.Blur` | `Blur` |
| `AnnotationMosaicMode.Mosaic` | `Mosaic` |
| `AnnotationMosaicMode.SmartErase` | `SmartErase` |

### `AnnotationNumberFormat`

编号的数字或字母格式。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `AnnotationNumberFormat.Chinese` | `Chinese` |
| `AnnotationNumberFormat.Decimal` | `Decimal` |
| `AnnotationNumberFormat.LowercaseLetters` | `LowercaseLetters` |
| `AnnotationNumberFormat.Roman` | `Roman` |
| `AnnotationNumberFormat.UppercaseLetters` | `UppercaseLetters` |

### `AnnotationNumberStyle`

编号的外形和填充样式。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `AnnotationNumberStyle.BorderedCircle` | `BorderedCircle` |
| `AnnotationNumberStyle.FilledCircle` | `FilledCircle` |
| `AnnotationNumberStyle.OutlineCircle` | `OutlineCircle` |
| `AnnotationNumberStyle.Plain` | `Plain` |
| `AnnotationNumberStyle.RoundedSquare` | `RoundedSquare` |

### `AnnotationPixelPath`

像素处理区域使用笔刷或矩形。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `AnnotationPixelPath.Brush` | `Brush` |
| `AnnotationPixelPath.Rectangle` | `Rectangle` |

### `AnnotationStrokeStyle`

标注线条的实线、虚线、点线或填充样式。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `AnnotationStrokeStyle.Dash` | `Dash` |
| `AnnotationStrokeStyle.DashDot` | `DashDot` |
| `AnnotationStrokeStyle.DashDotDot` | `DashDotDot` |
| `AnnotationStrokeStyle.Dot` | `Dot` |
| `AnnotationStrokeStyle.Filled` | `Filled` |
| `AnnotationStrokeStyle.Solid` | `Solid` |

### `AnnotationTool`

标注画布支持的工具种类。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `AnnotationTool.Arrow` | `Arrow` |
| `AnnotationTool.AutoMosaic` | `AutoMosaic` |
| `AnnotationTool.Blur` | `Blur` |
| `AnnotationTool.Brush` | `Brush` |
| `AnnotationTool.Ellipse` | `Ellipse` |
| `AnnotationTool.Eraser` | `Eraser` |
| `AnnotationTool.Highlight` | `Highlight` |
| `AnnotationTool.Line` | `Line` |
| `AnnotationTool.Magnifier` | `Magnifier` |
| `AnnotationTool.Mosaic` | `Mosaic` |
| `AnnotationTool.Number` | `Number` |
| `AnnotationTool.Polyline` | `Polyline` |
| `AnnotationTool.Rectangle` | `Rectangle` |
| `AnnotationTool.Select` | `Select` |
| `AnnotationTool.SmartErase` | `SmartErase` |
| `AnnotationTool.Spotlight` | `Spotlight` |
| `AnnotationTool.StrokeEraser` | `StrokeEraser` |
| `AnnotationTool.Text` | `Text` |
| `AnnotationTool.Watermark` | `Watermark` |

### `CaptureEvent`

目标或录制状态发生变化时使用的事件名。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `CaptureEvent.Cancelled` | `cancelled` |
| `CaptureEvent.Closed` | `closed` |
| `CaptureEvent.Error` | `error` |
| `CaptureEvent.Found` | `found` |
| `CaptureEvent.Lost` | `lost` |
| `CaptureEvent.Moved` | `moved` |
| `CaptureEvent.Resized` | `resized` |
| `CaptureEvent.Saved` | `saved` |
| `CaptureEvent.Stopped` | `stopped` |
| `CaptureEvent.Title` | `title` |

### `DocumentOrientation`

文档纸张的横向或纵向。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `DocumentOrientation.Automatic` | `Automatic` |
| `DocumentOrientation.Landscape` | `Landscape` |
| `DocumentOrientation.Portrait` | `Portrait` |

### `DocumentPageMode`

图像在文档页面中的适应或分页方式。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `DocumentPageMode.Fit` | `Fit` |
| `DocumentPageMode.Paginate` | `Paginate` |

### `DocumentPaginationDirection`

长图分页的方向。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `DocumentPaginationDirection.Horizontal` | `Horizontal` |
| `DocumentPaginationDirection.Vertical` | `Vertical` |

### `DocumentPaperSize`

文档输出的纸张尺寸。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `DocumentPaperSize.A3` | `A3` |
| `DocumentPaperSize.A4` | `A4` |
| `DocumentPaperSize.Custom` | `Custom` |
| `DocumentPaperSize.Letter` | `Letter` |
| `DocumentPaperSize.Original` | `Original` |

### `Easing`

场景移动动画的速度变化方式。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `Easing.EaseIn` | `ease-in` |
| `Easing.EaseInOut` | `ease-in-out` |
| `Easing.EaseOut` | `ease-out` |
| `Easing.Linear` | `linear` |
| `Easing["ease-in"]` | `ease-in` |
| `Easing["ease-in-out"]` | `ease-in-out` |
| `Easing["ease-out"]` | `ease-out` |

### `EffectKind`

像素效果的种类。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `EffectKind.Blur` | `Blur` |
| `EffectKind.BrightnessContrast` | `BrightnessContrast` |
| `EffectKind.Fill` | `Fill` |
| `EffectKind.Grayscale` | `Grayscale` |
| `EffectKind.Invert` | `Invert` |
| `EffectKind.Mosaic` | `Mosaic` |

### `EffectSpace`

效果范围采用变换前还是变换后的坐标。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `EffectSpace.Output` | `Output` |
| `EffectSpace.Source` | `Source` |

### `FormulaImageFormat`

公式渲染的图像格式。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `FormulaImageFormat.Png` | `Png` |
| `FormulaImageFormat.Svg` | `Svg` |

### `FormulaLatexFormat`

公式的 LaTeX 文本包装方式。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `FormulaLatexFormat.DisplayBrackets` | `DisplayBrackets` |
| `FormulaLatexFormat.DisplayDollar` | `DisplayDollar` |
| `FormulaLatexFormat.Equation` | `Equation` |
| `FormulaLatexFormat.EquationStar` | `EquationStar` |
| `FormulaLatexFormat.FormulaOnly` | `FormulaOnly` |
| `FormulaLatexFormat.InlineBrackets` | `InlineBrackets` |
| `FormulaLatexFormat.InlineDollar` | `InlineDollar` |

### `FormulaTextFormat`

公式文本输出的格式。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `FormulaTextFormat.AsciiMath` | `AsciiMath` |
| `FormulaTextFormat.Latex` | `Latex` |
| `FormulaTextFormat.MathMl` | `MathMl` |
| `FormulaTextFormat.Text` | `Text` |
| `FormulaTextFormat.Typst` | `Typst` |

### `GifQuality`

GIF 录制的编码质量档位。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `GifQuality.Balanced` | `Balanced` |
| `GifQuality.Best` | `Best` |
| `GifQuality.Fast` | `Fast` |

### `HdrImageFormat`

静态截图的 HDR 输出格式。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `HdrImageFormat.Exr` | `Exr` |
| `HdrImageFormat.Png` | `Png` |

### `HdrToneMapMode`

将 HDR 内容转换为 SDR 时采用的色调映射方式。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `HdrToneMapMode.HighlightCompression` | `HighlightCompression` |
| `HdrToneMapMode.ReferenceWhite` | `ReferenceWhite` |

### `ImageFormat`

图像合成和修复的输出格式。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `ImageFormat.Bmp` | `bmp` |
| `ImageFormat.Gif` | `gif` |
| `ImageFormat.Jpeg` | `jpeg` |
| `ImageFormat.Png` | `png` |
| `ImageFormat.WebP` | `webp` |

### `ImageMaskChannel`

从遮罩图片的亮度或透明度取得修复范围。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `ImageMaskChannel.Alpha` | `Alpha` |
| `ImageMaskChannel.Luminance` | `Luminance` |

### `LongCaptureSource`

长截图使用外部帧、屏幕或窗口作为输入。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `LongCaptureSource.Frames` | `Frames` |
| `LongCaptureSource.Screen` | `Screen` |
| `LongCaptureSource.Window` | `Window` |

### `MediaFormat`

媒体处理的输出格式。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `MediaFormat.APng` | `apng` |
| `MediaFormat.Gif` | `gif` |
| `MediaFormat.Png` | `png` |
| `MediaFormat.WebP` | `webp` |

### `OutputColorMode`

选择 SDR、HDR 或同时输出两者。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `OutputColorMode.Both` | `Both` |
| `OutputColorMode.Hdr` | `Hdr` |
| `OutputColorMode.Sdr` | `Sdr` |

### `OutputFormat`

动图录制的输出格式。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `OutputFormat.APng` | `APng` |
| `OutputFormat.Gif` | `Gif` |
| `OutputFormat.WebP` | `WebP` |

### `PinArrangement`

多张贴图的对齐、分布与紧凑排列方式。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `PinArrangement.AlignBottom` | `AlignBottom` |
| `PinArrangement.AlignCenter` | `AlignCenter` |
| `PinArrangement.AlignLeft` | `AlignLeft` |
| `PinArrangement.AlignMiddle` | `AlignMiddle` |
| `PinArrangement.AlignRight` | `AlignRight` |
| `PinArrangement.AlignTop` | `AlignTop` |
| `PinArrangement.Compact` | `Compact` |
| `PinArrangement.CompactHorizontal` | `CompactHorizontal` |
| `PinArrangement.CompactVertical` | `CompactVertical` |
| `PinArrangement.DistributeHorizontal` | `DistributeHorizontal` |
| `PinArrangement.DistributeVertical` | `DistributeVertical` |

### `PinCopyKind`

复制贴图的图像、文件、原内容、文字、HTML 或原始动图。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `PinCopyKind.Animation` | `Animation` |
| `PinCopyKind.File` | `File` |
| `PinCopyKind.Html` | `Html` |
| `PinCopyKind.Image` | `Image` |
| `PinCopyKind.Source` | `Source` |
| `PinCopyKind.Text` | `Text` |

### `PinOutputView`

使用贴图保存偏好、当前显示画面或完整原始画面。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `PinOutputView.Displayed` | `Displayed` |
| `PinOutputView.Original` | `Original` |
| `PinOutputView.Preferred` | `Preferred` |

### `PinTransform`

对同一贴图执行旋转、翻转、灰度或反色。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `PinTransform.FlipHorizontal` | `FlipHorizontal` |
| `PinTransform.FlipVertical` | `FlipVertical` |
| `PinTransform.Grayscale` | `Grayscale` |
| `PinTransform.Invert` | `Invert` |
| `PinTransform.RotateClockwise` | `RotateClockwise` |
| `PinTransform.RotateCounterclockwise` | `RotateCounterclockwise` |

### `PinTransparentLayer`

透明图像贴图的鼠标穿过、接收鼠标或棋盘格显示方式。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `PinTransparentLayer.AcceptingMouse` | `AcceptingMouse` |
| `PinTransparentLayer.Checkerboard` | `Checkerboard` |
| `PinTransparentLayer.IgnoreMouse` | `IgnoreMouse` |

### `PrivacyCandidateKind`

隐私检测候选的类别。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `PrivacyCandidateKind.Barcode` | `Barcode` |
| `PrivacyCandidateKind.Email` | `Email` |
| `PrivacyCandidateKind.IdentityNumber` | `IdentityNumber` |
| `PrivacyCandidateKind.ImageBlock` | `ImageBlock` |
| `PrivacyCandidateKind.ImageStructure` | `ImageStructure` |
| `PrivacyCandidateKind.PaymentCard` | `PaymentCard` |
| `PrivacyCandidateKind.Phone` | `Phone` |
| `PrivacyCandidateKind.QrCode` | `QrCode` |
| `PrivacyCandidateKind.RelatedStructure` | `RelatedStructure` |
| `PrivacyCandidateKind.Text` | `Text` |

### `ScrollCaptureAxis`

长截图沿垂直或水平方向拼接。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `ScrollCaptureAxis.Horizontal` | `Horizontal` |
| `ScrollCaptureAxis.Vertical` | `Vertical` |

### `ScrollCaptureStatus`

长截图画面匹配和拼接的结果状态。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `ScrollCaptureStatus.AlreadyCovered` | `AlreadyCovered` |
| `ScrollCaptureStatus.Ambiguous` | `Ambiguous` |
| `ScrollCaptureStatus.Appended` | `Appended` |
| `ScrollCaptureStatus.InvalidPosition` | `InvalidPosition` |
| `ScrollCaptureStatus.LimitReached` | `LimitReached` |
| `ScrollCaptureStatus.Started` | `Started` |
| `ScrollCaptureStatus.Unmatched` | `Unmatched` |

### `StaticImageFormat`

静态截图的 SDR 输出格式。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `StaticImageFormat.Avif` | `Avif` |
| `StaticImageFormat.Bmp` | `Bmp` |
| `StaticImageFormat.Jpeg` | `Jpeg` |
| `StaticImageFormat.Png` | `Png` |
| `StaticImageFormat.WebP` | `WebP` |

### `TableBorderStyle`

表格输出的边框样式。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `TableBorderStyle.All` | `All` |
| `TableBorderStyle.None` | `None` |
| `TableBorderStyle.Outer` | `Outer` |

### `TableCellValueKind`

表格单元格保存的值类型。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `TableCellValueKind.Boolean` | `Boolean` |
| `TableCellValueKind.Int64` | `Int64` |
| `TableCellValueKind.Number` | `Number` |
| `TableCellValueKind.Text` | `Text` |

### `TableContentAlignment`

表格单元格内容的对齐方式。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `TableContentAlignment.Auto` | `Auto` |
| `TableContentAlignment.Center` | `Center` |
| `TableContentAlignment.Left` | `Left` |
| `TableContentAlignment.Right` | `Right` |

### `TableOutputFormat`

表格导出的格式。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `TableOutputFormat.Csv` | `Csv` |
| `TableOutputFormat.Html` | `Html` |
| `TableOutputFormat.Tsv` | `Tsv` |
| `TableOutputFormat.Xlsx` | `Xlsx` |

### `VideoFormat`

视频录制的输出格式。

枚举成员为只读。

| 成员 | 值 |
| --- | --- |
| `VideoFormat.Mp4` | `mp4` |
| `VideoFormat.WebM` | `webm` |

## 模块中的数据成员

### `math`

| 成员 | 类型 | 访问 |
| --- | --- | --- |
| `math.e` | `number` | 只读 |
| `math.pi` | `number` | 只读 |
| `math.tau` | `number` | 只读 |

### `option`

| 成员 | 类型 | 访问 |
| --- | --- | --- |
| `option.none` | `Option<never>` | 只读 |
