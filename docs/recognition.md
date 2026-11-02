# 图像识别与公式输出

`ocr` 和 `barcode` 可用于独立脚本与录制脚本。输入可以是图像文件路径或 `Bytes`；相对路径以脚本所在目录为基准。服务异步执行，停止脚本会取消等待和后续处理。失败返回 `Err(ServiceError)`，取消沿用脚本取消行为。

| 调用 | 结果 |
| --- | --- |
| `ocr.capabilities()` | `Result<OcrCapabilities,ServiceError>` |
| `ocr.recognize(image, language = null)` | `Result<OcrResult,ServiceError>` |
| `barcode.recognize(image)` | `Result<BarcodeResult[],ServiceError>` |

## 文字

先查询 `ocr.capabilities()`。`available` 表示当前系统能否识别；`languages` 列出语言的 `tag`、`displayName` 和 `nativeName`，`maxImageDimension` 为输入边长上限。不可用时查看 `failureReason`。识别在本机处理，不会将图像发送到在线服务。

- Windows 使用已安装的系统识别语言。
- macOS 使用系统 Vision，发行包包含独立的 OCR 辅助程序。
- Linux 使用 Tesseract。DEB 会安装识别引擎与英语语言包；其他语言按需通过系统包管理器安装，例如 `tesseract-ocr-chi-sim`。使用压缩包时需自行安装 `tesseract-ocr` 和所需语言包。

语言列表来自当前引擎实际可用的语言。指定未安装的语言会返回错误，不会悄悄换成另一种语言。识别调用本身不会安装或下载模型。

```rgscript
let capabilities = ocr.capabilities()?;
if (capabilities.available) {
    let result = ocr.recognize("capture.png")?;
    print(result.text);
    for (let line of result.lines) {
        print(line.text);
        print(line.bounds.x);
    }
}
return result.ok(null);
```

`OcrResult` 包含 `text`、`language`、`width`、`height`、可为空的 `textAngle` 和 `lines`。每行包含 `text`、`bounds`、`words`；每个词包含 `text` 和 `bounds`。边界为 `x`、`y`、`width`、`height`，单位为经 EXIF 方向校正后的图像像素。动图读取第一帧。需要语言时传入能力列表中的语言标签，例如 `ocr.recognize("capture.png", "en-US")`。

## 二维码和条码

```rgscript
let codes = barcode.recognize("code.png")?;
for (let code of codes) {
    print(code.format);
    print(code.text);
}
return result.ok(null);
```

未识别到时返回成功的空数组。每项包含 `text`、`format` 和 `points`，各点为 `x`、`y`。`points` 是解码器检测到的边界角点，坐标对应经 EXIF 方向校正后的第一帧。

图像路径与 `Bytes` 输入最多 64 MiB；创建或读取 `Bytes` 的 API 另有自身上限。解码图像最多 16 Mi 像素；文字识别还受系统边长上限约束。每种服务最多同时处理两个任务，排队可以取消。超限和无效格式返回错误。服务处理本地文件和二进制图像，不执行识别文本中的链接或命令。

## 表格

`table.recognize(image)` 返回行列结构和单元格文字。表格识别需要在识别结果窗口中安装离线组件；可先用 `table.capabilities()` 检查。图像在本机处理。

| 调用 | 结果 |
| --- | --- |
| `table.capabilities()` | `Result<RecognitionProviderCapabilities,ServiceError>` |
| `table.recognize(image)` | `Result<TableRecognitionResult,ServiceError>` |
| `table.read(source)` | `Result<TableRecognitionResult,ServiceError>`，读取 XLSX 路径或 `Bytes` |
| `table.edit(data)` | `Result<TableRecognitionResult,ServiceError>`，打开表格编辑窗口 |
| `table.export(data, format = TableOutputFormat.Xlsx)` | `Result<Bytes,ServiceError>` |

结果包含 `rows`、`columns`、图像像素大小 `width` / `height`、`cells` 和可为空的 `structureConfidence`。单元格的 `row`、`column` 从 0 开始，`rowSpan`、`columnSpan` 是合并跨度；`text` 为显示文字，`bounds` 对应原图像素。纯文字单元格可直接修改 `text`。

```rgscript
let sheet = table.recognize("table.png")?;
for (let cell of sheet.cells) {
    cell.text = cell.text.trim();
}
let workbook = table.export(sheet, TableOutputFormat.Xlsx)?;
files.writeBytes("table.xlsx", workbook)?;
return result.ok(null);
```

导出支持 `Xlsx`、`Csv`、`Tsv`、`Html`。也可以传入自己构造的 `{rows, columns, cells}`，每个单元格至少包含 `row`、`column`、`text`；省略跨度时按 1 处理，空位输出为空单元格。重叠或越界的单元格会返回错误。一次导出最多 100 万个网格位置、3200 万字符。XLSX 保留合并结构以及文字、布尔和数值类型；CSV、TSV、HTML 使用对应的显示文字。

### 单元格类型

构造数据时，`text` 可直接传字符串、布尔值或数值。字符串会原样保留，例如 `"00123"`。需要明确指定类型时，传入 `value`：

| `value.kind` | 值字段 |
| --- | --- |
| `TableCellValueKind.Text` | 使用单元格的 `text` 字符串 |
| `TableCellValueKind.Boolean` | `boolean`，布尔值 |
| `TableCellValueKind.Int64` | `integer`，十进制整数字符串 |
| `TableCellValueKind.Number` | `number`，有限数值 |

`Int64` 的范围为 `-9223372036854775808` 至 `9223372036854775807`。使用字符串可避免脚本数值的浮点精度损失。有类型的单元格以 `value` 为准，显示文字会随值更新；纯文字单元格省略 `value`。日期文字会保持为文字。

```rgscript
let sheet = {
    rows: 2,
    columns: 3,
    cells: [
        { row: 0, column: 0, text: "00123" },
        { row: 0, column: 1, text: true },
        { row: 0, column: 2, text: 1.25 },
        {
            row: 1, column: 0, text: "",
            value: { kind: TableCellValueKind.Int64, integer: "9223372036854775807" }
        }
    ]
};
files.writeBytes("typed.xlsx", table.export(sheet)?)?;
let loaded = table.read("typed.xlsx")?;
let edited = table.edit(loaded)?;
files.writeBytes("edited.xlsx", table.export(edited)?)?;
return result.ok(null);
```

`table.read` 读取首个工作表的单元格值和合并跨度，不执行公式，也不导入原工作簿的样式。`table.export` 不会启动 Excel；Excel 客户端自身的数字精度限制仍适用，编号等需要逐字保留的内容可使用字符串。

`table.edit` 在桌面程序中打开编辑窗口，关闭后返回修改后的表格；停止脚本会取消编辑。缺少界面服务时返回 `unavailable`，没有编辑结果时返回 `cancelled` 错误。读取、编辑和导出已有表格均不需要识别模型。

表格识别结果可能需要纠正，尤其是没有表头或边框的布局。结果窗口中可直接编辑单元格，选择范围，合并、拆分、插删行列，再复制或保存。布尔值可点击复选框切换；`F2` 编辑当前格，`Esc` 放弃当前修改，`Delete` 清空选中的格。拖动行列标题的边界可调整尺寸，双击边界可适应内容。

## 公式

`formula.recognize(image)` 检测图中的公式区域，并分别识别为 LaTeX。`formula.capabilities()` 检查离线模型；缺失时可在公式识别窗口安装。没有检测到独立区域时，会尝试按一张公式识别输入图；空白图像返回空列表。

| 调用 | 结果 |
| --- | --- |
| `formula.capabilities()` | `Result<RecognitionProviderCapabilities,ServiceError>` |
| `formula.recognize(image)` | `Result<FormulaRecognitionResult,ServiceError>` |
| `formula.outputCapabilities()` | `Result<RecognitionProviderCapabilities,ServiceError>` |
| `formula.render(sources, format = FormulaImageFormat.Png, fontSize = 28, textColor = 0xff000000)` | `Result<Bytes,ServiceError>` |
| `formula.convert(sources, format = FormulaTextFormat.Latex)` | `Result<string,ServiceError>` |
| `formula.documentCapabilities()` | `Result<RecognitionProviderCapabilities,ServiceError>` |
| `formula.document(sources)` | `Result<Bytes,ServiceError>`，可编辑公式的 DOCX |
| `formula.format(sources, format = FormulaLatexFormat.DisplayDollar)` | `string` |
| `formula.unwrap(source)` | `string` |

识别结果包含 `width`、`height` 和 `formulas`。每项包含 `latex`、原图像素 `bounds` 和可为空的 `detectionConfidence`。检测分数衡量公式区域定位，不代表 LaTeX 内容完全正确。

`sources` 使用 LaTeX 公式本体，可传一段字符串或字符串数组；数组保留各公式边界。已有带分隔符的代码可先用 `formula.unwrap` 处理。PNG / SVG 将多式纵向排版。`fontSize` 为输出图像像素，`textColor` 为 ARGB 数值。PNG 与 SVG 都返回编码后的 `Bytes`，可直接用 `files.writeBytes` 保存；需要 SVG 文字时调用返回值的 `.text()?`。渲染和转换不需要下载识别模型；语法无效或目标格式不支持时返回错误。

`FormulaTextFormat` 包含 `Latex`、`Text`、`MathMl`、`AsciiMath`、`Typst`。MathML 使用真实分数、根号、脚标和矩阵结构；Typst 返回数学模式内容，需要放在 `$...$` 内使用。支持常见数学表达式，不执行任意 TeX 文档、宏包或外部命令。

`formula.document` 将每个公式写入独立的可编辑 Word 公式段落，不会转为图片，也不会启动 Word。用 `files.writeBytes("formulas.docx", formula.document(sources)?)?` 保存。任一公式无法转换时返回错误，不会跳过它生成不完整文档。

```rgscript
let recognized = formula.recognize("formulas.png")?;
let sources = [];
for (let item of recognized.formulas) {
    sources.push(item.latex);
}
if (sources.length > 0) {
    let png = formula.render(sources)?;
    files.writeBytes("formulas.png", png)?;
    print(formula.convert(sources, FormulaTextFormat.MathMl)?);
}
return result.ok(null);
```

公式结果窗口默认显示全部识别项；单项可编辑 LaTeX 并即时预览。多项的“全部”视图只读；可以逐项纠正、移除误识别项，再复制或保存。原图的显示状态会记住。

结果窗口的“设置 → LaTeX 格式”可选择七种外包装：仅公式、`$…$`、`$$…$$`、`\[…\]`、`\(…\)`、`equation`、`equation*`。默认是 `$$…$$`。选择会即时记住，影响代码显示、代码或纯文本复制以及 `.tex` 保存；渲染和结构格式转换仍使用公式本体。结果窗的纯文本复制保留当前显示的 LaTeX 代码；脚本 `FormulaTextFormat.Text` 提供可读数学文本转换。

`formula.format` 和 `formula.unwrap` 是纯字符串操作，不依赖识别组件或捕获会话。`unwrap` 识别完整配对的分隔符，只剥一层并去掉内部两端空白；残缺的输入保持原样。例如：

```rgscript
let code = formula.format("x^2 + y^2 = 1", FormulaLatexFormat.DisplayDollar);
print(code);
let body = formula.unwrap(code);
print(body);
```

`FormulaLatexFormat` 的值为 `FormulaOnly`、`InlineDollar`、`DisplayDollar`、`DisplayBrackets`、`InlineBrackets`、`Equation`、`EquationStar`。`format` 接受一式或字符串数组；多式分别包装后用换行连接。`unwrap` 每次处理一式，不能用它拆分整篇 LaTeX 文档。

## 离线组件

表格组件使用 SLANet 结构识别和本地文字识别；公式组件使用区域检测和 LaTeX 解码模型。模型和 ONNX Runtime 在点击安装后单独下载到应用数据目录，不随主程序重复打包。它们不上传截图，不需要登录。支持的系统和处理器取决于可用的 ONNX Runtime CPU 发行包。

结构化识别输入最多 64 MiB，解码后最多 32 Mi 像素；只读取动图的第一帧并校正 EXIF 方向。模型和公式解析在隔离进程中执行；取消正在运行的任务会结束该进程，下次请求重新启动。模型仍可能识别错误，应根据原图检查输出。

### 截图内的码检测

在“设置 → 截图 → 二维/条形码检测”选择：完全禁用、手动启用、自动启用或直接显示内容。默认完全禁用。启用后只分析当前冻结截图，不连续扫描桌面。支持普通 QR、Micro QR、rMQR、Data Matrix、Aztec、PDF417，以及 EAN/UPC、Code 39/93/128、ITF、DataBar 等一维码。

选区包含识别结果时，选框旁会显示码检测按钮。按 `Q` 或点击它显示／隐藏结果；该键可在内置快捷键中修改。点击码上的按钮查看内容，内容窗口可选中文字、复制内容或打开链接。“直接显示内容”会直接展开内容。检测框和内容窗口不会写入截图。

“识别并复制码内容”可添加到截图工具栏，也可设置为功能动作。它使用包含标注的选区，复制最靠上、同高度最靠左的码内容后结束截图；没有识别结果时同样结束并显示简短状态。它与打开识别结果窗口的操作相互独立。
