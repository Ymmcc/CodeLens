# CodeLens · 本地代码解释浮窗

CodeLens 是一个 Windows 托盘工具。在浏览器或编辑器里选中代码，按 **Ctrl+Alt+Q**，就能用本机 Ollama 在浮窗里看中文解释，并围绕同一个选区追问。

## 快速开始

需要 **Windows x64、.NET Framework 4.8** 和 [Ollama for Windows](https://ollama.com/download/windows)。构建脚本使用 .NET Framework 随附的 C# 编译器。

1. 在 GitHub 页面点击 **Fork**，把下面的 `YOUR_USERNAME` 换成自己的 GitHub 用户名。也可以直接克隆 [主仓库](https://github.com/Ymmcc/CodeLens)。

   ```powershell
   git clone 'https://github.com/YOUR_USERNAME/CodeLens.git'
   cd CodeLens
   .\build.ps1
   ```

2. 从开始菜单启动 Ollama，确认本机有一个支持聊天的模型。想用默认模型时，可以运行以下命令。

   ```powershell
   ollama list
   ollama pull qwen3.5:9b
   ```

3. 双击仓库目录中的 `CodeLens.exe`。它会驻留在系统托盘。选中代码后按 **Ctrl+Alt+Q**。

## 使用自己的模型

用 `ollama list` 找到本机模型的名称和标签，修改 `CodeLens.exe` 旁边的 [CodeLens.exe.config](CodeLens.exe.config)，然后从系统托盘退出并重新打开 CodeLens。修改配置后可直接运行。

```xml
<appSettings>
  <add key="OllamaModel" value="qwen3:4b" />
  <add key="OllamaPort" value="11434" />
</appSettings>
```

| 设置 | 默认值 | 说明 |
| --- | --- | --- |
| `OllamaModel` | `qwen3.5:9b` | 填写 `ollama list` 显示的本机聊天模型名称，例如 `qwen3:4b`。名称省略标签时使用 `:latest`。 |
| `OllamaPort` | `11434` | Ollama 在 `127.0.0.1` 上监听的端口，范围为 1–65535。留空或填写无效值时使用默认端口。 |

CodeLens 会先读取 Ollama 的已安装模型列表，再向所选模型发送聊天请求。Ollama 未运行时，CodeLens 会尝试从标准安装位置或 `PATH` 找到 `ollama.exe`，并在配置的端口后台启动服务。手动运行 Ollama 时，请让 `OLLAMA_HOST` 的端口和 `OllamaPort` 一致。 [Ollama 配置说明](https://docs.ollama.com/faq)

## 浮窗操作

- **原文** 可展开本次选区开头的预览。拖动标题栏可移动浮窗。
- 浮窗会随着内容增高，到达高度上限后可在内部滚动。切回编辑器或浏览器时，当前对话会保留。
- 解释完成后，可以在底部输入问题。按 **Enter** 或点击 **发送**。工具根据选区占用的上下文给出 1–4 次追问额度，每次向模型提供原选区和最近一轮问答。
- 按 **Esc** 或点击关闭按钮收起。选中新代码后再次按快捷键可开启新对话。右键托盘图标可退出。

## 本地数据

CodeLens 通过 Windows UI Automation 读取当前前台窗口的文本选区，上限为 7,000 个字符。选区、问题和相关对话只发给配置端口上的环回地址 `127.0.0.1`。CodeLens 不读取剪贴板，也不把对话写入磁盘。

CodeLens 会拒绝 Ollama 模型列表中标为远程的模型。由 CodeLens 启动的 Ollama 会禁用云功能。如果你自行运行 Ollama，也可以在服务中启用本地专用模式。 [Ollama 本地专用模式](https://docs.ollama.com/faq#how-do-i-disable-ollamas-cloud-features)

## 常见问题

| 现象 | 检查方式 |
| --- | --- |
| 提示找不到模型 | 运行 `ollama list`，核对配置中的完整模型名和标签。确保列出的模型支持聊天，并已下载到本机。 |
| 无法连接 Ollama | 启动 Ollama，确认端口一致。默认端口可在 PowerShell 运行 `Invoke-RestMethod http://127.0.0.1:11434/api/tags` 检查。 |
| 快捷键无法注册 | 在系统托盘确认 CodeLens 已启动，检查 **Ctrl+Alt+Q** 是否被其他程序占用。 |
| 应用无法提供选区 | 在支持 Windows UI Automation 文本选区的编辑器或浏览器里试试，例如 Chrome 的 GitHub 代码页。 |
| 模型加载慢或内存不足 | 首次推理通常需要加载模型。可换成本机已有的较小聊天模型。 |
| 构建找不到编译器 | 确认 Windows x64 已启用 .NET Framework 4.8，且 C# 编译器位于系统的 `Microsoft.NET\Framework64\v4.0.30319` 目录。 |

## 贡献与许可证

欢迎通过 [Issues](https://github.com/Ymmcc/CodeLens/issues) 报告选区兼容性或模型问题。提交 Pull Request 前，请运行 `.\build.ps1`，并验证选区读取、首次解释和一次追问。报告问题时可以附上复现步骤、Windows 版本和模型标签，示例代码请使用可公开的片段。

CodeLens 使用 [MIT 许可证](LICENSE)。模型权重适用各自的许可证。
