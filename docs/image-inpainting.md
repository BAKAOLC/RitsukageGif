# 智能擦除

在截图或贴图的马赛克工具组中选择「智能擦除」，用笔刷涂抹要移除的内容，或画出矩形范围。圆角、笔刷宽度和选区限制与同组工具一致。采样半径决定参与修复的邻近范围，默认 4 像素。操作可以撤销、重做，也可以重新调整范围。

它会利用范围外的已知画面重建被覆盖部分，适合擦除细线、小块文字、污点，以及平滑背景上的小物体。范围外的像素保持不变。修复针对当前对象下面的图层；后画的标注不会被取样。

算法在本机运行，不需要下载模型或上传画面。它不能恢复原本不可见的真实内容；大块图案、复杂纹理和较宽的缺失区域可能产生模糊或接缝。先查看预览，不满意时缩小范围或撤销。修复范围必须留有可取样的外部画面；覆盖整个画面时会提示错误，不会输出未处理的结果。

SDR、16 位 PNG 和 HDR 使用相同的区域遮罩。HDR 直接处理线性像素，保留高光、负色值和透明度；16 位 PNG 通过原色彩配置转换到浮点线性空间处理，再转换回去。

算法依据 [Telea 的图像修复论文](https://webspace.science.uu.nl/~telea001/uploads/PAPERS/JGT04/paper.pdf)中的边界推进方法独立实现，并限制梯度外推以减轻色彩过冲。没有使用参考软件或论文附带实现的代码。

## 脚本调用

`image.inpaint(input, output, options)` 返回 `Result<MediaInfo, ServiceError>`。成功后可以读取文件路径、大小和尺寸；失败或取消不会替换原目标文件。

```rgscript
let result = image.inpaint("frame.png", "clean.png", {
    regions: [{ x: 120, y: 80, width: 60, height: 24 }],
    radius: 4,
    format: ImageFormat.Png
});
if (result.isOk) log(result.value.bytes);
else log(result.error);
```

`regions` 可以包含多个矩形。也可以提供同尺寸的遮罩图片：默认白色表示修复、黑色表示保留；使用 `ImageMaskChannel.Alpha` 时改为读取透明度。

```rgscript
let result = image.inpaint("frame.png", "clean.webp", {
    mask: "mask.png",
    maskChannel: ImageMaskChannel.Alpha,
    radius: 4,
    lossless: true,
    overwrite: true
});
```

同时提供 `mask` 和 `regions` 时，仅修复两者重合的部分。路径相对于脚本工作目录。遮罩图片使用第 0 帧；动图输入通过 `frameIndex` 选择完整呈现的一帧，默认也是第 0 帧，输出为单张图像。

输出格式为 `ImageFormat.Png`、`WebP`、`Gif`、`Jpeg` 或 `Bmp`，默认从目标扩展名确定。`quality` 为 1–100，默认 90；`lossless` 控制 WebP 无损编码，默认开启。

16 位 PNG 和 APNG 选帧保存为 PNG 时保留精度和色彩配置，也支持 PQ HDR PNG。选择其他格式时转为 SDR；HDR 使用现有高光压缩映射。JPEG 有损压缩、GIF 量化以及 SDR 格式转换本身可能改变像素，不等同于 PNG 无损输出。
