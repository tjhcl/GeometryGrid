# Vendored Liquid Glass renderer

- Source: https://github.com/martin65536/liquid-glass-webgl
- Commit: `a05dc75e781873c4fbcfb5fc14ec99fa3902cc23`
- License: Apache-2.0, retained in `LICENSE`.
- Maintainer: martin65536; Web port: Z.ai Agent.
- Original design and Android implementation: Kyant.

Only the renderer, shader and shape modules used by the desktop integration are vendored here. The upstream demo backend, environment files, databases, screenshots and unrelated application code are deliberately not included.

GeometryGrid's desktop entry is `tools/webglass/entry.ts`; the bundled browser runtime is `src/GeometryGrid.App/Assets/LiquidGlass/liquid-glass-runtime.js`. The integration adds WPF/WebView2 hosting, offline resources, fixed panel layout and fallback behavior.
