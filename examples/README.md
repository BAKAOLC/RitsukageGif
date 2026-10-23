# 可运行范例

在编辑器中打开此文件夹，双击 `.rgpreset`，点击“运行”。`.rgs` 是供预设导入的公共模块。

## 录制场景

[file-animation.rgpreset](file-animation.rgpreset) 使用附带小动画演示文件源、实时局部效果和录制后帧总览，不需要游戏或桌面捕获。

LuaSTG 范例先打开 LuaSTGSub。下面的 LuaSTG 世界坐标采用横屏 640×480 布局。

| 文件 | 演示内容 |
| --- | --- |
| [luastg-world.rgpreset](luastg-world.rgpreset) | 按比例捕获 World 区域，手动停止，输出 GIF 并复制到剪贴板 |
| [multi-recording.rgpreset](multi-recording.rgpreset) | 四路同步录制：原画 GIF／WebP、屏幕上下文、独立动态镜头，约 6 秒 |
| [luastg-dynamic-layout.rgpreset](luastg-dynamic-layout.rgpreset) | 世界区域、椭圆画中画、移动与缩放动画、透明度变化，约 8 秒 |
| [luastg-screen-context.rgpreset](luastg-screen-context.rgpreset) | 同一窗口的内容捕获与屏幕上下文并排对比，跟随窗口、异形裁剪，约 8 秒 |
| [luastg-scripted-camera.rgpreset](luastg-scripted-camera.rgpreset) | 公共镜头模块、动态输入裁剪、尺寸事件、总览显隐、WebP，约 8 秒 |
| [scene-fallback-states.rgpreset](scene-fallback-states.rgpreset) | 状态流程、多窗口、图片与其他来源回退、多目标命名保存、APNG，约 6 秒 |

GIF 范例明确关闭 FFmpeg，边录边编码。WebP、APNG 在停止后编码。输出位于项目的 `captures`，普通单路范例同时复制到剪贴板；多路范例关闭自动复制，逐路显示文件大小。录制快捷键或命令行 `stop` 可提前停止；编辑器“停止”会取消脚本。

对比屏幕上下文时，可以用另一个窗口遮住游戏：`source screen` 会录到遮挡内容，`source window` 请求游戏窗口自己的内容。来源丢失的表现由各区域的 `missing` 配置决定。

## 文件与图像处理

这些预设不启动录制。输出会复用固定文件名，方便再次运行。

| 文件 | 演示内容 |
| --- | --- |
| [image-composition.rgpreset](image-composition.rgpreset) | 从动画选帧拼图、透明叠加、反色和动画帧总览 |
| [data-processing.rgpreset](data-processing.rgpreset) | JSON、CSV、二进制与文件摘要 |
| [media-processing.rgpreset](media-processing.rgpreset) | 使用附带图片，生成 PNG 缩略图、GIF、WebP，检查每个输出与文件大小 |
| [compress-to-budget.rgpreset](compress-to-budget.rgpreset) | 从原始 GIF 逐步调整颜色上限，尝试满足目标 MB，保留原文件 |

压缩范例默认读取之前的 LuaSTG 动态布局录制文件；可修改 `input` 的默认路径，或通过运行参数提供 `input`、`targetMB`。服务接口见[文件与图像服务](../docs/services.md)。

## 语言练习

这些预设只运行脚本，不启动录制；执行成功后在“输出”页显示通过信息。修改断言可以直接观察错误位置。

| 文件 | 主要语法 |
| --- | --- |
| [01-values-and-expressions.rgpreset](language/01-values-and-expressions.rgpreset) | 值、注释、作用域、运算与赋值、短路、条件表达式、可选访问 |
| [02-functions-and-control.rgpreset](language/02-functions-and-control.rgpreset) | 函数、箭头函数、闭包、递归、默认与剩余参数、循环、break/continue |
| [03-structures-and-collections.rgpreset](language/03-structures-and-collections.rgpreset) | 数组与对象解构、展开、切片、Map、Set、集合操作 |
| [04-types-and-patterns.rgpreset](language/04-types-and-patterns.rgpreset) | 类型注解、联合与可空类型、is、switch、守卫、属性和列表模式 |
| [05-results-and-errors.rgpreset](language/05-results-and-errors.rgpreset) | Result、Option、组合操作、`?` 传播、try/catch/finally、throw |
| [06-modules.rgpreset](language/06-modules.rgpreset) | 默认与命名导出、别名、命名空间、公共状态、模块缓存 |

语法细节见 [教程](../docs/README.md)。这些范例覆盖主要语言结构和录制流程，不是标准库每个方法的完整清单。

[LuaSTG 外部跟随](luastg-external-tracking.rgpreset)：通过命令行实时更新裁剪位置、尺寸与画面位置，详见[外部参数](../docs/editor-and-cli.md#外部参数与跟随捕获)。

多路范例的 sources/ 保存独立画面定义，入口只引用其布局与设置，不执行其他预设脚本。输出自动选用尚不存在的 captures/multi 或带数字后缀的目录。接口见[同步多路录制](../docs/multi-output.md)。

## 外部画面和数据

[external-channel.rgpreset](external-channel.rgpreset) 配合 [input-publisher.py](input-publisher.py)，展示完整外部画面和依据 JSON 坐标变化的局部镜头。Python 发送器只使用标准库；监听、启动步骤及协议见[外部输入通道](../docs/input-channels.md)。
