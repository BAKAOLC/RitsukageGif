# PDF 文档输出

截图工具栏的更多菜单提供 PDF 导出，贴图编辑中也可使用。PDF 是文档输出，不属于截图位图格式；它不会被当作普通图片重新解码。PDF 和打印使用当前画面的 SDR 图像，不更改共用的 SDR／HDR 捕获设置。

可按 Ctrl + P 打印截图或贴图，由系统对话框选择打印机和纸张；按键可在对应场景的内置快捷键中修改。Windows 使用原生打印对话框，macOS 使用系统打印面板，Linux 使用桌面打印门户。表格会按选择的纸张和边距重新分页。打印服务不可用时，按钮会禁用并显示原因。打印按钮默认隐藏，可在功能区“调整”中放入工具栏或更多菜单。取消 PDF 导出或打印会保留选区和标注。PDF 保存后沿用保存完成动作、通知和自动清理规则。

## 页面

- **原尺寸**：按图像像素和 DPI 计算物理大小，每张输入图像一页。
- **A4、A3、Letter、自定义**：选择纸张大小及方向。自动方向根据输入图像的宽高决定。
- **单页适应**：保持比例缩小到页内，不放大超过指定 DPI 对应的物理尺寸。
- **多页**：竖向按可用宽度缩小后沿高度分页；横向按可用高度缩小后沿宽度分页。最后一页保留剩余内容，不重复或丢弃边界像素。

纸张及边距使用毫米；DPI 表示每英寸图像像素数。96 像素在 96 DPI 时为 1 英寸，即 PDF 的 72 个点。每侧边距可分别设置。默认白色页底；脚本可通过 `background` 指定颜色或透明色。输入图像会按其色彩配置转换到 sRGB，透明像素按页底合成。

无损模式保留栅格图像质量；关闭后 `quality` 控制 PDF 内的图像压缩质量，不改变纸张或分页。导出采用临时文件，成功后才替换目标；取消或处理失败不会留下半份 PDF，也不会破坏已有目标文件。

## 脚本

下面是 `script { ... }` 内的示例。数组顺序决定输入图像顺序，一张输入图像可以生成多页。相对路径以入口脚本所在目录为基准。

```rgscript
const documentInfo = document.save(
    ['./first.png', './second.avif'],
    './report.pdf',
    {
        paperSize: DocumentPaperSize.A4,
        orientation: DocumentOrientation.Portrait,
        pageMode: DocumentPageMode.Paginate,
        paginationDirection: DocumentPaginationDirection.Vertical,
        margins: { left: 10, top: 10, right: 10, bottom: 10 },
        dpi: 96,
        lossless: true
    }
)?;
print(documentInfo.path, documentInfo.pages, documentInfo.bytes);
```

`document.save(images, output, options?)` 返回 `Result<DocumentInfo, ServiceError>`。`DocumentInfo` 包含 `path`、`bytes`、`pages`。默认不覆盖文件；需要覆盖时传入 `overwrite: true`。取消会终止操作，不会转成普通服务错误。

| 选项 | 默认值 | 说明 |
| --- | --- | --- |
| `paperSize` | `DocumentPaperSize.Original` | `Original`、`A4`、`A3`、`Letter`、`Custom` |
| `orientation` | `DocumentOrientation.Automatic` | `Automatic`、`Portrait`、`Landscape`；原尺寸不旋转 |
| `pageMode` | `DocumentPageMode.Fit` | `Fit`、`Paginate`；原尺寸每张输入一页 |
| `paginationDirection` | `DocumentPaginationDirection.Vertical` | `Vertical`、`Horizontal` |
| `widthMillimeters`、`heightMillimeters` | 210、297 | 自定义纸张尺寸 |
| `margins` | 各侧 0 | `left`、`top`、`right`、`bottom`，单位毫米 |
| `dpi` | 96 | 原始像素对应的物理大小 |
| `background` | `'#ffffff'` | PDF 页底色 |
| `lossless` | `true` | 无损栅格图像 |
| `quality` | 90 | 有损模式质量，0–100 |
| `overwrite` | `false` | 是否替换已有目标 |

单页最大 14400 点；过长图像请使用纸张和分页。一次最多输入 256 张图像、生成 4096 页。输入为栅格图片；动画输入使用首帧。需要逐帧导出时，先准备图片序列再传入。PDF 不作为图片输入。
