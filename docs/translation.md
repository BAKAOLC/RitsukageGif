# 翻译

可翻译截图识别的文字、贴图中的文字、识别结果及剪贴板文本。图像中的文字先在本机 OCR，再将文本发送给你配置的翻译服务。

## 配置服务

在“设置 → 常规 → 翻译”选择服务，填写完整请求地址：

| 服务 | 请求地址示例 |
| --- | --- |
| LibreTranslate | `https://你的服务/translate` |
| DeepL | `https://api-free.deepl.com/v2/translate` |
| Azure Translator | `https://api.cognitive.microsofttranslator.com/translate` |
| 兼容聊天接口 | `https://你的服务/v1/chat/completions` |

需要密钥时，输入后点击“保存密钥”。密钥使用系统保护存储，不随普通配置导出或云同步。Azure 可填写区域，兼容聊天接口需要填写模型。

软件不附带服务账号或额度。服务支持的语言、收费和用量限制由对应提供方决定。语言代码可参见 [DeepL 语言接口](https://developers.deepl.com/docs/getting-started/supported-languages)和 [Azure 支持语言](https://learn.microsoft.com/azure/ai-services/translator/language-support)。

## 使用

在截图工具栏的更多操作中选择“翻译”，或在贴图、识别结果中选择翻译。也可在功能动作中配置“截图并翻译”和“翻译剪贴板”的快捷键。

翻译窗口同时保留原文与译文，可修改原文、源语言、目标语言和备用语言。源语言与目标语言相同时可使用备用语言。正在请求时可停止；失败后原文仍在，可直接重试。复制只复制译文。

只有你执行翻译操作后才发送请求，打开设置不会发送图像或文本。当前结果为文本，不会自动改写原图中的文字。
