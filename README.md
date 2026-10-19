# GeometryGrid

**面向网格几何题的 Windows 作图工作台。** 支持动态约束作图、JSON 题图、构造搜索、逐步演示，以及图片和高清视频导出。

当前源码基于 **v3.02**，以 **Apache License 2.0** 开源。

**[下载最新 Windows x64 便携包](https://github.com/tjhcl/GeometryGrid/releases/latest)** · [发布与检查说明](SECURITY_AUDIT.md)

内置示例已更新为维护者提供的“2026 天津中考第 18 题”：`examples/tianjin_2026_q18.json`。

## 能做什么

- **直接作图**：点、线段、直线、射线、平行线、垂线、圆、圆弧；自动吸附网格与交点，支持撤销、重做与实时预览。
- **不定图与参数**：用表达式定义坐标和依赖关系，拖动动点时联动更新几何对象。
- **搜索构造**：指定目标坐标和起始—最大笔数，选择共线反推、启发式或穷举策略；支持多核 CPU 和实验性 GPU 功能。
- **逐步讲解**：暂停、上一步、下一步；强调取点、缓动落笔、交点确认，保留自定义样式。
- **离线导出**：PNG、JPG、EMF、GIF，以及 H.264/MP4 高清或 4K、30/60 帧视频。
- **AI 协作建模**：把题图转为 JSON，再导入软件编辑与搜索。仓库附有 JSON 创建规范和 AI 提示词。

> 搜索受策略、笔数、状态上限及数值误差影响；找到的构造不必然是全局最优，采样验证也不代替完整数学证明。

## 从源码运行

要求 **Windows 10/11 x64、.NET 8 SDK 8.0.423 或更新的 8.0 SDK**。首次构建需要联网恢复 NuGet 依赖和字体。

```powershell
git clone https://github.com/tjhcl/GeometryGrid.git
cd GeometryGrid
dotnet restore GeometryGrid.sln
dotnet build GeometryGrid.sln -c Release
dotnet test tests/GeometryGrid.Tests/GeometryGrid.Tests.csproj -c Release
dotnet run --project src/GeometryGrid.App/GeometryGrid.App.csproj -c Release
```

界面使用 **Noto Sans CJK SC**（SIL OFL 1.1）。首次构建自动从固定上游提交下载字体，并核对 SHA-256；之后可使用本地缓存。此公开副本不包含没有再分发授权证明的 Product Sans / 方正字体。标题栏重用项目自身的多分辨率 ICO 图标。

### 打包 Windows 便携版

```powershell
dotnet publish src/GeometryGrid.App/GeometryGrid.App.csproj -c Release -o artifacts/app
dotnet publish src/GeometryGrid.Cli/GeometryGrid.Cli.csproj -c Release -o artifacts/cli
```

将桌面程序、命令行求解器、使用说明、JSON 规范、AI 提示词、`LICENSE`、`NOTICE`、`THIRD-PARTY-NOTICES.md` 和字体 OFL 一并纳入发布包。不要把旧的、嵌有未核实授权字体的本地发布包直接重新分发。

也可在 PowerShell 中执行 `./scripts/Build-Release.ps1`，自动测试、构建、按显式清单生成便携 ZIP 及 `SHA256SUMS.txt`。本项目的 GitHub Release 由同一脚本从发布提交构建。

### 命令行暴算

```powershell
dotnet run --project src/GeometryGrid.Cli -- search examples/tianjin_2026_q18.json --min-steps 3 --max-steps 5 --threads 8 --out solution.ggsteps.json
dotnet run --project src/GeometryGrid.Cli -- replay examples/tianjin_2026_q18.json solution.ggsteps.json
```

## 文档与示例

- [使用说明](使用说明.md)
- [JSON 创建规范](JSON创建规范_README.md)
- [AI 题图转 JSON 提示词](AI题图转JSON提示词.md)
- [版本记录](版本说明.md)（软件内也可查看）
- [内置示例](examples/tianjin_2026_q18.json)
- [贡献指南](CONTRIBUTING.md) · [协作署名](CONTRIBUTORS.md)

4K 视频使用标准 **3840×2160** 画幅，竖图交换宽高。原画布居中等比例放入，必要时留白，不拉伸、不裁切；不会把方形图放大成系统编码器可能不接受的 3840×3840。

MP4 使用 Windows Media Foundation。系统没有媒体组件或编码器不支持参数时，应依据错误码诊断，不能一律归因为缺少 Media Feature Pack。Liquid Glass 与 WebView2 是可选界面效果，不进入导出图像。

## 授权与致谢

GeometryGrid 自有代码采用 [Apache-2.0](LICENSE)。第三方代码、字体与 NuGet 依赖仍按各自许可分发，详见 [NOTICE](NOTICE) 与 [第三方说明](THIRD-PARTY-NOTICES.md)。

- Liquid Glass：[`martin65536/liquid-glass-webgl`](https://github.com/martin65536/liquid-glass-webgl)，固定提交 `a05dc75e781873c4fbcfb5fc14ec99fa3902cc23`。
- 原始设计与 Android 实现：Kyant；Web 移植：Z.ai Agent；仓库维护者：martin65536。
- AI 编程协作：**OpenAI Codex**。这是开发过程署名，不表示 OpenAI 官方维护或背书；详见 [CONTRIBUTORS.md](CONTRIBUTORS.md)。

交流群：**1080501105**。欢迎在 Issues 提交可复现的问题或建议。
