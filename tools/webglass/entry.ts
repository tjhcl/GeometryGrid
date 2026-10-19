import {
  LiquidGlassRenderer,
  type GlassElementConfig,
  type GlassHighlight,
} from '../../third_party/liquid-glass-webgl/src/components/liquid-glass/renderer'

declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage(message: unknown): void
      }
    }
    geometryGridGlass?: {
      refresh(): void
      setQuality(dpr: number): void
    }
  }
}

const canvas = document.querySelector<HTMLCanvasElement>('#liquid-glass')
if (!canvas) throw new Error('Liquid Glass canvas was not found.')

const renderer = new LiquidGlassRenderer(canvas)
const defaultHighlight: GlassHighlight = {
  mode: 0,
  color: [1, 1, 1],
  angle: Math.PI / 4,
  falloff: 1,
  alpha: 0.62,
  widthDp: 0.7,
}

const glass = (
  id: string,
  x: number,
  y: number,
  w: number,
  h: number,
  radius: number,
  options: Partial<GlassElementConfig> = {},
): GlassElementConfig => ({
  id,
  kind: 'glass-shape',
  rect: { x, y, w: Math.max(1, w), h: Math.max(1, h) },
  cornerRadius: radius,
  refractionHeight: 18,
  refractionAmount: -30,
  depthEffect: true,
  chromaticAberration: true,
  blurRadius: 5,
  saturation: 1.28,
  brightness: 0.015,
  contrast: 1.04,
  tintColor: [0.82, 0.91, 1, 0.035],
  surfaceColor: [1, 1, 1, 0.11],
  highlight: { ...defaultHighlight },
  outerShadow: {
    radius: 28,
    alpha: 0.14,
    offsetX: 0,
    offsetY: 6,
    color: [0.09, 0.16, 0.3],
  },
  innerShadow: null,
  label: '',
  labelColor: [0, 0, 0, 1],
  showChevron: false,
  isInteractive: false,
  scroll: false,
  ...options,
})

const buildWallpaper = () => {
  const svg = `
  <svg xmlns="http://www.w3.org/2000/svg" width="1920" height="1200" viewBox="0 0 1920 1200">
    <defs>
      <linearGradient id="base" x1="0" y1="0" x2="1" y2="1">
        <stop stop-color="#eef5ff"/>
        <stop offset=".46" stop-color="#f8f7ff"/>
        <stop offset="1" stop-color="#e9f8ff"/>
      </linearGradient>
      <radialGradient id="blue"><stop stop-color="#72a7ff" stop-opacity=".82"/><stop offset="1" stop-color="#72a7ff" stop-opacity="0"/></radialGradient>
      <radialGradient id="violet"><stop stop-color="#ad88ff" stop-opacity=".62"/><stop offset="1" stop-color="#ad88ff" stop-opacity="0"/></radialGradient>
      <radialGradient id="cyan"><stop stop-color="#63dce8" stop-opacity=".55"/><stop offset="1" stop-color="#63dce8" stop-opacity="0"/></radialGradient>
      <filter id="soft"><feGaussianBlur stdDeviation="34"/></filter>
      <pattern id="grain" width="80" height="80" patternUnits="userSpaceOnUse">
        <circle cx="5" cy="8" r="1" fill="#56719a" opacity=".10"/>
        <circle cx="48" cy="34" r=".8" fill="#536b91" opacity=".08"/>
        <circle cx="75" cy="68" r=".7" fill="#7e65ab" opacity=".08"/>
      </pattern>
    </defs>
    <rect width="1920" height="1200" fill="url(#base)"/>
    <ellipse cx="185" cy="120" rx="520" ry="430" fill="url(#blue)" filter="url(#soft)"/>
    <ellipse cx="1660" cy="350" rx="600" ry="570" fill="url(#violet)" filter="url(#soft)"/>
    <ellipse cx="920" cy="1170" rx="660" ry="380" fill="url(#cyan)" filter="url(#soft)"/>
    <path d="M-80 720 C330 460 660 940 1030 650 S1710 390 2030 680" fill="none" stroke="#fff" stroke-opacity=".24" stroke-width="90"/>
    <path d="M-120 790 C340 520 670 990 1040 710 S1700 470 2040 740" fill="none" stroke="#4b78cc" stroke-opacity=".08" stroke-width="2"/>
    <rect width="1920" height="1200" fill="url(#grain)"/>
  </svg>`
  return `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`
}

const rebuild = () => {
  const width = Math.max(320, window.innerWidth)
  const height = Math.max(240, window.innerHeight)
  renderer.resize(width, height)

  const inspectorWidth = Math.min(410, Math.max(330, width * 0.245))
  const topHeight = 70
  const statusHeight = 36
  const elements: GlassElementConfig[] = [
    glass('app-bar', 12, 10, width - 24, topHeight, 22, {
      refractionHeight: 22,
      refractionAmount: -34,
      blurRadius: 7,
      surfaceColor: [1, 1, 1, 0.13],
    }),
    glass('tool-rail', 12, 90, 74, height - 144, 22, {
      refractionHeight: 16,
      refractionAmount: -26,
      blurRadius: 5,
    }),
    glass('inspector', width - inspectorWidth - 12, 90, inspectorWidth, height - 144, 24, {
      refractionHeight: 20,
      refractionAmount: -32,
      blurRadius: 7,
      surfaceColor: [1, 1, 1, 0.16],
    }),
    glass('status-bar', 12, height - statusHeight - 8, width - 24, statusHeight, 14, {
      refractionHeight: 11,
      refractionAmount: -20,
      blurRadius: 4,
      surfaceColor: [1, 1, 1, 0.12],
    }),
    glass('ambient-orb-left', 94, height * 0.57, 72, 72, 36, {
      refractionHeight: 22,
      refractionAmount: -40,
      blurRadius: 2,
      surfaceColor: [0.78, 0.9, 1, 0.055],
      outerShadow: null,
      highlight: { ...defaultHighlight, alpha: 0.44, widthDp: 0.55 },
    }),
    glass('ambient-orb-right', width - inspectorWidth - 83, height * 0.22, 54, 54, 27, {
      refractionHeight: 18,
      refractionAmount: -36,
      blurRadius: 2,
      surfaceColor: [0.9, 0.84, 1, 0.05],
      outerShadow: null,
      highlight: { ...defaultHighlight, alpha: 0.4, widthDp: 0.5 },
    }),
  ]

  renderer.setElements(elements)
}

let resizeHandle = 0
window.addEventListener('resize', () => {
  cancelAnimationFrame(resizeHandle)
  resizeHandle = requestAnimationFrame(rebuild)
})

window.geometryGridGlass = {
  refresh: rebuild,
  setQuality(dpr: number) {
    renderer.dpr = Math.max(0.75, Math.min(1.5, dpr))
    rebuild()
  },
}

renderer.setGravityAngle(Math.PI / 4)
renderer.loadWallpaper(buildWallpaper()).then(() => {
  rebuild()
  window.chrome?.webview?.postMessage({
    type: 'liquid-glass-ready',
    renderer: 'martin65536/liquid-glass-webgl',
  })
}).catch((error: unknown) => {
  window.chrome?.webview?.postMessage({
    type: 'liquid-glass-error',
    message: error instanceof Error ? error.message : String(error),
  })
})

