# 贡献指南

欢迎提交 Issue 或 Pull Request。贡献自有代码时，按本项目 Apache-2.0 许可提供；第三方资源必须保留来源和许可证。

## 开发与验证

1. 在 Windows 10/11 上安装 .NET 8 SDK。
2. 按 README 恢复依赖、构建并运行测试；首次构建会恢复并校验 OFL 字体。
3. 为 bug 修复补充回归测试。涉及界面、拖动、字体、导出的修改，还应人工检查对应场景。
4. MP4 测试需要系统 Media Foundation/H.264 编码能力。构建通过不等于用户界面或视频播放已经验收。

提交 bug 时请附软件版本、最小 JSON、复现步骤、期望与实际结果。分享日志前，请移除用户名、路径、个人文件内容及其他敏感信息。

## 资源与凭据

不要提交 `.env`、令牌、私钥、崩溃日志、个人题库、构建缓存或未核实授权的字体。图标与第三方渲染源码的授权说明见 `NOTICE`、`THIRD-PARTY-NOTICES.md`。

Liquid Glass 桌面入口在 `tools/webglass/entry.ts`；对应上游源码位于 `third_party/liquid-glass-webgl/src/components/liquid-glass/`。预编译运行层已随源码保留，普通 .NET 构建不需要 Node.js。修改该运行层后可执行：

```powershell
npx --yes esbuild@0.25.5 tools/webglass/entry.ts --bundle --format=iife --minify --outfile=src/GeometryGrid.App/Assets/LiquidGlass/liquid-glass-runtime.js
```

请在 PR 中说明测试命令与实际结果；不要把数值采样通过描述为对所有参数成立的数学证明。
