# API 参考

按功能查询可调用接口。选项对象、结果对象和枚举见[类型参考](script-types-reference.md)。

签名中参数名后的 `?` 表示参数可以省略，类型后的 `?` 表示可以传入 `null`；`...` 表示剩余参数。这些标记用于展示调用约定。返回 `Result` 的接口需要检查结果或使用错误传播，见[模块与错误处理](modules-and-errors.md)。

可选参数的“省略”表示使用该接口的默认行为，不表示可以显式传入 `null`。

类型示意中，`T` 是元素或成功值的类型，`E` 是错误类型，`U` 是回调结果类型；`A`、`B`、`C` 分别表示第一、第二、第三个实参的类型。

## 全局函数

可以直接按函数名称调用。

### Map

```rgscript
Map(entries?: array): Map
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `entries` | `array` | 可选 | 空集合 |

### Set

```rgscript
Set(values?: array): Set
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `values` | `array` | 可选 | 空集合 |

### assert

```rgscript
assert(condition: boolean, message?: any): void
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `condition` | `boolean` | 必填 | — |
| `message` | `any` | 可选 | 省略 |

### breakpoint

```rgscript
breakpoint(): void
```

### cancel

```rgscript
cancel(): void
```

### copy

```rgscript
copy(): void
```

### hide

```rgscript
hide(): void
```

### len

```rgscript
len(value: any): number
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `any` | 必填 | — |

### log

```rgscript
log(...values: any): void
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `values` | `any` | 剩余参数 | 空的剩余参数 |

### number

```rgscript
number(value: any): number
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `any` | 必填 | — |

### print

```rgscript
print(...values: any): void
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `values` | `any` | 剩余参数 | 空的剩余参数 |

### select

```rgscript
select(): void
```

### show

```rgscript
show(): void
```

### start

```rgscript
start(options?: RecordingOptions): void
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `options` | `RecordingOptions` | 可选 | 省略 |

### status

```rgscript
status(): RecordingStatus
```

### stop

```rgscript
stop(): void
```

### str

```rgscript
str(value: any): string
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `any` | 必填 | — |

### wait

```rgscript
wait(seconds: number): void
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `seconds` | `number` | 必填 | — |

### waitUntil

```rgscript
waitUntil(condition: function, timeout?: number = 30): void
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `condition` | `function` | 必填 | — |
| `timeout` | `number` | 可选 | `30` |

## 功能动作

执行功能动作或创建文件贴图。

### actions.get

```rgscript
actions.get(id: string): Result<ActionInfo, string>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `id` | `string` | 必填 | — |

### actions.list

```rgscript
actions.list(): ActionInfo[]
```

### actions.pinFiles

```rgscript
actions.pinFiles(paths: string[]): Result<void, string>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `paths` | `string[]` | 必填 | — |

### actions.run

```rgscript
actions.run(kind: ActionKind, options?: ActionOptions): Result<ActionResult,string>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `kind` | `ActionKind` | 必填 | — |
| `options` | `ActionOptions` | 可选 | 省略 |

### actions.runId

```rgscript
actions.runId(id: string): Result<ActionResult,string>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `id` | `string` | 必填 | — |

## 动图编辑

打开动图或创建空白动画，编辑帧、时长与画面。

### animation.create

```rgscript
animation.create(width: number, height: number, delay?: number = 100): Result<AnimationDocument,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `width` | `number` | 必填 | — |
| `height` | `number` | 必填 | — |
| `delay` | `number` | 可选 | `100` |

### animation.open

```rgscript
animation.open(path: string): Result<AnimationDocument,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `path` | `string` | 必填 | — |

## 图像标注

打开、创建或捕获画布，再添加和编辑标注。

### annotation.capture

```rgscript
annotation.capture(): Result<AnnotationCanvas,ServiceError>
```

### annotation.create

```rgscript
annotation.create(width: number, height: number, background?: string = "transparent"): Result<AnnotationCanvas,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `width` | `number` | 必填 | — |
| `height` | `number` | 必填 | — |
| `background` | `string` | 可选 | `"transparent"` |

### annotation.open

```rgscript
annotation.open(image: string|Bytes): Result<AnnotationCanvas,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `image` | `string\|Bytes` | 必填 | — |

## 应用操作

打开设置、查询和打开日志目录，或请求退出程序。

### app.exit

```rgscript
app.exit(): void
```

### app.logDirectory

```rgscript
app.logDirectory(): string
```

### app.openLogs

```rgscript
app.openLogs(): void
```

### app.openSettings

```rgscript
app.openSettings(): void
```

## 条码识别

读取图像中的条码和二维码。

### barcode.recognize

```rgscript
barcode.recognize(image: string|Bytes): Result<BarcodeResult[],ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `image` | `string\|Bytes` | 必填 | — |

## 字节数据

创建、编码和计算字节数据。

### bytes.fromBase64

```rgscript
bytes.fromBase64(text: string): Result<Bytes,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `text` | `string` | 必填 | — |

### bytes.fromHex

```rgscript
bytes.fromHex(text: string): Result<Bytes,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `text` | `string` | 必填 | — |

### bytes.fromText

```rgscript
bytes.fromText(text: string): Result<Bytes,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `text` | `string` | 必填 | — |

## 单帧捕获

保存当前捕获范围或布局。

### capture.save

```rgscript
capture.save(output: string, options?: StaticCaptureOptions): Result<MediaInfo,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `output` | `string` | 必填 | — |
| `options` | `StaticCaptureOptions` | 可选 | 省略 |

### capture.saveOutputs

```rgscript
capture.saveOutputs(outputs: StaticCaptureOutput[], options?: StaticCaptureBatchOptions): Result<MediaInfo[],ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `outputs` | `StaticCaptureOutput[]` | 必填 | — |
| `options` | `StaticCaptureBatchOptions` | 可选 | 省略 |

## 剪贴板

读取和写入文本、HTML、文件列表及图像。

`readImage` 返回 PNG 字节；已有 PNG 会保留原始字节，其他图像格式取首帧转为 PNG。`writeImage` 接受 PNG 字节，不保证保留动画。`readFiles` 返回本地文件路径，`writeFiles` 的相对路径以脚本目录为基准。

在需要进程持续持有剪贴板内容的系统上，独立任务应使用 `--keep-alive` 保留会话，待其他程序读取后再退出。

### clipboard.clear

```rgscript
clipboard.clear(): Result<void,ServiceError>
```

### clipboard.isAvailable

```rgscript
clipboard.isAvailable(): Result<boolean,ServiceError>
```

### clipboard.readFiles

```rgscript
clipboard.readFiles(): Result<string[],ServiceError>
```

### clipboard.readHtml

```rgscript
clipboard.readHtml(): Result<string?,ServiceError>
```

### clipboard.readImage

```rgscript
clipboard.readImage(): Result<Bytes?,ServiceError>
```

### clipboard.readText

```rgscript
clipboard.readText(): Result<string?,ServiceError>
```

### clipboard.writeFiles

```rgscript
clipboard.writeFiles(paths: string[]): Result<void,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `paths` | `string[]` | 必填 | — |

### clipboard.writeHtml

```rgscript
clipboard.writeHtml(html: string, text?: string?): Result<void,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `html` | `string` | 必填 | — |
| `text` | `string?` | 可选 | 省略 |

### clipboard.writeImage

```rgscript
clipboard.writeImage(png: Bytes): Result<void,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `png` | `Bytes` | 必填 | — |

### clipboard.writeText

```rgscript
clipboard.writeText(text: string): Result<void,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `text` | `string` | 必填 | — |

## CSV

解析和生成分隔表格文本。

### csv.parse

```rgscript
csv.parse(text: string, delimiter?: string = ","): Result<string[][],ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `text` | `string` | 必填 | — |
| `delimiter` | `string` | 可选 | `","` |

### csv.stringify

```rgscript
csv.stringify(rows: string[][], delimiter?: string = ","): Result<string,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `rows` | `string[][]` | 必填 | — |
| `delimiter` | `string` | 可选 | `","` |

## 文档输出

将图像输出为文档。

### document.save

```rgscript
document.save(images: string[], output: string, options?: DocumentOutputOptions): Result<DocumentInfo,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `images` | `string[]` | 必填 | — |
| `output` | `string` | 必填 | — |
| `options` | `DocumentOutputOptions` | 可选 | 省略 |

## 事件

注册处理函数并发送事件。

### events.emit

```rgscript
events.emit(name: CaptureEvent|string): void
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `name` | `CaptureEvent\|string` | 必填 | — |

### events.off

```rgscript
events.off(name: CaptureEvent|string): void
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `name` | `CaptureEvent\|string` | 必填 | — |

### events.on

```rgscript
events.on(name: CaptureEvent|string, handler: function): void
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `name` | `CaptureEvent\|string` | 必填 | — |
| `handler` | `function` | 必填 | — |

### events.pump

```rgscript
events.pump(): void
```

## 文件与目录

读取、写入和管理文件及目录。

### files.copy

```rgscript
files.copy(source: string, destination: string, overwrite?: boolean = false): Result<string,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `source` | `string` | 必填 | — |
| `destination` | `string` | 必填 | — |
| `overwrite` | `boolean` | 可选 | `false` |

### files.createDirectory

```rgscript
files.createDirectory(path: string): Result<string,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `path` | `string` | 必填 | — |

### files.exists

```rgscript
files.exists(path: string): Result<boolean,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `path` | `string` | 必填 | — |

### files.info

```rgscript
files.info(path: string): Result<FileInfo,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `path` | `string` | 必填 | — |

### files.list

```rgscript
files.list(path: string): Result<FileInfo[],ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `path` | `string` | 必填 | — |

### files.move

```rgscript
files.move(source: string, destination: string, overwrite?: boolean = false): Result<string,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `source` | `string` | 必填 | — |
| `destination` | `string` | 必填 | — |
| `overwrite` | `boolean` | 可选 | `false` |

### files.readBytes

```rgscript
files.readBytes(path: string, maxBytes?: number = 1048576): Result<Bytes,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `path` | `string` | 必填 | — |
| `maxBytes` | `number` | 可选 | `1048576` |

### files.readText

```rgscript
files.readText(path: string, maxBytes?: number = 1048576): Result<string,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `path` | `string` | 必填 | — |
| `maxBytes` | `number` | 可选 | `1048576` |

### files.writeBytes

```rgscript
files.writeBytes(path: string, data: Bytes, overwrite?: boolean = false): Result<string,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `path` | `string` | 必填 | — |
| `data` | `Bytes` | 必填 | — |
| `overwrite` | `boolean` | 可选 | `false` |

### files.writeText

```rgscript
files.writeText(path: string, text: string, overwrite?: boolean = false): Result<string,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `path` | `string` | 必填 | — |
| `text` | `string` | 必填 | — |
| `overwrite` | `boolean` | 可选 | `false` |

## 公式

识别、转换和输出公式。

### formula.capabilities

```rgscript
formula.capabilities(): Result<RecognitionProviderCapabilities,ServiceError>
```

### formula.convert

```rgscript
formula.convert(sources: string|string[], format?: FormulaTextFormat = "Latex"): Result<string,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `sources` | `string\|string[]` | 必填 | — |
| `format` | `FormulaTextFormat` | 可选 | `"Latex"` |

### formula.document

```rgscript
formula.document(sources: string|string[]): Result<Bytes,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `sources` | `string\|string[]` | 必填 | — |

### formula.documentCapabilities

```rgscript
formula.documentCapabilities(): Result<RecognitionProviderCapabilities,ServiceError>
```

### formula.format

```rgscript
formula.format(sources: string|string[], format?: FormulaLatexFormat = "DisplayDollar"): string
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `sources` | `string\|string[]` | 必填 | — |
| `format` | `FormulaLatexFormat` | 可选 | `"DisplayDollar"` |

### formula.outputCapabilities

```rgscript
formula.outputCapabilities(): Result<RecognitionProviderCapabilities,ServiceError>
```

### formula.recognize

```rgscript
formula.recognize(image: string|Bytes): Result<FormulaRecognitionResult,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `image` | `string\|Bytes` | 必填 | — |

### formula.render

```rgscript
formula.render(sources: string|string[], format?: FormulaImageFormat = "Png", fontSize?: number = 28, textColor?: number = 4278190080): Result<Bytes,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `sources` | `string\|string[]` | 必填 | — |
| `format` | `FormulaImageFormat` | 可选 | `"Png"` |
| `fontSize` | `number` | 可选 | `28` |
| `textColor` | `number` | 可选 | `4278190080` |

### formula.unwrap

```rgscript
formula.unwrap(source: string): string
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `source` | `string` | 必填 | — |

## 图像处理

合成图像、生成帧总览和修复图像。

### image.compose

```rgscript
image.compose(output: string, options: ImageCompositionOptions): Result<MediaInfo,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `output` | `string` | 必填 | — |
| `options` | `ImageCompositionOptions` | 必填 | — |

### image.contactSheet

```rgscript
image.contactSheet(input: string, output: string, options?: ImageContactSheetOptions): Result<MediaInfo,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `input` | `string` | 必填 | — |
| `output` | `string` | 必填 | — |
| `options` | `ImageContactSheetOptions` | 可选 | 省略 |

### image.inpaint

```rgscript
image.inpaint(input: string, output: string, options: ImageInpaintingOptions): Result<MediaInfo,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `input` | `string` | 必填 | — |
| `output` | `string` | 必填 | — |
| `options` | `ImageInpaintingOptions` | 必填 | — |

## 外部输入通道

读取外部数据和等待更新。

### inputs.names

```rgscript
inputs.names(): string[]
```

### inputs.read

```rgscript
inputs.read(name: string): InputData?
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `name` | `string` | 必填 | — |

### inputs.wait

```rgscript
inputs.wait(name: string, afterVersion?: number = -1, timeout?: number = 30): InputData?
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `name` | `string` | 必填 | — |
| `afterVersion` | `number` | 可选 | `-1` |
| `timeout` | `number` | 可选 | `30` |

## JSON

解析和生成 JSON 文本。

### json.parse

```rgscript
json.parse(text: string): Result<any,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `text` | `string` | 必填 | — |

### json.stringify

```rgscript
json.stringify(value: any, pretty?: boolean = false): Result<string,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `any` | 必填 | — |
| `pretty` | `boolean` | 可选 | `false` |

## 长截图

创建长截图会话，采集或接收连续画面并拼接。

### longCapture.create

```rgscript
longCapture.create(options: LongCaptureOptions): Result<LongCaptureSession,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `options` | `LongCaptureOptions` | 必填 | — |

## 数学

数值运算、取整、三角函数和随机数。

### math.abs

```rgscript
math.abs(value: number): number
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `number` | 必填 | — |

### math.acos

```rgscript
math.acos(value: number): number
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `number` | 必填 | — |

### math.asin

```rgscript
math.asin(value: number): number
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `number` | 必填 | — |

### math.atan

```rgscript
math.atan(value: number): number
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `number` | 必填 | — |

### math.atan2

```rgscript
math.atan2(left: number, right: number): number
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `left` | `number` | 必填 | — |
| `right` | `number` | 必填 | — |

### math.ceil

```rgscript
math.ceil(value: number): number
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `number` | 必填 | — |

### math.clamp

```rgscript
math.clamp(value: number, minimum: number, maximum: number): number
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `number` | 必填 | — |
| `minimum` | `number` | 必填 | — |
| `maximum` | `number` | 必填 | — |

### math.cos

```rgscript
math.cos(value: number): number
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `number` | 必填 | — |

### math.exp

```rgscript
math.exp(value: number): number
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `number` | 必填 | — |

### math.floor

```rgscript
math.floor(value: number): number
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `number` | 必填 | — |

### math.log

```rgscript
math.log(value: number): number
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `number` | 必填 | — |

### math.log10

```rgscript
math.log10(value: number): number
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `number` | 必填 | — |

### math.max

```rgscript
math.max(value: number, ...others: number): number
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `number` | 必填 | — |
| `others` | `number` | 剩余参数 | 空的剩余参数 |

### math.min

```rgscript
math.min(value: number, ...others: number): number
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `number` | 必填 | — |
| `others` | `number` | 剩余参数 | 空的剩余参数 |

### math.pow

```rgscript
math.pow(left: number, right: number): number
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `left` | `number` | 必填 | — |
| `right` | `number` | 必填 | — |

### math.random

```rgscript
math.random(): number
```

### math.round

```rgscript
math.round(value: number): number
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `number` | 必填 | — |

### math.sign

```rgscript
math.sign(value: number): number
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `number` | 必填 | — |

### math.sin

```rgscript
math.sin(value: number): number
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `number` | 必填 | — |

### math.sqrt

```rgscript
math.sqrt(value: number): number
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `number` | 必填 | — |

### math.tan

```rgscript
math.tan(value: number): number
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `number` | 必填 | — |

### math.trunc

```rgscript
math.trunc(value: number): number
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `number` | 必填 | — |

## 媒体处理

查看媒体信息、转换、压缩和输出。

### media.inspect

```rgscript
media.inspect(path: string): Result<MediaInfo,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `path` | `string` | 必填 | — |

### media.optimize

```rgscript
media.optimize(input: string, output: string, options: MediaOptimizationOptions): Result<MediaOptimizationResult,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `input` | `string` | 必填 | — |
| `output` | `string` | 必填 | — |
| `options` | `MediaOptimizationOptions` | 必填 | — |

### media.process

```rgscript
media.process(input: string, output: string, options?: MediaOptions): Result<MediaInfo,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `input` | `string` | 必填 | — |
| `output` | `string` | 必填 | — |
| `options` | `MediaOptions` | 可选 | 省略 |

### media.processMany

```rgscript
media.processMany(input: string, outputs: MediaOutput[]): Result<MediaInfo,ServiceError>[]
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `input` | `string` | 必填 | — |
| `outputs` | `MediaOutput[]` | 必填 | — |

## 文字识别

查询识别能力并识别图像中的文字。

### ocr.capabilities

```rgscript
ocr.capabilities(): Result<OcrCapabilities,ServiceError>
```

### ocr.recognize

```rgscript
ocr.recognize(image: string|Bytes, language?: string?): Result<OcrResult,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `image` | `string\|Bytes` | 必填 | — |
| `language` | `string?` | 可选 | 省略 |

## 可选值

创建可选值，或根据空值决定是否有值。

### option.fromNullable

```rgscript
option.fromNullable(value: any): Option<A>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `any` | 必填 | — |

### option.some

```rgscript
option.some(value: any): Option<A>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `any` | 必填 | — |

## 外部参数

读取调用参数并等待参数变化。

### params.boolean

```rgscript
params.boolean(name: string, fallback?: boolean = false): boolean
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `name` | `string` | 必填 | — |
| `fallback` | `boolean` | 可选 | `false` |

### params.get

```rgscript
params.get(name: string, fallback?: any): any
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `name` | `string` | 必填 | — |
| `fallback` | `any` | 可选 | 省略 |

### params.number

```rgscript
params.number(name: string, fallback?: number = 0): number
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `name` | `string` | 必填 | — |
| `fallback` | `number` | 可选 | `0` |

### params.revision

```rgscript
params.revision(): number
```

### params.snapshot

```rgscript
params.snapshot(): object
```

### params.state

```rgscript
params.state(): ParameterState
```

### params.string

```rgscript
params.string(name: string, fallback?: string = ""): string
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `name` | `string` | 必填 | — |
| `fallback` | `string` | 可选 | `""` |

### params.waitChanged

```rgscript
params.waitChanged(revision: number): number
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `revision` | `number` | 必填 | — |

## 路径

组合路径，取得文件名、扩展名和目录。

### paths.absolute

```rgscript
paths.absolute(path: string): Result<string,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `path` | `string` | 必填 | — |

### paths.directory

```rgscript
paths.directory(path: string): Result<string,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `path` | `string` | 必填 | — |

### paths.extension

```rgscript
paths.extension(path: string): Result<string,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `path` | `string` | 必填 | — |

### paths.join

```rgscript
paths.join(...parts: string): Result<string,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `parts` | `string` | 剩余参数 | 空的剩余参数 |

### paths.name

```rgscript
paths.name(path: string): Result<string,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `path` | `string` | 必填 | — |

## 贴图

创建和管理贴图，调整显示、分组、标注及输出。

### pins.activateGroup

```rgscript
pins.activateGroup(id: string): Result<void,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `id` | `string` | 必填 | — |

### pins.adjustBrightness

```rgscript
pins.adjustBrightness(id: string, delta: number): Result<PinInfo,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `id` | `string` | 必填 | — |
| `delta` | `number` | 必填 | — |

### pins.annotate

```rgscript
pins.annotate(id: string): Result<AnnotationCanvas,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `id` | `string` | 必填 | — |

### pins.arrange

```rgscript
pins.arrange(ids: string[], arrangement: PinArrangement, allowNegativeSpacing?: boolean = false): Result<PinInfo[],ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `ids` | `string[]` | 必填 | — |
| `arrangement` | `PinArrangement` | 必填 | — |
| `allowNegativeSpacing` | `boolean` | 可选 | `false` |

### pins.close

```rgscript
pins.close(id: string, destroy?: boolean = false): Result<void,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `id` | `string` | 必填 | — |
| `destroy` | `boolean` | 可选 | `false` |

### pins.closed

```rgscript
pins.closed(): Result<PinClosedInfo[],ServiceError>
```

### pins.copy

```rgscript
pins.copy(id: string, kind?: PinCopyKind = "Image", view?: PinOutputView = "Preferred"): Result<void,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `id` | `string` | 必填 | — |
| `kind` | `PinCopyKind` | 可选 | `"Image"` |
| `view` | `PinOutputView` | 可选 | `"Preferred"` |

### pins.createColor

```rgscript
pins.createColor(color: string): Result<PinInfo,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `color` | `string` | 必填 | — |

### pins.createFiles

```rgscript
pins.createFiles(paths: string[]): Result<PinInfo,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `paths` | `string[]` | 必填 | — |

### pins.createFormula

```rgscript
pins.createFormula(latex: string): Result<PinInfo,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `latex` | `string` | 必填 | — |

### pins.createGroup

```rgscript
pins.createGroup(name: string, color?: string?): Result<PinGroupInfo,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `name` | `string` | 必填 | — |
| `color` | `string?` | 可选 | 省略 |

### pins.createHtml

```rgscript
pins.createHtml(html: string, text?: string?): Result<PinInfo,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `html` | `string` | 必填 | — |
| `text` | `string?` | 可选 | 省略 |

### pins.createImage

```rgscript
pins.createImage(image: Bytes): Result<PinInfo,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `image` | `Bytes` | 必填 | — |

### pins.createText

```rgscript
pins.createText(text: string): Result<PinInfo,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `text` | `string` | 必填 | — |

### pins.duplicate

```rgscript
pins.duplicate(id: string): Result<PinInfo,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `id` | `string` | 必填 | — |

### pins.files

```rgscript
pins.files(id: string): Result<string[],ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `id` | `string` | 必填 | — |

### pins.fit

```rgscript
pins.fit(id: string, width: number, height: number): Result<PinInfo,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `id` | `string` | 必填 | — |
| `width` | `number` | 必填 | — |
| `height` | `number` | 必填 | — |

### pins.focus

```rgscript
pins.focus(id: string): Result<void,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `id` | `string` | 必填 | — |

### pins.get

```rgscript
pins.get(id: string): Result<PinInfo?,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `id` | `string` | 必填 | — |

### pins.groups

```rgscript
pins.groups(): Result<PinGroupInfo[],ServiceError>
```

### pins.html

```rgscript
pins.html(id: string): Result<string?,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `id` | `string` | 必填 | — |

### pins.list

```rgscript
pins.list(): Result<PinInfo[],ServiceError>
```

### pins.originalImage

```rgscript
pins.originalImage(id: string): Result<Bytes,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `id` | `string` | 必填 | — |

### pins.pinFile

```rgscript
pins.pinFile(path: string): Result<PinInfo,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `path` | `string` | 必填 | — |

### pins.playback

```rgscript
pins.playback(id: string, paused?: boolean?, speed?: number?): Result<PinPlaybackState,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `id` | `string` | 必填 | — |
| `paused` | `boolean?` | 可选 | 省略 |
| `speed` | `number?` | 可选 | 省略 |

### pins.removeGroup

```rgscript
pins.removeGroup(id: string, moveTo?: string?): Result<void,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `id` | `string` | 必填 | — |
| `moveTo` | `string?` | 可选 | 省略 |

### pins.resetProcessing

```rgscript
pins.resetProcessing(id: string): Result<PinInfo,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `id` | `string` | 必填 | — |

### pins.restore

```rgscript
pins.restore(id?: string?): Result<PinInfo?,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `id` | `string?` | 可选 | 省略 |

### pins.save

```rgscript
pins.save(id: string, path: string, view?: PinOutputView = "Preferred", color?: OutputColorMode = "Sdr"): Result<string[],ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `id` | `string` | 必填 | — |
| `path` | `string` | 必填 | — |
| `view` | `PinOutputView` | 可选 | `"Preferred"` |
| `color` | `OutputColorMode` | 可选 | `"Sdr"` |

### pins.set

```rgscript
pins.set(id: string, options: PinUpdate): Result<PinInfo,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `id` | `string` | 必填 | — |
| `options` | `PinUpdate` | 必填 | — |

### pins.setGroup

```rgscript
pins.setGroup(id: string, name?: string?, color?: string?): Result<PinGroupInfo,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `id` | `string` | 必填 | — |
| `name` | `string?` | 可选 | 省略 |
| `color` | `string?` | 可选 | 省略 |

### pins.snapshot

```rgscript
pins.snapshot(id: string, view?: PinOutputView = "Preferred", color?: OutputColorMode = "Sdr"): Result<PinSnapshot,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `id` | `string` | 必填 | — |
| `view` | `PinOutputView` | 可选 | `"Preferred"` |
| `color` | `OutputColorMode` | 可选 | `"Sdr"` |

### pins.step

```rgscript
pins.step(id: string, direction: number): Result<PinPlaybackState,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `id` | `string` | 必填 | — |
| `direction` | `number` | 必填 | — |

### pins.text

```rgscript
pins.text(id: string): Result<string?,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `id` | `string` | 必填 | — |

### pins.thumbnailRegion

```rgscript
pins.thumbnailRegion(id: string, region: EffectRect): Result<PinInfo,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `id` | `string` | 必填 | — |
| `region` | `EffectRect` | 必填 | — |

### pins.transform

```rgscript
pins.transform(id: string, transform: PinTransform): Result<PinInfo,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `id` | `string` | 必填 | — |
| `transform` | `PinTransform` | 必填 | — |

## 隐私区域

检测可用于遮挡处理的图像区域。

### privacy.detect

```rgscript
privacy.detect(image: string|Bytes, language?: string?): Result<PrivacyDetectionResult,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `image` | `string\|Bytes` | 必填 | — |
| `language` | `string?` | 可选 | 省略 |

### privacy.detectAnnotations

```rgscript
privacy.detectAnnotations(image: string|Bytes, language?: string?): Result<PrivacyDetectionResult,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `image` | `string\|Bytes` | 必填 | — |
| `language` | `string?` | 可选 | 省略 |

## 录制

控制录制并读取各路输出状态。

### record.cancel

```rgscript
record.cancel(): void
```

### record.copy

```rgscript
record.copy(): void
```

### record.hide

```rgscript
record.hide(): void
```

### record.outputs

```rgscript
record.outputs(): RecordingStreamStatus[]
```

### record.select

```rgscript
record.select(): void
```

### record.show

```rgscript
record.show(): void
```

### record.start

```rgscript
record.start(options?: RecordingOptions): void
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `options` | `RecordingOptions` | 可选 | 省略 |

### record.startMany

```rgscript
record.startMany(outputs: RecordingOutput[]): void
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `outputs` | `RecordingOutput[]` | 必填 | — |

### record.status

```rgscript
record.status(): RecordingStatus
```

### record.stop

```rgscript
record.stop(): void
```

## 结果值

创建成功或错误结果，或接收函数执行中的错误。

### result.attempt

```rgscript
result.attempt(callback: function): Result<any,string>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `callback` | `function` | 必填 | — |

回调 `callback` 接收 `()`，返回 `any`。

结果类型可推断为 `Result<U,string>`。

### result.err

```rgscript
result.err(error: any): Result<never,A>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `error` | `any` | 必填 | — |

### result.ok

```rgscript
result.ok(value: any): Result<A,never>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `any` | 必填 | — |

## 画面布局

调整区域的范围、位置、效果和播放状态。

### scene.animate

```rgscript
scene.animate(name: string, x: number, y: number, width: number, height: number, seconds: number, easing?: Easing = "linear"): void
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `name` | `string` | 必填 | — |
| `x` | `number` | 必填 | — |
| `y` | `number` | 必填 | — |
| `width` | `number` | 必填 | — |
| `height` | `number` | 必填 | — |
| `seconds` | `number` | 必填 | — |
| `easing` | `Easing` | 可选 | `"linear"` |

### scene.animateFor

```rgscript
scene.animateFor(output: string, name: string, x: number, y: number, width: number, height: number, seconds: number, easing?: Easing = "linear"): void
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `output` | `string` | 必填 | — |
| `name` | `string` | 必填 | — |
| `x` | `number` | 必填 | — |
| `y` | `number` | 必填 | — |
| `width` | `number` | 必填 | — |
| `height` | `number` | 必填 | — |
| `seconds` | `number` | 必填 | — |
| `easing` | `Easing` | 可选 | `"linear"` |

### scene.crop

```rgscript
scene.crop(name: string, x: number, y: number, width: number, height: number): void
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `name` | `string` | 必填 | — |
| `x` | `number` | 必填 | — |
| `y` | `number` | 必填 | — |
| `width` | `number` | 必填 | — |
| `height` | `number` | 必填 | — |

### scene.cropFor

```rgscript
scene.cropFor(output: string, name: string, x: number, y: number, width: number, height: number): void
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `output` | `string` | 必填 | — |
| `name` | `string` | 必填 | — |
| `x` | `number` | 必填 | — |
| `y` | `number` | 必填 | — |
| `width` | `number` | 必填 | — |
| `height` | `number` | 必填 | — |

### scene.effects

```rgscript
scene.effects(name: string, effects: PixelEffect[]): void
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `name` | `string` | 必填 | — |
| `effects` | `PixelEffect[]` | 必填 | — |

### scene.effectsFor

```rgscript
scene.effectsFor(output: string, name: string, effects: PixelEffect[]): void
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `output` | `string` | 必填 | — |
| `name` | `string` | 必填 | — |
| `effects` | `PixelEffect[]` | 必填 | — |

### scene.get

```rgscript
scene.get(name: string): SceneLayer
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `name` | `string` | 必填 | — |

### scene.getFor

```rgscript
scene.getFor(output: string, name: string): SceneLayer
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `output` | `string` | 必填 | — |
| `name` | `string` | 必填 | — |

### scene.move

```rgscript
scene.move(name: string, x: number, y: number, width: number, height: number): void
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `name` | `string` | 必填 | — |
| `x` | `number` | 必填 | — |
| `y` | `number` | 必填 | — |
| `width` | `number` | 必填 | — |
| `height` | `number` | 必填 | — |

### scene.moveFor

```rgscript
scene.moveFor(output: string, name: string, x: number, y: number, width: number, height: number): void
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `output` | `string` | 必填 | — |
| `name` | `string` | 必填 | — |
| `x` | `number` | 必填 | — |
| `y` | `number` | 必填 | — |
| `width` | `number` | 必填 | — |
| `height` | `number` | 必填 | — |

### scene.opacity

```rgscript
scene.opacity(name: string, opacity: number): void
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `name` | `string` | 必填 | — |
| `opacity` | `number` | 必填 | — |

### scene.opacityFor

```rgscript
scene.opacityFor(output: string, name: string, opacity: number): void
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `output` | `string` | 必填 | — |
| `name` | `string` | 必填 | — |
| `opacity` | `number` | 必填 | — |

### scene.playback

```rgscript
scene.playback(name: string, options: PlaybackOptions): void
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `name` | `string` | 必填 | — |
| `options` | `PlaybackOptions` | 必填 | — |

### scene.playbackFor

```rgscript
scene.playbackFor(output: string, name: string, options: PlaybackOptions): void
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `output` | `string` | 必填 | — |
| `name` | `string` | 必填 | — |
| `options` | `PlaybackOptions` | 必填 | — |

### scene.set

```rgscript
scene.set(name: string, options: SceneUpdate): void
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `name` | `string` | 必填 | — |
| `options` | `SceneUpdate` | 必填 | — |

### scene.setFor

```rgscript
scene.setFor(output: string, name: string, options: SceneUpdate): void
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `output` | `string` | 必填 | — |
| `name` | `string` | 必填 | — |
| `options` | `SceneUpdate` | 必填 | — |

### scene.visible

```rgscript
scene.visible(name: string, visible: boolean): void
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `name` | `string` | 必填 | — |
| `visible` | `boolean` | 必填 | — |

### scene.visibleFor

```rgscript
scene.visibleFor(output: string, name: string, visible: boolean): void
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `output` | `string` | 必填 | — |
| `name` | `string` | 必填 | — |
| `visible` | `boolean` | 必填 | — |

## 应用设置

读取和修改应用设置。

### settings.export

```rgscript
settings.export(path: string): Result<string,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `path` | `string` | 必填 | — |

### settings.get

```rgscript
settings.get(pointer?: string = ""): Result<any,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `pointer` | `string` | 可选 | `""` |

### settings.import

```rgscript
settings.import(path: string, options?: SettingsApplyOptions): Result<void,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `path` | `string` | 必填 | — |
| `options` | `SettingsApplyOptions` | 可选 | 省略 |

### settings.reset

```rgscript
settings.reset(pointer?: string = "", options?: SettingsApplyOptions): Result<void,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `pointer` | `string` | 可选 | `""` |
| `options` | `SettingsApplyOptions` | 可选 | 省略 |

### settings.set

```rgscript
settings.set(pointer: string, value: any, options?: SettingsApplyOptions): Result<void,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `pointer` | `string` | 必填 | — |
| `value` | `any` | 必填 | — |
| `options` | `SettingsApplyOptions` | 可选 | 省略 |

## 表格

识别、编辑和输出表格。

### table.capabilities

```rgscript
table.capabilities(): Result<RecognitionProviderCapabilities,ServiceError>
```

### table.edit

```rgscript
table.edit(data: TableRecognitionResult|TableExportData): Result<TableRecognitionResult,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `data` | `TableRecognitionResult\|TableExportData` | 必填 | — |

### table.export

```rgscript
table.export(data: TableRecognitionResult|TableExportData, format?: TableOutputFormat = "Xlsx"): Result<Bytes,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `data` | `TableRecognitionResult\|TableExportData` | 必填 | — |
| `format` | `TableOutputFormat` | 可选 | `"Xlsx"` |

### table.read

```rgscript
table.read(source: string|Bytes): Result<TableRecognitionResult,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `source` | `string\|Bytes` | 必填 | — |

### table.recognize

```rgscript
table.recognize(image: string|Bytes): Result<TableRecognitionResult,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `image` | `string\|Bytes` | 必填 | — |

## 翻译

查询翻译能力并翻译文本。

### translation.capabilities

```rgscript
translation.capabilities(): Result<TranslationCapabilities,ServiceError>
```

### translation.translate

```rgscript
translation.translate(text: string, options?: TranslationOptions): Result<TranslationResult,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `text` | `string` | 必填 | — |
| `options` | `TranslationOptions` | 可选 | 省略 |

## 视频录制

录制、停止和查询视频任务。

### video.capabilities

```rgscript
video.capabilities(): Result<VideoCapabilities,ServiceError>
```

### video.start

```rgscript
video.start(output: string, options?: VideoOptions): Result<VideoHandle,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `output` | `string` | 必填 | — |
| `options` | `VideoOptions` | 可选 | 省略 |

### video.stop

```rgscript
video.stop(id: string, cancel?: boolean): Result<VideoHandle,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `id` | `string` | 必填 | — |
| `cancel` | `boolean` | 可选 | 省略 |

### video.wait

```rgscript
video.wait(id: string): Result<VideoHandle,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `id` | `string` | 必填 | — |

## 动画文档实例方法

管理动画帧、时长、循环与图像效果，并导出动画或单帧。 通过对应类型的值调用，例如 `value.addBlank(...)`。

### addBlank

```rgscript
value.addBlank(index: number, delay?: number = 100, color?: string = "transparent"): Result<void,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `index` | `number` | 必填 | — |
| `delay` | `number` | 可选 | `100` |
| `color` | `string` | 可选 | `"transparent"` |

### applyEffects

```rgscript
value.applyEffects(indices: number[], effects: PixelEffect[]): Result<void,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `indices` | `number[]` | 必填 | — |
| `effects` | `PixelEffect[]` | 必填 | — |

### close

```rgscript
value.close(): Result<void,ServiceError>
```

### delete

```rgscript
value.delete(indices: number[]): Result<void,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `indices` | `number[]` | 必填 | — |

### duplicate

```rgscript
value.duplicate(indices: number[]): Result<void,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `indices` | `number[]` | 必填 | — |

### export

```rgscript
value.export(path: string, options?: AnimationExportOptions): Result<string,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `path` | `string` | 必填 | — |
| `options` | `AnimationExportOptions` | 可选 | 省略 |

### exportFrame

```rgscript
value.exportFrame(index: number, path: string): Result<string,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `index` | `number` | 必填 | — |
| `path` | `string` | 必填 | — |

### import

```rgscript
value.import(path: string, index: number): Result<void,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `path` | `string` | 必填 | — |
| `index` | `number` | 必填 | — |

### move

```rgscript
value.move(from: number, to: number): Result<void,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `from` | `number` | 必填 | — |
| `to` | `number` | 必填 | — |

### moveSelected

```rgscript
value.moveSelected(indices: number[], direction: number): Result<number[],ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `indices` | `number[]` | 必填 | — |
| `direction` | `number` | 必填 | — |

### overlay

```rgscript
value.overlay(indices: number[], path: string, x?: number = 0, y?: number = 0): Result<void,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `indices` | `number[]` | 必填 | — |
| `path` | `string` | 必填 | — |
| `x` | `number` | 可选 | `0` |
| `y` | `number` | 可选 | `0` |

### redo

```rgscript
value.redo(): Result<void,ServiceError>
```

### replaceFrames

```rgscript
value.replaceFrames(indices: number[], path: string): Result<void,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `indices` | `number[]` | 必填 | — |
| `path` | `string` | 必填 | — |

### setDelay

```rgscript
value.setDelay(indices: number[], delay: number): Result<void,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `indices` | `number[]` | 必填 | — |
| `delay` | `number` | 必填 | — |

### setIterations

```rgscript
value.setIterations(iterations: number): Result<void,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `iterations` | `number` | 必填 | — |

### transform

```rgscript
value.transform(options: AnimationTransformOptions): Result<void,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `options` | `AnimationTransformOptions` | 必填 | — |

### undo

```rgscript
value.undo(): Result<void,ServiceError>
```

## 标注画布实例方法

编辑画布中的形状、笔迹、文字、编号和水印，并导出图像。 通过对应类型的值调用，例如 `value.add(...)`。

### add

```rgscript
value.add(tool: AnnotationTool, points: AnnotationPoint[], style?: AnnotationStyle): Result<number,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `tool` | `AnnotationTool` | 必填 | — |
| `points` | `AnnotationPoint[]` | 必填 | — |
| `style` | `AnnotationStyle` | 可选 | 省略 |

### arrow

```rgscript
value.arrow(x1: number, y1: number, x2: number, y2: number, style?: AnnotationStyle): Result<number,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `x1` | `number` | 必填 | — |
| `y1` | `number` | 必填 | — |
| `x2` | `number` | 必填 | — |
| `y2` | `number` | 必填 | — |
| `style` | `AnnotationStyle` | 可选 | 省略 |

### blur

```rgscript
value.blur(x: number, y: number, width: number, height: number, style?: AnnotationStyle): Result<number,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `x` | `number` | 必填 | — |
| `y` | `number` | 必填 | — |
| `width` | `number` | 必填 | — |
| `height` | `number` | 必填 | — |
| `style` | `AnnotationStyle` | 可选 | 省略 |

### brush

```rgscript
value.brush(points: AnnotationPoint[], style?: AnnotationStyle): Result<number,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `points` | `AnnotationPoint[]` | 必填 | — |
| `style` | `AnnotationStyle` | 可选 | 省略 |

### clear

```rgscript
value.clear(): Result<void,ServiceError>
```

### close

```rgscript
value.close(): Result<void,ServiceError>
```

### ellipse

```rgscript
value.ellipse(x: number, y: number, width: number, height: number, style?: AnnotationStyle): Result<number,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `x` | `number` | 必填 | — |
| `y` | `number` | 必填 | — |
| `width` | `number` | 必填 | — |
| `height` | `number` | 必填 | — |
| `style` | `AnnotationStyle` | 可选 | 省略 |

### erase

```rgscript
value.erase(points: AnnotationPoint[], style?: AnnotationStyle): Result<number,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `points` | `AnnotationPoint[]` | 必填 | — |
| `style` | `AnnotationStyle` | 可选 | 省略 |

### eraseStrokes

```rgscript
value.eraseStrokes(points: AnnotationPoint[]): Result<number,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `points` | `AnnotationPoint[]` | 必填 | — |

### export

```rgscript
value.export(path: string, options?: AnnotationExportOptions): Result<MediaInfo,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `path` | `string` | 必填 | — |
| `options` | `AnnotationExportOptions` | 可选 | 省略 |

### exportHdr

```rgscript
value.exportHdr(path: string, options?: AnnotationHdrExportOptions): Result<MediaInfo,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `path` | `string` | 必填 | — |
| `options` | `AnnotationHdrExportOptions` | 可选 | 省略 |

### highlight

```rgscript
value.highlight(points: AnnotationPoint[], style?: AnnotationStyle): Result<number,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `points` | `AnnotationPoint[]` | 必填 | — |
| `style` | `AnnotationStyle` | 可选 | 省略 |

### image

```rgscript
value.image(): Result<Bytes,ServiceError>
```

### imageHdr

```rgscript
value.imageHdr(): Result<Bytes,ServiceError>
```

### line

```rgscript
value.line(x1: number, y1: number, x2: number, y2: number, style?: AnnotationStyle): Result<number,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `x1` | `number` | 必填 | — |
| `y1` | `number` | 必填 | — |
| `x2` | `number` | 必填 | — |
| `y2` | `number` | 必填 | — |
| `style` | `AnnotationStyle` | 可选 | 省略 |

### magnifier

```rgscript
value.magnifier(x: number, y: number, width: number, height: number, style?: AnnotationStyle): Result<number,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `x` | `number` | 必填 | — |
| `y` | `number` | 必填 | — |
| `width` | `number` | 必填 | — |
| `height` | `number` | 必填 | — |
| `style` | `AnnotationStyle` | 可选 | 省略 |

### mosaic

```rgscript
value.mosaic(x: number, y: number, width: number, height: number, style?: AnnotationStyle): Result<number,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `x` | `number` | 必填 | — |
| `y` | `number` | 必填 | — |
| `width` | `number` | 必填 | — |
| `height` | `number` | 必填 | — |
| `style` | `AnnotationStyle` | 可选 | 省略 |

### mosaicCandidates

```rgscript
value.mosaicCandidates(candidates: PrivacyCandidate[], style?: AnnotationStyle): Result<number?,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `candidates` | `PrivacyCandidate[]` | 必填 | — |
| `style` | `AnnotationStyle` | 可选 | 省略 |

### move

```rgscript
value.move(index: number, dx: number, dy: number): Result<number,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `index` | `number` | 必填 | — |
| `dx` | `number` | 必填 | — |
| `dy` | `number` | 必填 | — |

### number

```rgscript
value.number(number: number, x: number, y: number, style?: AnnotationStyle): Result<number,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `number` | `number` | 必填 | — |
| `x` | `number` | 必填 | — |
| `y` | `number` | 必填 | — |
| `style` | `AnnotationStyle` | 可选 | 省略 |

### polyline

```rgscript
value.polyline(points: AnnotationPoint[], style?: AnnotationStyle): Result<number,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `points` | `AnnotationPoint[]` | 必填 | — |
| `style` | `AnnotationStyle` | 可选 | 省略 |

### rect

```rgscript
value.rect(x: number, y: number, width: number, height: number, style?: AnnotationStyle): Result<number,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `x` | `number` | 必填 | — |
| `y` | `number` | 必填 | — |
| `width` | `number` | 必填 | — |
| `height` | `number` | 必填 | — |
| `style` | `AnnotationStyle` | 可选 | 省略 |

### redo

```rgscript
value.redo(): Result<void,ServiceError>
```

### remove

```rgscript
value.remove(index: number): Result<void,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `index` | `number` | 必填 | — |

### replace

```rgscript
value.replace(index: number, tool: AnnotationTool, points: AnnotationPoint[], style?: AnnotationStyle): Result<number,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `index` | `number` | 必填 | — |
| `tool` | `AnnotationTool` | 必填 | — |
| `points` | `AnnotationPoint[]` | 必填 | — |
| `style` | `AnnotationStyle` | 可选 | 省略 |

### smartErase

```rgscript
value.smartErase(x: number, y: number, width: number, height: number, style?: AnnotationStyle): Result<number,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `x` | `number` | 必填 | — |
| `y` | `number` | 必填 | — |
| `width` | `number` | 必填 | — |
| `height` | `number` | 必填 | — |
| `style` | `AnnotationStyle` | 可选 | 省略 |

### spotlight

```rgscript
value.spotlight(x: number, y: number, width: number, height: number, style?: AnnotationStyle): Result<number,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `x` | `number` | 必填 | — |
| `y` | `number` | 必填 | — |
| `width` | `number` | 必填 | — |
| `height` | `number` | 必填 | — |
| `style` | `AnnotationStyle` | 可选 | 省略 |

### text

```rgscript
value.text(text: string, x: number, y: number, style?: AnnotationStyle): Result<number,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `text` | `string` | 必填 | — |
| `x` | `number` | 必填 | — |
| `y` | `number` | 必填 | — |
| `style` | `AnnotationStyle` | 可选 | 省略 |

### undo

```rgscript
value.undo(): Result<void,ServiceError>
```

### watermark

```rgscript
value.watermark(image: string|Bytes, x: number, y: number, style?: AnnotationStyle): Result<number,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `image` | `string\|Bytes` | 必填 | — |
| `x` | `number` | 必填 | — |
| `y` | `number` | 必填 | — |
| `style` | `AnnotationStyle` | 可选 | 省略 |

## Bytes实例方法

通过对应类型的值调用，例如 `value.sha256(...)`。

### sha256

```rgscript
value.sha256(): string
```

### slice

```rgscript
value.slice(start: number, end?: number?): Result<Bytes,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `start` | `number` | 必填 | — |
| `end` | `number?` | 可选 | 字节数据末尾 |

### text

```rgscript
value.text(): Result<string,ServiceError>
```

### toBase64

```rgscript
value.toBase64(): string
```

### toHex

```rgscript
value.toHex(): string
```

## 长截图会话实例方法

控制采集与暂停、拼接传入画面、读取结果并保存长截图。 通过对应类型的值调用，例如 `value.capture(...)`。

### capture

```rgscript
value.capture(): Result<LongCaptureMatch,ServiceError>
```

### close

```rgscript
value.close(): Result<boolean,ServiceError>
```

### feed

```rgscript
value.feed(image: string|Bytes|LongCaptureImage): Result<LongCaptureMatch,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `image` | `string\|Bytes\|LongCaptureImage` | 必填 | — |

### move

```rgscript
value.move(position: number): Result<boolean,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `position` | `number` | 必填 | — |

### pause

```rgscript
value.pause(): Result<boolean,ServiceError>
```

### read

```rgscript
value.read(): Result<LongCaptureImage,ServiceError>
```

### save

```rgscript
value.save(output: string, options?: LongCaptureSaveOptions): Result<MediaInfo,ServiceError>
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `output` | `string` | 必填 | — |
| `options` | `LongCaptureSaveOptions` | 可选 | 省略 |

### start

```rgscript
value.start(): Result<boolean,ServiceError>
```

### state

```rgscript
value.state(): Result<LongCaptureState,ServiceError>
```

### stop

```rgscript
value.stop(): Result<LongCaptureImage,ServiceError>
```

## Map实例方法

通过对应类型的值调用，例如 `value.clear(...)`。

### clear

```rgscript
value.clear(): void
```

### delete

```rgscript
value.delete(key: any): boolean
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `key` | `any` | 必填 | — |

### entries

```rgscript
value.entries(): array
```

### get

```rgscript
value.get(key: any): any
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `key` | `any` | 必填 | — |

### has

```rgscript
value.has(key: any): boolean
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `key` | `any` | 必填 | — |

### keys

```rgscript
value.keys(): array
```

### set

```rgscript
value.set(key: any, value: any): Map
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `key` | `any` | 必填 | — |
| `value` | `any` | 必填 | — |

### values

```rgscript
value.values(): array
```

## Option实例方法

通过对应类型的值调用，例如 `value.andThen(...)`。

### andThen

```rgscript
value.andThen(transform: function): Option
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `transform` | `function` | 必填 | — |

回调 `transform` 接收 `(arg1: T)`，返回 `Option`。

结果类型可推断为 `U|Option<never>`。

### map

```rgscript
value.map(transform: function): Option
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `transform` | `function` | 必填 | — |

回调 `transform` 接收 `(arg1: T)`，返回 `any`。

结果类型可推断为 `Option<U>`。

### okOr

```rgscript
value.okOr(error: any): Result
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `error` | `any` | 必填 | — |

结果类型可推断为 `Result<T,A>`。

### orElse

```rgscript
value.orElse(recover: function): Option
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `recover` | `function` | 必填 | — |

回调 `recover` 接收 `()`，返回 `Option`。

结果类型可推断为 `Option<T>|U`。

### unwrapOr

```rgscript
value.unwrapOr(fallback: any): any
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `fallback` | `any` | 必填 | — |

结果类型可推断为 `T|A`。

### unwrapOrElse

```rgscript
value.unwrapOrElse(fallback: function): any
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `fallback` | `function` | 必填 | — |

回调 `fallback` 接收 `()`，返回 `any`。

结果类型可推断为 `T|U`。

## Result实例方法

通过对应类型的值调用，例如 `value.andThen(...)`。

### andThen

```rgscript
value.andThen(transform: function): Result
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `transform` | `function` | 必填 | — |

回调 `transform` 接收 `(arg1: T)`，返回 `Result`。

结果类型可推断为 `U|Result<never,E>`。

### map

```rgscript
value.map(transform: function): Result
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `transform` | `function` | 必填 | — |

回调 `transform` 接收 `(arg1: T)`，返回 `any`。

结果类型可推断为 `Result<U,E>`。

### mapErr

```rgscript
value.mapErr(transform: function): Result
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `transform` | `function` | 必填 | — |

回调 `transform` 接收 `(arg1: E)`，返回 `any`。

结果类型可推断为 `Result<T,U>`。

### orElse

```rgscript
value.orElse(recover: function): Result
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `recover` | `function` | 必填 | — |

回调 `recover` 接收 `(arg1: E)`，返回 `Result`。

结果类型可推断为 `Result<T,never>|U`。

### unwrapOr

```rgscript
value.unwrapOr(fallback: any): any
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `fallback` | `any` | 必填 | — |

结果类型可推断为 `T|A`。

### unwrapOrElse

```rgscript
value.unwrapOrElse(fallback: function): any
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `fallback` | `function` | 必填 | — |

回调 `fallback` 接收 `(arg1: E)`，返回 `any`。

结果类型可推断为 `T|U`。

## Set实例方法

通过对应类型的值调用，例如 `value.add(...)`。

### add

```rgscript
value.add(value: any): Set
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `any` | 必填 | — |

### clear

```rgscript
value.clear(): void
```

### delete

```rgscript
value.delete(value: any): boolean
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `any` | 必填 | — |

### has

```rgscript
value.has(value: any): boolean
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `any` | 必填 | — |

### values

```rgscript
value.values(): array
```

## 数组实例方法

通过对应类型的值调用，例如 `value.concat(...)`。

### concat

```rgscript
value.concat(...values: any): array
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `values` | `any` | 剩余参数 | 空的剩余参数 |

### every

```rgscript
value.every(callback: function): boolean
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `callback` | `function` | 必填 | — |

回调 `callback` 接收 `(arg1: T, arg2: number, arg3: T[])`，返回 `boolean`。

### filter

```rgscript
value.filter(callback: function): array
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `callback` | `function` | 必填 | — |

回调 `callback` 接收 `(arg1: T, arg2: number, arg3: T[])`，返回 `boolean`。

结果类型可推断为 `T[]`。

### find

```rgscript
value.find(callback: function): any
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `callback` | `function` | 必填 | — |

回调 `callback` 接收 `(arg1: T, arg2: number, arg3: T[])`，返回 `boolean`。

结果类型可推断为 `T|null`。

### findIndex

```rgscript
value.findIndex(callback: function): number
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `callback` | `function` | 必填 | — |

回调 `callback` 接收 `(arg1: T, arg2: number, arg3: T[])`，返回 `boolean`。

### forEach

```rgscript
value.forEach(callback: function): void
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `callback` | `function` | 必填 | — |

回调 `callback` 接收 `(arg1: T, arg2: number, arg3: T[])`，返回 `any`。

### includes

```rgscript
value.includes(value: any, start?: number = 0): boolean
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `any` | 必填 | — |
| `start` | `number` | 可选 | `0` |

### indexOf

```rgscript
value.indexOf(value: any, start?: number = 0): number
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `any` | 必填 | — |
| `start` | `number` | 可选 | `0` |

### join

```rgscript
value.join(separator?: string = ","): string
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `separator` | `string` | 可选 | `","` |

### map

```rgscript
value.map(callback: function): array
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `callback` | `function` | 必填 | — |

回调 `callback` 接收 `(arg1: T, arg2: number, arg3: T[])`，返回 `any`。

结果类型可推断为 `(U)[]`。

### pop

```rgscript
value.pop(): any
```

结果类型可推断为 `T|null`。

### push

```rgscript
value.push(...values: any): number
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `values` | `any` | 剩余参数 | 空的剩余参数 |

### reduce

```rgscript
value.reduce(callback: function, initial?: any): any
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `callback` | `function` | 必填 | — |
| `initial` | `any` | 可选 | 以首个元素开始；空数组会报错 |

回调 `callback` 接收 `(arg1: (B|T), arg2: T, arg3: number, arg4: T[])`，返回 `any`。

可以提供一个初值。提供初值时，回调的首个参数从初值开始；省略时从数组首个元素开始。结果类型来自初值、元素和回调返回值。

### reverse

```rgscript
value.reverse(): array
```

结果类型可推断为 `T[]`。

### shift

```rgscript
value.shift(): any
```

结果类型可推断为 `T|null`。

### slice

```rgscript
value.slice(start?: number = 0, end?: number): array
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `start` | `number` | 可选 | `0` |
| `end` | `number` | 可选 | 数组末尾 |

结果类型可推断为 `T[]`。

### some

```rgscript
value.some(callback: function): boolean
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `callback` | `function` | 必填 | — |

回调 `callback` 接收 `(arg1: T, arg2: number, arg3: T[])`，返回 `boolean`。

### unshift

```rgscript
value.unshift(...values: any): number
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `values` | `any` | 剩余参数 | 空的剩余参数 |

## 对象实例方法

通过对应类型的值调用，例如 `value.delete(...)`。

### delete

```rgscript
value.delete(key: string): boolean
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `key` | `string` | 必填 | — |

### entries

```rgscript
value.entries(): array
```

### has

```rgscript
value.has(key: string): boolean
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `key` | `string` | 必填 | — |

### keys

```rgscript
value.keys(): array
```

### values

```rgscript
value.values(): array
```

## 字符串实例方法

通过对应类型的值调用，例如 `value.contains(...)`。

### contains

```rgscript
value.contains(value: string): boolean
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `string` | 必填 | — |

### endsWith

```rgscript
value.endsWith(value: string): boolean
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `string` | 必填 | — |

### includes

```rgscript
value.includes(value: string): boolean
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `string` | 必填 | — |

### indexOf

```rgscript
value.indexOf(value: string, start?: number = 0): number
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `string` | 必填 | — |
| `start` | `number` | 可选 | `0` |

### lower

```rgscript
value.lower(): string
```

### repeat

```rgscript
value.repeat(count: number): string
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `count` | `number` | 必填 | — |

### replace

```rgscript
value.replace(search: string, replacement: string): string
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `search` | `string` | 必填 | — |
| `replacement` | `string` | 必填 | — |

### replaceAll

```rgscript
value.replaceAll(search: string, replacement: string): string
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `search` | `string` | 必填 | — |
| `replacement` | `string` | 必填 | — |

### slice

```rgscript
value.slice(start?: number = 0, end?: number): string
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `start` | `number` | 可选 | `0` |
| `end` | `number` | 可选 | 字符串末尾 |

### split

```rgscript
value.split(separator: string): array
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `separator` | `string` | 必填 | — |

### startsWith

```rgscript
value.startsWith(value: string): boolean
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `value` | `string` | 必填 | — |

### substring

```rgscript
value.substring(start?: number = 0, end?: number): string
```

| 参数 | 类型 | 要求 | 省略时 |
| --- | --- | --- | --- |
| `start` | `number` | 可选 | `0` |
| `end` | `number` | 可选 | 字符串末尾 |

### trim

```rgscript
value.trim(): string
```

### trimEnd

```rgscript
value.trimEnd(): string
```

### trimStart

```rgscript
value.trimStart(): string
```

### upper

```rgscript
value.upper(): string
```
