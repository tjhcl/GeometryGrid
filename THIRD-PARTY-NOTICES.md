# GeometryGrid 第三方开源说明

## Liquid Glass WebGL

- 上游仓库：`martin65536/liquid-glass-webgl`
- 仓库地址：https://github.com/martin65536/liquid-glass-webgl
- GeometryGrid 固定使用提交：`a05dc75e781873c4fbcfb5fc14ec99fa3902cc23`
- Web 移植开发：Z.ai Agent
- 仓库维护者：martin65536
- 原始设计与 Android 实现：Kyant（`Kyant0/AndroidLiquidGlass`）
- 许可证：Apache License 2.0

GeometryGrid v.2.9 及后续版本直接从下载的上游 `renderer` 与 GLSL 源码编译桌面专用运行层，
用于主窗口顶栏、工具轨、检查器和状态栏的实时光学折射、模糊、色散与边缘高光。
上游许可证原文已嵌入 GeometryGrid EXE，并可从“更新日志”界面打开查看；源码目录中
同时保留上游完整 `LICENSE` 文件。

本项目对上游代码的改动主要是：提供无 React 的桌面入口、固定面板布局、自包含壁纸、
WebView2/WPF 合成、离线资源加载和不可用时的安全回退。上述适配不改变上游项目的
Apache License 2.0 授权。

## Noto Sans CJK SC

- Copyright 2014-2021 Adobe (http://www.adobe.com/).
- 上游：https://github.com/notofonts/noto-cjk
- 固定提交：`523d033d6cb47f4a80c58a35753646f5c3608a78`（Sans2.004）。
- 许可证：SIL Open Font License 1.1，原文保留于 `src/GeometryGrid.App/Assets/Fonts/NotoSansCJK-OFL.txt`。
- 字体未修改，首次构建下载后校验 SHA-256：`2C76254F6FC379FDDFCE0A7E84FB5385BB135D3E399294F6EEB6680D0365B74B`。

公开源码不包含 Product Sans 或方正字体文件，不声称拥有其再分发权。

## NuGet 依赖

- ComputeSharp 3.2.0：Sergio Pedri，MIT；包与传递依赖按各自许可证使用。
- Microsoft.Web.WebView2 1.0.4078.44：Microsoft；按 NuGet 包内 `LICENSE.txt` 的条款使用，不由本项目重新授权为 Apache-2.0。
- xUnit、Microsoft.NET.Test.Sdk 与 coverlet：仅用于测试，许可随各包提供。

发布二进制时需同时保留相关的许可和声明；本仓库不重新上传旧版本中未核实字体授权的 EXE/ZIP。
