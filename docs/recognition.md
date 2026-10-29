# 文字与二维码识别

`ocr` 和 `barcode` 可用于独立脚本与录制脚本。输入可以是图像文件路径或 `Bytes`；相对路径以脚本所在目录为基准。服务异步执行，停止脚本会取消等待和后续处理。失败返回 `Err(ServiceError)`，取消沿用脚本取消行为。

| 调用 | 结果 |
| --- | --- |
| `ocr.capabilities()` | `Result<OcrCapabilities,ServiceError>` |
| `ocr.recognize(image, language = null)` | `Result<OcrResult,ServiceError>` |
| `barcode.recognize(image)` | `Result<BarcodeResult[],ServiceError>` |

## 文字

先查询 `ocr.capabilities()`。`available` 表示当前系统能否识别；`languages` 列出语言的 `tag`、`displayName` 和 `nativeName`，`maxImageDimension` 为输入边长上限。不可用时查看 `failureReason`。识别不安装或下载语言模型；Windows 使用已安装的系统识别语言。

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

未识别到时返回成功的空数组。每项包含 `text`、`format` 和 `points`，各点为 `x`、`y`。这些点是解码器返回的定位特征，不代表完整边框；坐标对应经 EXIF 方向校正后的第一帧。

直接传入的编码文件最多 64 MiB；`Bytes` 沿用二进制服务的 16 MiB 上限。解码图像最多 16 Mi 像素；文字识别还受系统边长上限约束。每种服务最多同时处理两个任务，排队可以取消。超限和无效格式返回错误。服务处理本地文件和二进制图像，不执行识别文本中的链接或命令。
