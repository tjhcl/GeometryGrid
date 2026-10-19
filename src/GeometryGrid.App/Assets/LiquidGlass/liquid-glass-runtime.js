(()=>{var U=`
// Corner style: 0 = circular (standard arc), 1 = continuous (squircle/superellipse).
// Declared here (in SDF_GLSL) because sdShape references it, and SDF_GLSL is
// included by multiple shaders (element, shadow, highlight, plain-rect).
uniform float uCornerStyle;

// --- Continuous-curvature SDF texture (capsule shape) ---
// When uUseContinuousSdf > 0.5, sdShape() dispatches to sdContinuousCurvature
// which samples a precomputed SDF texture (generated from the G2-continuous
// Bezier path in continuous-curve.ts). Only the dialog card sets this to 1;
// other shaders that include SDF_GLSL leave it at the default 0 \u2014 sdShape
// falls through to the analytic sdRoundedRect / sdContinuousRoundedRect path.
uniform sampler2D uContinuousSdf;
uniform float uUseContinuousSdf;        // 0 or 1
uniform vec2  uContinuousSdfTexSize;    // SDF texture size in px (256, 256)
uniform vec2  uContinuousSdfElementSize; // element's original w,h in px

// radiusAt \u2014 picks the corner radius from cornerRadii based on which
// quadrant 'coord' is in. For uniform radii (the catalog case) this
// always returns the same value.
float radiusAt(vec2 coord, vec4 radii) {
    if (coord.x >= 0.0) {
        if (coord.y <= 0.0) return radii.y;
        else return radii.z;
    } else {
        if (coord.y <= 0.0) return radii.x;
        else return radii.w;
    }
}

// sdRoundedRect \u2014 signed distance to a rounded-rect boundary.
// Negative inside, positive outside, zero on the edge.
// Uses standard circular arcs for the corners.
float sdRoundedRect(vec2 coord, vec2 halfSize, float radius) {
    vec2 cornerCoord = abs(coord) - (halfSize - vec2(radius));
    float outside = length(max(cornerCoord, 0.0)) - radius;
    float inside = min(max(cornerCoord.x, cornerCoord.y), 0.0);
    return outside + inside;
}

// sdContinuousRoundedRect \u2014 continuous-curvature rounded rect.
// The original uses G2-continuous Bezier corners (ContinuousCurvatureRoundedRectangleCornerBuilder).
// The visual difference between Continuous and Circular is very subtle (only
// curvature continuity at the tangent points). For the SDF-based renderer,
// the circular arc SDF (sdRoundedRect) is a close enough approximation \u2014 the
// Bezier corners deviate from the arc by <0.5% of the radius, which is
// sub-pixel at typical element sizes.
//
// When uCornerStyle=1 (continuous), we use sdRoundedRect directly. The
// difference from the original is imperceptible. A future upgrade could
// implement exact Bezier SDF for pixel-perfect matching.
float sdContinuousRoundedRect(vec2 coord, vec2 halfSize, float radius) {
    return sdRoundedRect(coord, halfSize, radius);
}

// sampleClipMask \u2014 sample R channel (coverage) from the mask texture.
// Returns browser-native AA coverage [0,1] for clip + edgeAlpha.
float sampleClipMask(vec2 coord, vec2 halfSize, float radius) {
    float maxDim = max(max(uContinuousSdfElementSize.x, uContinuousSdfElementSize.y), 1e-4);
    float aspectW = uContinuousSdfElementSize.x / maxDim;
    float margin = 4.0;
    float drawW = (uContinuousSdfTexSize.x - 2.0 * margin) * aspectW;
    float scale = drawW / max(uContinuousSdfElementSize.x, 1e-4);
    vec2 tex = uContinuousSdfTexSize * 0.5 + coord * scale;
    vec2 uv = tex / uContinuousSdfTexSize;
    return texture2D(uContinuousSdf, uv).r;  // R = coverage [0,1]
}

// sampleClipSdf \u2014 sample G channel (SDF) from the mask texture.
// Returns signed distance: negative inside, positive outside, 0 at edge.
// Same shape as sampleClipMask (both from the same Bezier path), so clip
// and stroke shapes are always identical.
float sampleClipSdf(vec2 coord, vec2 halfSize, float radius) {
    float maxDim = max(max(uContinuousSdfElementSize.x, uContinuousSdfElementSize.y), 1e-4);
    float aspectW = uContinuousSdfElementSize.x / maxDim;
    float margin = 4.0;
    float drawW = (uContinuousSdfTexSize.x - 2.0 * margin) * aspectW;
    float scale = drawW / max(uContinuousSdfElementSize.x, 1e-4);
    vec2 tex = uContinuousSdfTexSize * 0.5 + coord * scale;
    vec2 uv = tex / uContinuousSdfTexSize;
    float g = texture2D(uContinuousSdf, uv).g;  // G = SDF [0,1]
    return (g * 2.0 - 1.0) * radius;  // decode to element-space distance
}

// sdClipShape \u2014 SDF for clip/discard when uUseContinuousSdf is OFF.
float sdClipShape(vec2 coord, vec2 halfSize, float radius) {
    return sdRoundedRect(coord, halfSize, radius);
}

// sdShape \u2014 SDF for refraction/highlight internal calculations.
// When uUseContinuousSdf=1, uses sampleClipSdf (same shape as clip mask).
// Otherwise uses sdRoundedRect.
float sdShape(vec2 coord, vec2 halfSize, float radius) {
    if (uUseContinuousSdf > 0.5) {
        return sampleClipSdf(coord, halfSize, radius);
    }
    return sdRoundedRect(coord, halfSize, radius);
}

// gradSdRoundedRect \u2014 gradient of the SDF (points outward from edge).
// Used both for refraction direction and highlight specular.
vec2 gradSdRoundedRect(vec2 coord, vec2 halfSize, float radius) {
    vec2 cornerCoord = abs(coord) - (halfSize - vec2(radius));
    if (cornerCoord.x >= 0.0 || cornerCoord.y >= 0.0) {
        vec2 v = max(cornerCoord, vec2(0.0));
        // Guard against normalize(0,0) -> NaN
        float len = length(v);
        if (len < 1e-6) return vec2(0.0);
        return sign(coord) * (v / len);
    } else {
        float gradX = step(cornerCoord.y, cornerCoord.x);
        return sign(coord) * vec2(gradX, 1.0 - gradX);
    }
}

// rotateBy \u2014 rotate a 2D vector by angle (radians). Used to un-rotate the
// sample coord into the element's local space (so the SDF shape appears
// rotated by +uElementRotation), and to rotate refraction offsets back to
// screen space.
vec2 rotateBy(vec2 v, float angle) {
    float c = cos(angle);
    float s = sin(angle);
    return vec2(v.x * c - v.y * s, v.x * s + v.y * c);
}

// erfApprox \u2014 error function approximation (Abramowitz & Stegun 7.1.26).
// Max error < 2.5e-5. Used by inner shadow to model BlurMaskFilter's
// Gaussian convolution of a ring shape. erf(x) \u2208 [-1, 1].
float erfApprox(float x) {
    float a = abs(x);
    float t = 1.0 / (1.0 + 0.47047 * a);
    float y = 1.0 - (((0.3480242 * t - 0.0958798) * t + 0.7478556) * t * exp(-a * a));
    return sign(x) * y;
}
`,he=`
// Returns wallpaper UV for a canvas pixel coordinate (top-left origin).
vec2 coverUv(vec2 canvasPx) {
    float canvasAspect = uCanvasSize.x / uCanvasSize.y;
    float wpAspect = uWallpaperSize.x / uWallpaperSize.y;
    vec2 uv = canvasPx / uCanvasSize;
    if (wpAspect > canvasAspect) {
        // Wallpaper is wider than canvas \u2014 crop horizontally.
        float s = canvasAspect / wpAspect;
        uv.x = (uv.x - 0.5) * s + 0.5;
    } else {
        // Wallpaper is taller than canvas \u2014 crop vertically.
        float s = wpAspect / canvasAspect;
        uv.y = (uv.y - 0.5) * s + 0.5;
    }
    return uv;
}

// Per-axis scale: 1 canvas pixel in wallpaper UV units.
// Used to convert a blur radius (in canvas px) into UV-space offsets
// for poisson-disc sampling.
vec2 canvasPxToUvScale() {
    float canvasAspect = uCanvasSize.x / uCanvasSize.y;
    float wpAspect = uWallpaperSize.x / uWallpaperSize.y;
    if (wpAspect > canvasAspect) {
        return vec2(canvasAspect / wpAspect, 1.0) / uCanvasSize;
    } else {
        return vec2(1.0, wpAspect / canvasAspect) / uCanvasSize;
    }
}
`;var vt=`
uniform sampler2D uBackdrop;
uniform sampler2D uWallpaperSampler;  // wallpaper texture (unscaled backdrop for toggle knobs)
uniform sampler2D uTabsBackdropSampler;  // tabsBackdrop FBO (tinted scene for indicator CombinedBackdrop)
uniform vec2  uCanvasSize;        // canvas size in px
uniform vec2  uWallpaperSize;     // UNUSED \u2014 kept for uniform-set compatibility
uniform vec2  uElementOffset;     // element top-left in canvas px (SCALED rect \u2014 where the quad is drawn)
uniform vec2  uElementSize;       // element size in px (SCALED \u2014 includes graphicsLayer scaleX/scaleY)
uniform vec4  uCornerRadii;       // (topLeft, topRight, bottomRight, bottomLeft) in px (ORIGINAL, unscaled)
uniform float uRefractionHeight;  // px (ORIGINAL space \u2014 NOT scaled by layerScale, faithful to AGSL)
uniform float uRefractionAmount;  // px (ORIGINAL space \u2014 NOT scaled, faithful to AGSL)
// --- Layer transform (faithful to graphicsLayer { scaleX, scaleY }) ---
// The original applies the refraction shader at the ORIGINAL element size, THEN
// scales the entire rendered layer by (scaleX, scaleY) via graphicsLayer. To
// replicate this in a single-pass shader, we compute the SDF/refraction in
// ORIGINAL space (by dividing the screen-space centered coord by uLayerScale),
// then map the refraction offset back to screen space for backdrop sampling.
// This keeps the SDF shape correct (not stretched) while covering the scaled rect.
uniform vec2  uOriginalSize;        // element size in px (ORIGINAL, unscaled by graphicsLayer)
uniform float uOriginalCornerRadius; // corner radius in px (ORIGINAL, unscaled)
uniform vec2  uLayerScale;          // (scaleX, scaleY) from graphicsLayer \u2014 maps original\u2192screen
uniform float uElementRotation;    // rotation in radians (graphicsLayer rotationZ) \u2014 0 = none
uniform float uDepthEffect;       // 0 or 1
uniform float uChromaticAberration; // 0 or 1
uniform float uBlurRadius;        // px
uniform float uSaturation;        // vibrancy = 1.5
uniform float uBrightness;        // brightness offset (0 for vibrancy)
uniform float uContrast;          // 1.0 for vibrancy
uniform vec4  uTintColor;         // rgba; alpha 0 = no tint
uniform vec4  uSurfaceColor;      // rgba; alpha 0 = no surface
uniform vec4  uHighlightColor;    // rgb + 1.0 (alpha handled by uHighlightAlpha)
uniform float uHighlightAngle;    // radians
uniform float uHighlightFalloff;
uniform float uHighlightAlpha;
uniform float uHighlightMode;     // 0=default, 1=ambient, 2=plain
uniform float uHighlightStrokeWidth; // px (full stroke width, matching paint.strokeWidth)
uniform float uHighlightBlur;     // px (BlurMaskFilter radius)
// Content scale (non-uniform, faithful to LiquidToggle.kt / LiquidSlider.kt):
//   scale(scaleX, scaleY) { drawBackdrop() }
// Toggle: X lerp(2/3, 0.75, p), Y lerp(0, 0.75, p)
// Slider: X lerp(2/3, 1, p),    Y lerp(0, 1, p)
// At rest Y=0 \u2192 backdrop sampled from a single horizontal line (degenerate),
// but the white overlay (alpha=1) hides it. When pressed, scales to full.
uniform float uContentScaleX;
uniform float uContentScaleY;
// --- Toggle knob CombinedBackdrop effect (faithful to LiquidToggle.kt) ---
// The knob's backdrop is a CombinedBackdrop of:
//   1. Outer backdrop (LayerBackdrop wallpaper OR CanvasBackdrop solid color)
//   2. Scaled trackBackdrop (track color rect, scaled by lerp(2/3,0.75) x lerp(0,0.75))
// uUseToggleBackdrop = 1.0 \u2192 sample outer backdrop + composite scaled track color
// uUseToggleBackdrop = 0.0 \u2192 sample scene (uBackdrop) as before
//
// uUseSolidBackdrop = 1.0 \u2192 outer backdrop is solid color (uSolidBackdropColor)
// uUseSolidBackdrop = 0.0 \u2192 outer backdrop is wallpaper texture (uWallpaperSampler)
// Faithful to ToggleContent.kt:
//   - t1 (on wallpaper): backdrop = LayerBackdrop \u2192 sample wallpaper texture
//   - t2 (on card):      backdrop = rememberCanvasBackdrop { drawRect(color) } \u2192 solid color
uniform float uUseToggleBackdrop;
uniform float uUseSolidBackdrop;
uniform vec4  uSolidBackdropColor;  // rgba 0..1; used when uUseSolidBackdrop = 1.0
uniform vec4  uTrackColor;        // rgba 0..1; alpha 0 = no track color
uniform vec4  uTrackRect;         // (centerX, centerY, halfW, halfH) in canvas px (dpr-scaled)
uniform float uTrackCornerRadius; // canvas px (dpr-scaled)
// --- Bottom tab \u6307\u793A\u5668 CombinedBackdrop (faithful to LiquidBottomTabs.kt) ---
// The \u6307\u793A\u5668's backdrop = CombinedBackdrop(wallpaper, \u5185\u5C42\u80CC\u666F\u677F) where
// \u5185\u5C42\u80CC\u666F\u677F (tabsBackdrop) is a hidden Row with ColorFilter.tint(accentColor). Only the
// opaque \u6807\u7B7E\u5185\u5BB9 (icons/labels) becomes blue after tint \u2014 the glass part
// is transparent. We pass up to 8 tab content rects; pixels inside any rect
// (clipped to the \u5BB9\u5668 capsule) are tinted accentColor.
uniform float uIndicatorBackdrop;    // 0 or 1
uniform vec4  uContainerRect;        // (centerX, centerY, halfW, halfH) in canvas px (dpr-scaled)
uniform float uContainerCornerRadius; // canvas px (dpr-scaled)
uniform vec4  uIndicatorAccent;      // (r, g, b, a) \u2014 accentColor + unused
uniform float uInsetPx;              // indicator backdrop inset in device px (4dp * dpr)
uniform float uIndicatorPressProgress; // 0..1 press progress (for 2nd-layer scale)
uniform float uIndicatorPanelOffset; // panel offset in device px (2nd-layer x translation)
uniform float uDpr;                 // device pixel ratio (for dp\u2192px conversion)
uniform vec2  uContainerCenter;      // container center (scale origin) in canvas px (dpr-scaled)
uniform float uContainerScale;       // container layerBlock scale (1 + 16dp/width * pressProgress)
// Tab content fgTextures (icon+label alpha masks) for blue tint. Up to 8 tabs.
// Only opaque icon/label pixels become blue \u2014 the container glass stays natural.
uniform sampler2D uTabContentTex0;
uniform sampler2D uTabContentTex1;
uniform sampler2D uTabContentTex2;
uniform sampler2D uTabContentTex3;
uniform sampler2D uTabContentTex4;
uniform sampler2D uTabContentTex5;
uniform sampler2D uTabContentTex6;
uniform sampler2D uTabContentTex7;
uniform vec4  uTabContentRects[8];   // (centerX, centerY, halfW, halfH) per tab, canvas px (dpr-scaled)
uniform float uTabContentCount;      // number of valid tab rects (0..8)
uniform sampler2D uTabsGlassLayer;   // scene snapshot BEFORE tab-content (wallpaper+glass only, no text)
// --- SDF texture glass (faithful to SdfShader.kt) ---
uniform sampler2D uSdfTexSampler;   // clock_sdf texture (R=SDF, GB=normal, A=shape alpha)
uniform float uUseSdfTexture;       // 0 or 1
uniform vec2  uSdfTexSize;          // texture natural dimensions (px)
uniform float uSdfLightAngle;       // bevel light angle (degrees)
uniform float uEnterAlpha;          // global element alpha (enterProgress, 0..1)
// When 1.0, skip applyColorControls in the element shader (colorControls was
// already applied as a fullscreen pass BEFORE the 2-pass blur on the backdrop
// FBO, matching the original's colorControls\u2192blur\u2192lens order). Used by
// backdropFbo + useSeparableBlur elements (dialog card).
uniform float uSkipColorControls;   // 0 or 1
// --- Magnifier glass (faithful to MagnifierContent.kt) ---
uniform float uUseMagnifier;        // 0 or 1
uniform float uMagnifierZoom;       // zoom factor (1.5)
uniform float uMagnifierOffsetY;    // sample Y offset to cursor (80dp, device px)
// --- Sample wallpaper directly (bypass scene FBO) ---
// When 1.0, sampleBackdrop uses coverUv + uWallpaperSampler (clean wallpaper)
// instead of sceneUv + uBackdrop (scene FBO). Used by elements that sit over
// a scrim/dim (Dialog card, ControlCenter tiles) so the glass refracts the
// clean wallpaper instead of the alpha-decayed scene FBO. Faithful to the
// original where LayerBackdrop captures the wallpaper Image (alpha=1).
uniform float uSampleWallpaper;     // 0 or 1
// --- Scrim color (applied to the wallpaper BEFORE colorControls/blur/lens) ---
// Faithful to DialogContent.kt / ControlCenterContent.kt where the scrim
// (drawRect(dimColor)) is painted onto the wallpaper Image (via
// BackdropDemoScaffold's modifier = drawWithContent { drawContent(); drawRect(dimColor) }),
// so the LayerBackdrop captures wallpaper+scrim as one opaque layer.
// In the port, when uSampleWallpaper=1 (clean wallpaper), we apply the scrim
// here in the shader to replicate that composited backdrop. uScrimColor.a=0
// means no scrim. Applied as SrcOver: backdrop.rgb = scrim.rgb*scrim.a + backdrop.rgb*(1-scrim.a).
uniform vec4 uScrimColor;           // rgba 0..1; a=0 = no scrim
// --- \u5185\u5C42\u80CC\u666F\u677F rim highlight stroke mask (Canvas2D, same approach as outer rim) ---
// When uIndicatorBackdrop=1, the inner backdrop plate's rim highlight is sampled
// from this pre-rasterized Canvas2D stroke mask instead of computed analytically.
// The mask is drawn for the \u5185\u5C42\u80CC\u666F\u677F capsule shape (uContainerRect dimensions)
// with clip(stroke) + BlurMaskFilter, giving browser-native Skia AA.
uniform sampler2D uInnerStrokeMask;   // Canvas2D stroke mask texture for inner backdrop highlight
uniform vec2  uInnerStrokeMaskOffset; // margin (strokeMargin) in device px \u2014 UV offset
uniform vec2  uInnerStrokeMaskSize;   // (maskW, maskH) in device px \u2014 total mask texture size
`;function Xt(i){let e=[];if(i<=1)return e.push({x:0,y:0,w:1}),e;let t=Math.PI*(3-Math.sqrt(5)),a=3,o=0;for(let r=0;r<i;r++){let l=(r+.5)/i,s=a*Math.sqrt(l),n=r*t,h=s*Math.cos(n),c=s*Math.sin(n),u=h*h+c*c,d=Math.exp(-.5*u);e.push({x:h,y:c,w:d}),o+=d}if(o>0)for(let r of e)r.w/=o;return e}function Tt(i,e,t,a){if(i.length===1)return`    return texture2D(${e}, ${t});
`;let o="";for(let r of i){let l=r.x.toFixed(6),s=r.y.toFixed(6),n=r.w.toFixed(8);o+=`    sum += texture2D(${e}, ${t} + vec2(${l}, ${s}) * ${a}) * ${n};
`}return o}var Me=16;function xt(i=Me){let e=Xt(i),t=Tt(e,"uBackdrop","uv","pxToUv"),a=Tt(e,"uWallpaperSampler","uv","pxToUv");return`
// Forward declarations \u2014 blendHue/rgb2hsv/hsv2rgb are defined later but used
// by sampleIndicatorBackdrop (which must come before sampleToggleBackdrop in
// the file for readability). GLSL ES 1.00 requires declaration before use.
vec3 rgb2hsv(vec3 c);
vec3 hsv2rgb(vec3 c);
vec3 blendHue(vec3 dst, vec3 src);

float circleMap(float x) {
    return 1.0 - sqrt(1.0 - x * x);
}

// SDF-texture glass sampling (faithful to SdfShader.kt).
// Samples the clock_sdf texture at element-local coords.
// Returns vec4(intensity, maskAlpha, normalX, normalY); zeroes if outside.
vec4 sampleSdfTexture(vec2 localPx) {
    vec2 uv = vec2(localPx.x / uOriginalSize.x,
                   localPx.y / uOriginalSize.y);
    if (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0) {
        return vec4(0.0);
    }
    vec4 v = texture2D(uSdfTexSampler, uv);
    float sd = v.r * 2.0 - 1.0;
    float mask = smoothstep(0.5, 1.0, v.a);
    if (mask <= 0.0) return vec4(0.0);
    if (mask < 1.0) sd = 0.0;
    vec2 normal = normalize(v.gb * 2.0 - 1.0);
    float intensity = circleMap(1.0 - min(1.0, -sd * 1.5));
    return vec4(intensity, mask, normal.x, normal.y);
}

// Convert a canvas-pixel coordinate (top-left origin) to scene-texture UV.
// The scene texture is the same size as the canvas, and is rendered with
// gl_FragCoord (bottom-left origin). So UV = (canvasPx.x / canvasW, 1 -
// canvasPx.y / canvasH). The Y flip happens here so the rest of the shader
// can work in top-left-origin canvas px.
vec2 sceneUv(vec2 canvasPx) {
    return vec2(canvasPx.x / uCanvasSize.x, 1.0 - canvasPx.y / uCanvasSize.y);
}

// Gaussian disc blur \u2014 ${i} taps, dynamically generated in JS.
// Offsets are in units of radius (sigma = radius), scaled at runtime.
// radius < 0.5 falls back to single tap (no visible blur).
//
// When uSampleWallpaper > 0.5, samples the CLEAN wallpaper (uWallpaperSampler
// via coverUv) instead of the scene FBO (uBackdrop via sceneUv), AND applies
// the scrim (uScrimColor) to replicate the original's wallpaper+scrim composited
// LayerBackdrop. The scrim is applied INSIDE sampleBackdrop so EVERY sampling
// site \u2014 the initial backdrop sample, the refraction re-sample, and each
// chromatic-aberration channel \u2014 gets the same wallpaper+scrim composite.
// This fixes the "scrim not applied at edges" bug where the refraction band
// re-sampled the clean wallpaper (without scrim), making the edge brighter
// than the interior.
vec4 sampleBackdrop(vec2 canvasPx, float radius) {
    if (uSampleWallpaper > 0.5) {
        vec2 uv = coverUv(canvasPx);
        vec4 c;
        if (radius < 0.5) {
            c = texture2D(uWallpaperSampler, uv);
        } else {
            vec2 pxToUv = radius * canvasPxToUvScale();
            vec4 sum = vec4(0.0);
${a}            c = sum;
        }
        // Apply scrim (SrcOver) so the backdrop = wallpaper+scrim, opaque.
        if (uScrimColor.a > 0.001) {
            c.rgb = uScrimColor.rgb * uScrimColor.a + c.rgb * (1.0 - uScrimColor.a);
            c.a = 1.0;
        }
        return c;
    }
    vec2 uv = sceneUv(canvasPx);
    if (radius < 0.5) {
        return texture2D(uBackdrop, uv);
    }
    vec2 pxToUv = radius / uCanvasSize;
    vec4 sum = vec4(0.0);
${t}    return sum;
}

// Gaussian disc blur of the WALLPAPER (uWallpaperSampler via coverUv).
// Used by the SDF-texture glass path (LockScreen) \u2014 faithful to the original's
// blur(2dp) effect applied before the SDF shader.
vec4 sampleWallpaperBlurred(vec2 canvasPx, float radius) {
    vec2 uv = coverUv(canvasPx);
    if (radius < 0.5) {
        return texture2D(uWallpaperSampler, uv);
    }
    vec2 pxToUv = radius * canvasPxToUvScale();
    vec4 sum = vec4(0.0);
${a}    return sum;
}

// --- Toggle knob CombinedBackdrop sampling (faithful to LiquidToggle.kt) ---
// The knob's backdrop is a CombinedBackdrop of:
//   1. Outer backdrop:
//      - LayerBackdrop (wallpaper) for t1 \u2192 sample uWallpaperSampler
//      - CanvasBackdrop (solid color) for t2 \u2192 use uSolidBackdropColor
//   2. Scaled trackBackdrop (track color rect, clipped to Capsule, scaled
//      by lerp(2/3, 0.75, pressProgress) x lerp(0, 0.75, pressProgress)
//      around the knob's center)
//
// This function samples the outer backdrop (wallpaper OR solid color) with blur,
// then composites the scaled track color on top using a rounded-rect SDF
// at the uTrackRect position (center + half-size + corner radius).
//
// The track color SDF is also blurred by approximating the blur as a
// smoothstep over uBlurRadius \u2014 this matches the original where the blur
// effect is applied to the CombinedBackdrop (outer + track color).
vec4 sampleToggleBackdrop(vec2 canvasPx, float radius) {
    // 1. Sample outer backdrop with blur.
    vec4 wp;
    if (uUseSolidBackdrop > 0.5) {
        // CanvasBackdrop case (t2): solid color fills the entire knob area.
        // Faithful to: rememberCanvasBackdrop { drawRect(backgroundColor) }
        // The drawRect fills the DrawScope (knob's bounds) with the color,
        // so every pixel of the knob's backdrop is the solid color.
        wp = uSolidBackdropColor;
    } else if (radius < 0.5) {
        // LayerBackdrop case (t1): sample wallpaper texture unscaled.
        // IMPORTANT: use coverUv (cover-fit) to match the wallpaper background
        // pass (WALLPAPER_FRAGMENT_SHADER). Using sceneUv (raw normalization)
        // here would sample the wrong texel when the wallpaper aspect ratio
        // differs from the canvas \u2014 causing the knob to see a shifted/misaligned
        // wallpaper that doesn't match what's displayed behind it.
        vec2 uv = coverUv(canvasPx);
        wp = texture2D(uWallpaperSampler, uv);
    } else {
        // LayerBackdrop case (t1) with blur: 9-tap poisson disc on wallpaper.
        // Use coverUv for the center sample, and convert the blur radius from
        // canvas px to UV-space using canvasPxToUvScale() (which accounts for
        // the cover-fit aspect ratio cropping).
        vec2 uv = coverUv(canvasPx);
        vec2 pxToUv = radius * canvasPxToUvScale();
        vec4 sum = vec4(0.0);
        float total = 0.0;
        sum += texture2D(uWallpaperSampler, uv) * 0.25; total += 0.25;
        sum += texture2D(uWallpaperSampler, uv + vec2( 1.000,  0.000) * pxToUv) * 0.12; total += 0.12;
        sum += texture2D(uWallpaperSampler, uv + vec2(-1.000,  0.000) * pxToUv) * 0.12; total += 0.12;
        sum += texture2D(uWallpaperSampler, uv + vec2( 0.000,  1.000) * pxToUv) * 0.12; total += 0.12;
        sum += texture2D(uWallpaperSampler, uv + vec2( 0.000, -1.000) * pxToUv) * 0.12; total += 0.12;
        sum += texture2D(uWallpaperSampler, uv + vec2( 0.707,  0.707) * pxToUv) * 0.0675; total += 0.0675;
        sum += texture2D(uWallpaperSampler, uv + vec2( 0.707, -0.707) * pxToUv) * 0.0675; total += 0.0675;
        sum += texture2D(uWallpaperSampler, uv + vec2(-0.707,  0.707) * pxToUv) * 0.0675; total += 0.0675;
        sum += texture2D(uWallpaperSampler, uv + vec2(-0.707, -0.707) * pxToUv) * 0.0675; total += 0.0675;
        wp = sum / total;
    }

    // 2. Composite scaled track color on top.
    // The track rect is centered at uTrackRect.xy with half-size uTrackRect.zw,
    // and corner radius uTrackCornerRadius. We compute the SDF of this
    // rounded rect at canvasPx, then apply a smoothstep for edge AA + blur.
    // If uTrackColor.a == 0.0 OR the track rect is degenerate (halfW or
    // halfH < 0.5px, which happens at rest when scaleY=0), skip compositing.
    // Faithful to original: scale(scaleX, 0) { drawRect() } draws nothing.
    if (uTrackColor.a > 0.001 && uTrackRect.z > 0.5 && uTrackRect.w > 0.5) {
        vec2 trackCenter = uTrackRect.xy;
        vec2 trackHalf = uTrackRect.zw;
        vec2 trackLocal = canvasPx - trackCenter;
        // sdRoundedRect expects centered coord (relative to center).
        // Use uniform corner radius = uTrackCornerRadius.
        float tr = uTrackCornerRadius;
        // Approximate the rounded-rect SDF (matches sdRoundedRect from SDF_GLSL).
        vec2 q = abs(trackLocal) - trackHalf + vec2(tr);
        float trackSd = length(max(q, vec2(0.0))) + min(max(q.x, q.y), 0.0) - tr;
        // Blur the edge by uBlurRadius (approximate Gaussian edge feather).
        // Inside (trackSd < -radius) \u2192 mask=1; outside (trackSd > radius) \u2192 mask=0.
        // Use max(radius, 1.0) to guarantee at least 1px smoothstep for AA
        // \u2014 when fully pressed, blurRadius=0, but edges must still be smooth.
        float aaRadius = max(radius, 1.0);
        float mask = 1.0 - smoothstep(-aaRadius, aaRadius, trackSd);
        // Composite: srcOver (track color over outer backdrop).
        float a = mask * uTrackColor.a;
        wp.rgb = mix(wp.rgb, uTrackColor.rgb, a);
        wp.a = mix(wp.a, 1.0, a);
    }
    return wp;
}

// sampleIndicatorBackdrop \u2014 faithful to LiquidBottomTabs.kt indicator.
//
// Naming convention (used throughout the bottom-tabs code):
//   - \u5BB9\u5668 (Container)  = outer visible glass bar (64dp), Container Row in Kotlin
//   - \u6307\u793A\u5668 (Indicator) = selected sliding glass capsule (56dp), Indicator Box in Kotlin
//   - \u5185\u5C42\u80CC\u666F\u677F (Inner backdrop) = hidden 56dp glass captured by tabsBackdrop,
//     tinted blue by ColorFilter.tint(accentColor), sampled by the indicator
//   - \u6807\u7B7E\u5185\u5BB9 (Tab content) = icon + label inside each tab slot
//
// Original: indicator.drawBackdrop(backdrop = rememberCombinedBackdrop(backdrop, tabsBackdrop))
//   - backdrop (outer) = LayerBackdrop = wallpaper (sampled via coverUv)
//   - tabsBackdrop (inner) = hidden Row's 56dp glass, inset 4dp from the
//     indicator's draw area on all sides.
//
// Implementation (mirrors sampleToggleBackdrop):
//   1. Sample wallpaper (outer backdrop) with blur \u2014 same as toggle's outer.
//   2. Composite the scene FBO (uBackdrop = container glass + content)
//      inside an INSET capsule SDF (containerRect shrunk 4dp each side).
//      This is the "smaller background plate" refracted inside the indicator.
vec4 sampleIndicatorBackdrop(vec2 canvasPx, float radius) {
    // 1. Sample wallpaper (outer LayerBackdrop) via coverUv (cover-fit).
    vec4 wp;
    if (radius < 0.5) {
        vec2 uv = coverUv(canvasPx);
        wp = texture2D(uWallpaperSampler, uv);
    } else {
        vec2 uv = coverUv(canvasPx);
        vec2 pxToUv = radius * canvasPxToUvScale();
        vec4 sum = vec4(0.0);
        float total = 0.0;
        sum += texture2D(uWallpaperSampler, uv) * 0.25; total += 0.25;
        sum += texture2D(uWallpaperSampler, uv + vec2( 1.000,  0.000) * pxToUv) * 0.12; total += 0.12;
        sum += texture2D(uWallpaperSampler, uv + vec2(-1.000,  0.000) * pxToUv) * 0.12; total += 0.12;
        sum += texture2D(uWallpaperSampler, uv + vec2( 0.000,  1.000) * pxToUv) * 0.12; total += 0.12;
        sum += texture2D(uWallpaperSampler, uv + vec2( 0.000, -1.000) * pxToUv) * 0.12; total += 0.12;
        sum += texture2D(uWallpaperSampler, uv + vec2( 0.707,  0.707) * pxToUv) * 0.0675; total += 0.0675;
        sum += texture2D(uWallpaperSampler, uv + vec2( 0.707, -0.707) * pxToUv) * 0.0675; total += 0.0675;
        sum += texture2D(uWallpaperSampler, uv + vec2(-0.707,  0.707) * pxToUv) * 0.0675; total += 0.0675;
        sum += texture2D(uWallpaperSampler, uv + vec2(-0.707, -0.707) * pxToUv) * 0.0675; total += 0.0675;
        wp = sum / total;
    }

    // 2. \u5185\u5C42\u80CC\u666F\u677F (Inner backdrop) SDF \u2014 the hidden Row's 56dp glass capsule.
    //    Faithful to LiquidBottomTabs.kt: the hidden Row has NO layerBlock,
    //    so its glass does NOT scale with the container. Only panelOffset
    //    shifts it (translationX = panelOffset).
    vec2 capsuleHalf = max(uContainerRect.zw, vec2(0.0));
    float cr = max(uContainerCornerRadius, 0.0);
    // Center = rectCenter + panelOffset (NO container scale).
    vec2 scaledCenter = uContainerRect.xy + vec2(uIndicatorPanelOffset, 0.0);
    vec2 capsuleLocal = canvasPx - scaledCenter;
    vec2 cq = abs(capsuleLocal) - capsuleHalf + vec2(cr);
    float capsuleSd = length(max(cq, vec2(0.0))) + min(max(cq.x, cq.y), 0.0) - cr;
    // Mask: interpolate between 1.0 (at rest) and smoothstep (when pressed).
    // At rest (progress=0): mask=1.0 \u2014 no separate smoothstep transition at
    // the containerRect boundary, because it overlaps with the indicator's own
    // edge (both 56dp capsules). A second smoothstep here would reveal raw
    // wallpaper at the indicator edge, causing jagged aliasing. With mask=1.0,
    // the indicator always shows the glass scene inside its shape, and edgeAlpha
    // smoothly fades to transparent \u2014 matching the container glass behind it.
    // When pressed (progress=1): restore the original smoothstep mask for the
    // CombinedBackdrop clipping. Refraction displaces samples away from the
    // shared edge, so the smoothstep no longer causes jaggies; and the inner
    // backdrop capsule clip preserves the correct CombinedBackdrop visual
    // (scene inside capsule, wallpaper outside).
    float indicatorAaRadius = max(radius, 1.0);
    float smoothstepMask = 1.0 - smoothstep(-indicatorAaRadius, indicatorAaRadius, capsuleSd);
    float mask = mix(1.0, smoothstepMask, uIndicatorPressProgress);

    // 2b. \u5185\u5C42\u80CC\u666F\u677F shadow (Shadow.Default) \u2014 faithful to LiquidBottomTabs.kt
    //     hidden Row's drawBackdrop: shadow defaults to Shadow.Default when not specified.
    //     Shadow.Default: radius=24dp, offset=DpOffset(0, radius/6=4dp), color=Black@0.1, alpha=1.
    //     In the CombinedBackdrop, the shadow is composited between wallpaper (outer)
    //     and glass body (inner). Through the semi-transparent glass body, this shadow
    //     bleeds through near the capsule edges \u2014 most visible near the top edge where
    //     the shadow offset (0, +4dp) makes those pixels "outside" the shadow capsule
    //     (shadow capsule top = original top + 4dp, so original top is outside it).
    //     Implementation mirrors ShadowModifier.kt:
    //       1. Shift capsule by shadow offset \u2192 shadow shape SDF
    //       2. Gaussian falloff (MaskFilter.makeBlur sigma = radius directly)
    //       3. Mask inside original capsule (ShadowMaskPaint BlendMode.Clear)
    //       4. Darken wallpaper by Black@0.1 \xD7 shadowIntensity
    float shadowOffsetYpx = (24.0 / 6.0) * uDpr; // DpOffset(0, radius/6) in device px
    vec2 shadowLocal = capsuleLocal - vec2(0.0, shadowOffsetYpx);
    vec2 shadowCq2 = abs(shadowLocal) - capsuleHalf + vec2(cr);
    float shadowSd = length(max(shadowCq2, vec2(0.0))) + min(max(shadowCq2.x, shadowCq2.y), 0.0) - cr;
    // Shadow intensity: Gaussian falloff from shadow shape edge.
    // MaskFilter.makeBlur(FilterBlurMode.NORMAL, radius) takes sigma = radius directly.
    float shadowSigma = max(24.0 * uDpr, 1.0); // sigma = 24dp in device px
    float shadowIntensity = 0.5 * exp(-shadowSd * shadowSd / (2.0 * shadowSigma * shadowSigma));
    // Mask shadow inside the original capsule (ShadowMaskPaint BlendMode.Clear
    // removes shadow where the shape itself is drawn, so shadow only appears outside).
    shadowIntensity *= smoothstep(-1.0, 1.0, capsuleSd);
    // Darken wallpaper by Black@0.1 \xD7 shadowIntensity (SrcOver compositing).
    wp.rgb *= (1.0 - shadowIntensity * 0.1);

    // 3. Sample the GLASS LAYER FBO (wallpaper + container glass, NO tab text).
    //    This is a snapshot taken after the container glass is rendered but
    //    before tab-content is drawn \u2014 so it has no white/black text to bleed
    //    through. The blue tab text is drawn on top via fgTexture (step 4).
    vec2 sceneUv2 = sceneUv(canvasPx - vec2(uIndicatorPanelOffset, 0.0));
    vec4 scene = texture2D(uTabsGlassLayer, sceneUv2);

    // 4. Draw blue \u6807\u7B7E\u5185\u5BB9 (tab content: icons/labels) on top of the glass layer.
    //    Use each tab's fgTexture alpha as a hard mask (step) \u2014 pixels inside
    //    the icon/label shape become blue, everything else stays the glass
    //    layer's natural color. No white edges (hard replace, no mix).
    //    Faithful to LiquidBottomTabs.kt: the hidden Row's tab content gets
    //    LocalLiquidBottomTabScale = lerp(1, 1.2, pressProgress) + panelOffset
    //    (NOT the container scale \u2014 the hidden Row is a sibling of the
    //    container, not a child, so the container layerBlock doesn't apply).
    float contentScale = 1.0 + 0.2 * uIndicatorPressProgress;
    float tabMask = 0.0;
    for (int i = 0; i < 8; i++) {
        if (float(i) >= uTabContentCount) break;
        vec4 r = uTabContentRects[i];
        if (r.z > 0.5 && r.w > 0.5) {
            // Tab content scales around its OWN center (not container center)
            // by contentScale, then shifts by panelOffset.
            vec2 tabCenter = r.xy + vec2(uIndicatorPanelOffset, 0.0);
            vec2 scaledHalf = r.zw * contentScale;
            vec2 localPx = canvasPx - (tabCenter - scaledHalf);
            vec2 uv = localPx / (scaledHalf * 2.0);
            if (all(greaterThanEqual(uv, vec2(0.0))) && all(lessThanEqual(uv, vec2(1.0)))) {
                float a = 0.0;
                if (i == 0) a = texture2D(uTabContentTex0, uv).a;
                else if (i == 1) a = texture2D(uTabContentTex1, uv).a;
                else if (i == 2) a = texture2D(uTabContentTex2, uv).a;
                else if (i == 3) a = texture2D(uTabContentTex3, uv).a;
                else if (i == 4) a = texture2D(uTabContentTex4, uv).a;
                else if (i == 5) a = texture2D(uTabContentTex5, uv).a;
                else if (i == 6) a = texture2D(uTabContentTex6, uv).a;
                else if (i == 7) a = texture2D(uTabContentTex7, uv).a;
                tabMask = max(tabMask, a);
            }
        }
    }
    // Use fgTexture alpha directly as the blue compositing factor. fgTexture
    // is LINEAR-filtered so its alpha has smooth AA edges \u2014 no smoothstep
    // threshold needed (which caused jaggies by hard-clipping the AA gradient).
    vec3 sceneColor = mix(scene.rgb, uIndicatorAccent.rgb, tabMask);

    // 5. Composite scene over wallpaper (SrcOver).
    //    At rest (mask\u22481.0): a \u2248 scene.a \u2014 glass scene composited at natural opacity.
    //    When pressed (mask=smoothstep): a = scene.a * mask \u2014 CombinedBackdrop clip.
    float a = scene.a * mask;
    vec3 resultRgb = mix(wp.rgb, sceneColor, a);

    // 6. \u5185\u5C42\u80CC\u666F\u677F rim highlight \u2014 faithful to LiquidBottomTabs.kt hidden Row:
    //    highlight = { Highlight.Default.copy(alpha = progress) }
    //    The HighlightModifier draws a STROKE (width=0.5dp, strokeWidth=2px)
    //    blurred by 0.25dp, clipped inside the capsule, colored by the
    //    DefaultHighlightShaderString AGSL shader:
    //      float2 grad = gradSdRoundedRect(centeredCoord, halfSize, gradRadius);
    //      float2 normal = float2(cos(angle), sin(angle));
    //      float d = dot(grad, normal);
    //      float intensity = pow(abs(d), falloff);
    //      return color * intensity;   // color = White(1.0), alpha=1*progress
    //    with angle=45\xB0, falloff=1, gradRadius = min(radius*1.5, min(halfW, halfH)).
    //    The stroke's outward half (capsuleSd > 0) is clipped, leaving the inner
    //    half. Final contribution = White(1.0) * intensity * strokeMask * progress,
    //    added with Plus blend (additive).
    //    NOTE: this is the SAME as the \u6307\u793A\u5668's own rim highlight (step 2f in
    //    post-passes) \u2014 both use Highlight.Default. The only difference is the
    //    SDF: here it's the \u5185\u5C42\u80CC\u666F\u677F capsule (inset 4dp), there it's the
    //    \u6307\u793A\u5668's own capsule. The shader math is identical.
    //
    //    The stroke mask is now sampled from a pre-rasterized Canvas2D texture
    //    (uInnerStrokeMask) instead of computed analytically (65-tap Gaussian
    //    convolution of a hard-edge stroke band). This gives browser-native Skia
    //    hardware coverage AA \u2014 identical quality to the outer indicator rim
    //    highlight. The Canvas2D pipeline does ctx.clip(path) \u2192 ctx.stroke(path)
    //    \u2192 ctx.filter=blur, which naturally removes the outer half and provides
    //    sub-pixel AA. No per-pixel SDF loops, no smoothstep clipAA needed.
    float highlightAlpha = uIndicatorPressProgress;
    if (highlightAlpha > 0.001) {
        // SDF gradient + Default highlight intensity (angle=45\xB0, falloff=1).
        // This part is identical to the AGSL DefaultHighlightShaderString.
        float indRadius = max(cr, 0.0);
        float indHalfMin = min(capsuleHalf.x, capsuleHalf.y);
        float gradRadius = min(indRadius * 1.5, indHalfMin);
        vec2 grad = gradSdRoundedRect(capsuleLocal, capsuleHalf, gradRadius);
        vec2 normal = vec2(0.70710678, 0.70710678); // cos(45\xB0), sin(45\xB0)
        float d = dot(grad, normal);
        float intensity = pow(abs(d), 1.0);

        // Sample the pre-rasterized Canvas2D stroke mask texture.
        // UV mapping: capsuleLocal (centered, -halfW..+halfW) \u2192 element-local
        // (0..2*halfW) by adding capsuleHalf \u2192 add margin offset \u2192 divide
        // by maskSize. This is the same convention as the outer indicator
        // stroke mask (STROKE_MASK_COMPOSITE_FRAGMENT_SHADER).
        vec2 innerLocal = capsuleLocal + capsuleHalf;
        vec2 innerMaskUv = (innerLocal + uInnerStrokeMaskOffset) / uInnerStrokeMaskSize;
        // Bounds check \u2014 discard samples outside the mask texture.
        float innerMask = 0.0;
        if (innerMaskUv.x >= 0.0 && innerMaskUv.x <= 1.0 &&
            innerMaskUv.y >= 0.0 && innerMaskUv.y <= 1.0) {
            innerMask = texture2D(uInnerStrokeMask, innerMaskUv).a;
        }

        // White(0.5) * intensity * innerMask * progress, Plus blend (additive).
        // Faithful to HighlightStyle.Default: color = White.copy(alpha=0.5f).
        // The AGSL shader uses this 0.5 alpha, NOT color.copy(alpha=1f).
        // Same fix as DEFAULT_HIGHLIGHT.alpha = 0.5 (was previously 1.0).
        // No clipAA needed \u2014 the Canvas2D clip(path) before stroke already removes
        // the outer half, and Skia hardware coverage provides AA.
        resultRgb += vec3(0.5) * intensity * innerMask * highlightAlpha;
    }

    return vec4(resultRgb, 1.0);
}

// Magnifier backdrop sampling \u2014 faithful to MagnifierContent.kt's
// onDrawBackdrop: withTransform({ scale(1.5); translate(top=-80dp) }, drawBackdrop).
// Zoom around the magnifier center, then offset Y toward cursor.
vec4 sampleMagnifier(vec2 canvasPx, float radius) {
    vec2 magCenter = uElementOffset + uElementSize * 0.5;
    vec2 zoomedCoord = magCenter + (canvasPx - magCenter) / uMagnifierZoom;
    vec2 cursorCoord = vec2(zoomedCoord.x, zoomedCoord.y + uMagnifierOffsetY);
    return sampleBackdrop(cursorCoord, radius);
}

// colorControls \u2014 exact port of ColorFilter.kt colorControlsColorFilter.
// saturation 1.5, brightness 0, contrast 1 -> pure saturation boost.
vec3 applyColorControls(vec3 c, float brightness, float contrast, float saturation) {
    float invSat = 1.0 - saturation;
    float r = 0.213 * invSat;
    float g = 0.715 * invSat;
    float b = 0.072 * invSat;
    float t = (0.5 - contrast * 0.5 + brightness) * 255.0;
    float cs = contrast * saturation;
    float cr = contrast * r;
    float cg = contrast * g;
    float cb = contrast * b;
    vec3 outc;
    outc.r = (cr + cs) * c.r + cg * c.g + cb * c.b + t / 255.0;
    outc.g = cr * c.r + (cg + cs) * c.g + cb * c.b + t / 255.0;
    outc.b = cr * c.r + cg * c.g + (cb + cs) * c.b + t / 255.0;
    return outc;
}

// --- HSV conversion + BlendMode.Hue ---------------------------
// Faithful port of Skia's BlendMode.Hue (non-separable blend).
// Hue blend: result takes hue from src, saturation+value from dst.
// Used by drawRect(tint, BlendMode.Hue) in onDrawSurface.
vec3 rgb2hsv(vec3 c) {
    float maxC = max(c.r, max(c.g, c.b));
    float minC = min(c.r, min(c.g, c.b));
    float delta = maxC - minC;
    float v = maxC;
    float s = maxC < 1e-6 ? 0.0 : delta / maxC;
    float h = 0.0;
    if (delta > 1e-6) {
        if (maxC == c.r) {
            h = mod((c.g - c.b) / delta, 6.0);
        } else if (maxC == c.g) {
            h = (c.b - c.r) / delta + 2.0;
        } else {
            h = (c.r - c.g) / delta + 4.0;
        }
        h *= 60.0;
        if (h < 0.0) h += 360.0;
    }
    return vec3(h / 360.0, s, v);
}

vec3 hsv2rgb(vec3 c) {
    float h = c.x * 6.0;
    float s = c.y;
    float v = c.z;
    float i = floor(h);
    float f = h - i;
    float p = v * (1.0 - s);
    float q = v * (1.0 - s * f);
    float t = v * (1.0 - s * (1.0 - f));
    i = mod(i, 6.0);
    if (i < 1.0) return vec3(v, t, p);
    if (i < 2.0) return vec3(q, v, p);
    if (i < 3.0) return vec3(p, v, t);
    if (i < 4.0) return vec3(p, q, v);
    if (i < 5.0) return vec3(t, p, v);
    return vec3(v, p, q);
}

// BlendMode.Hue: take hue from src, sat+val from dst.
vec3 blendHue(vec3 dst, vec3 src) {
    vec3 dh = rgb2hsv(dst);
    vec3 sh = rgb2hsv(src);
    return hsv2rgb(vec3(sh.x, dh.y, dh.z));
}
`}function Yt(i=Me){let e=xt(i);return`
precision highp float;

${vt}

${U}

${he}

${e}

void main() {
    // gl_FragCoord origin is bottom-left in WebGL; flip to top-left.
    vec2 screenCoord = vec2(gl_FragCoord.x, uCanvasSize.y - gl_FragCoord.y);
    // Content scale (non-uniform): when < 1.0, compress the backdrop UV toward
    // the element center. Faithful to LiquidToggle.kt / LiquidSlider.kt:
    //   scale(scaleX, scaleY) { drawBackdrop() }
    // At rest (progress=0), Y scale = 0 \u2192 degenerate (single horizontal line),
    // but the white overlay hides it. When pressed, scales to full.
    vec2 contentScale = vec2(uContentScaleX, uContentScaleY);
    vec2 sampleCoord = screenCoord;
    if (uContentScaleX < 0.999 || uContentScaleY < 0.999) {
        vec2 elementCenter = uElementOffset + uElementSize * 0.5;
        sampleCoord = elementCenter + (screenCoord - elementCenter) * contentScale;
    }

    // --- ORIGINAL-SPACE SDF (faithful to graphicsLayer { scaleX, scaleY }) ---
    // The original applies the refraction shader at the ORIGINAL element size,
    // THEN scales the entire rendered layer by (scaleX, scaleY). To replicate
    // this in a single-pass shader, we:
    //   1. Compute the centered coord in SCREEN space (relative to element center)
    //   2. Divide by uLayerScale to map back to ORIGINAL space
    //   3. Compute SDF/refraction in ORIGINAL space (shape is correct, not stretched)
    //   4. Map the refraction offset back to SCREEN space for backdrop sampling
    //      (offset_screen = offset_orig * uLayerScale)
    //
    // elementCenter is the SAME for scaled and original rects (scaling is around
    // the center), so uElementOffset + uElementSize*0.5 gives the correct center.
    vec2 elementCenter = uElementOffset + uElementSize * 0.5;
    vec2 centeredScreen = screenCoord - elementCenter;
    // Map to original space (guard against divide-by-zero).
    vec2 layerScale = max(uLayerScale, vec2(1e-4));
    vec2 centeredOrig = centeredScreen / layerScale;
    // Apply element rotation (graphicsLayer rotationZ). Un-rotate the sample
    // coord into the element's local space so the SDF shape appears rotated
    // by +rotation. The layer is rotated AFTER shading, so we shade in local
    // (un-rotated) space. Refraction offsets computed in local space are
    // rotated BACK to screen space (by +rotation) before sampling the backdrop.
    float rot = uElementRotation;
    vec2 centeredOrigRot = rotateBy(centeredOrig, -rot);

    vec2 origHalfSize = uOriginalSize * 0.5;
    float origRadius = uOriginalCornerRadius;

    // --- SDF-texture glass path (faithful to SdfShader.kt) ---
    if (uUseSdfTexture > 0.5) {
        vec2 localPx = centeredOrigRot + uOriginalSize * 0.5;
        vec4 sdfData = sampleSdfTexture(localPx);
        if (sdfData.y <= 0.0) discard;
        float intensity = sdfData.x;
        float sdfMask = sdfData.y;
        vec2 normal = sdfData.zw;

        // Sample the WALLPAPER directly (not the scene FBO) \u2014 faithful to
        // LockScreenContent.kt's drawPlainBackdrop which uses the LayerBackdrop
        // (raw wallpaper, before the dark scrim is drawn).
        // The original applies blur(2dp) BEFORE the SDF shader (in the effects
        // block), so 'content' (the SDF shader's input) is already blurred.
        // We replicate by sampling the wallpaper with a 9-tap poisson blur at
        // the refracted coordinate.
        vec2 refractedOffsetOrig = intensity * uRefractionHeight * normal;
        vec2 refractedOffsetScreen = refractedOffsetOrig * layerScale;
        vec2 refractedScreen = screenCoord - refractedOffsetScreen;

        // Faithful to SdfShader.kt: color = content.eval(refractedCoord) * v.a
        // The content is the wallpaper after colorControls + blur(2dp).
        // FAITHFUL ORDERING: the original's onDrawBackdrop draws the wallpaper
        // AND drawRect(White 0.25) into the same buffer, THEN applies the
        // RenderEffect chain (colorControls, blur, SDF shader). So the white
        // overlay is PART of the SDF shader content input, and colorControls
        // is applied to the COMBINED (wallpaper + white) buffer.
        // We replicate: mix white into raw wallpaper FIRST, then apply
        // colorControls \u2014 so colorControls darkens the white too (matching
        // the original where contrast=0.75, brightness=-0.1 dims the white).
        vec4 content = sampleWallpaperBlurred(refractedScreen, uBlurRadius);
        vec3 rawContent = content.rgb;
        // Mix in white overlay (White 0.25 SrcOver) on RAW wallpaper first.
        if (uSurfaceColor.a > 0.001) {
            rawContent = uSurfaceColor.rgb * uSurfaceColor.a + rawContent * (1.0 - uSurfaceColor.a);
        }
        // THEN apply colorControls to the combined buffer.
        vec3 contentColor = applyColorControls(rawContent, uBrightness, uContrast, uSaturation);
        // Multiply by sdfMask (v.a) \u2014 faithful to content * v.a.
        vec3 color = contentColor * sdfMask;

        // Bevel lighting
        float angleRad = uSdfLightAngle * 3.1415926 / 180.0;
        vec2 lightDir = vec2(cos(angleRad), sin(angleRad));
        float bevel1 = clamp(dot(normal, lightDir), 0.0, 1.0);
        color.rgb *= 1.0 + 0.5 * intensity * bevel1;
        float bevel2 = clamp(dot(normal, -lightDir), 0.0, 1.0);
        color.rgb *= 1.0 + 0.5 * bevel2 * min(1.0, smoothstep(1.0, 0.0, abs(intensity - 0.25) * 6.0));

        gl_FragColor = vec4(color, sdfMask * uEnterAlpha);
        return;
    }

    // SDF for refraction/highlight \u2014 always analytic sdRoundedRect.
    float sd = sdShape(centeredOrigRot, origHalfSize, origRadius);
    // Clip + edgeAA: alpha mask (browser-native AA) when capsule enabled.
    float edgeAlpha;
    if (uUseContinuousSdf > 0.5) {
        float mask = sampleClipMask(centeredOrigRot, origHalfSize, origRadius);
        if (mask < 0.01) discard;
        edgeAlpha = mask;
    } else {
        if (sd > 0.5) discard;
        edgeAlpha = 1.0 - smoothstep(-0.5, 0.5, sd);
    }

    // --- 1. Backdrop sample (before refraction) -------------------
    // Use sampleCoord (content-scaled) so the backdrop shrinks inward when
    // uContentScaleX/Y < 1.0 (toggle/slider knob press effect).
    vec4 backdrop;
    if (uIndicatorBackdrop > 0.5) {
        backdrop = sampleIndicatorBackdrop(screenCoord, uBlurRadius);
    } else if (uUseToggleBackdrop > 0.5) {
        backdrop = sampleToggleBackdrop(screenCoord, uBlurRadius);
    } else if (uUseMagnifier > 0.5) {
        backdrop = sampleMagnifier(screenCoord, uBlurRadius);
    } else {
        backdrop = sampleBackdrop(sampleCoord, uBlurRadius);
    }
    // colorControls: for backdropFbo+useSeparableBlur elements, cc was already
    // applied as a fullscreen pass BEFORE the 2-pass blur (uSkipColorControls=1),
    // matching the original's colorControls\u2192blur order. Skip here to avoid
    // double-applying. For inline-blur elements, apply here.
    vec3 color = (uSkipColorControls > 0.5) ? backdrop.rgb : applyColorControls(backdrop.rgb, uBrightness, uContrast, uSaturation);
    // Magnifier glass is always OPAQUE \u2014 faithful to the original which
    // samples rememberCombinedBackdrop (wallpaper + content + cursor all
    // composited onto the opaque wallpaper). The port's scene texture may
    // carry partial alpha (e.g. card 0.9), which would make the glass
    // translucent. Force alpha=1 for magnifier.
    float alpha = (uUseMagnifier > 0.5) ? 1.0 : backdrop.a;

    // --- 2. Lens refraction (SDF + circleMap) ---------------------
    // Faithful port of RoundedRectRefractionWithDispersionShaderString.
    // SDF/grad computed in ORIGINAL space; uRefractionHeight/Amount are in
    // original px (NOT scaled by layerScale \u2014 the original AGSL shader receives
    // the original size and the graphicsLayer scales the OUTPUT, not the params).
    // Early-out: if we're deeper than refractionHeight from the edge,
    // skip refraction entirely (the lens doesn't reach here).
    if (uRefractionHeight > 0.5 && (-sd) < uRefractionHeight) {
        float sdClamped = min(sd, 0.0);
        float d = circleMap(1.0 - (-sdClamped) / uRefractionHeight) * uRefractionAmount;

        float gradRadius = min(origRadius * 1.5, min(origHalfSize.x, origHalfSize.y));
        vec2 grad = gradSdRoundedRect(centeredOrigRot, origHalfSize, gradRadius);
        // AGSL: normalize(grad + depthEffect * normalize(centeredCoord))
        vec2 depthVec = vec2(0.0);
        if (uDepthEffect > 0.5) {
            float dirLen = length(centeredOrigRot);
            if (dirLen > 1e-6) depthVec = centeredOrigRot / dirLen;
        }
        vec2 gradSum = grad + uDepthEffect * depthVec;
        float gradLen = length(gradSum);
        if (gradLen > 1e-6) grad = gradSum / gradLen;

        // Refraction offset in ORIGINAL space, then map to SCREEN space.
        //   offset_orig = d * grad          (original px)
        //   offset_screen = offset_orig * layerScale  (screen px, for sampling)
        // Faithful to: AGSL computes offset in original space, then graphicsLayer
        // scales the rendered output \u2014 so a pixel at original position p samples
        // the backdrop at p + offset_orig, and the result appears at screen
        // position center + p*layerScale. The backdrop sample position in screen
        // space is therefore center + (p + offset_orig)*layerScale
        // = screenCoord + offset_orig * layerScale.
        vec2 refractedOffsetOrig = d * grad;
        // Rotate the local-space offset BACK to screen space (by +rotation),
        // then scale by layerScale. Without the rotation, refraction points
        // in the wrong direction when the element is rotated.
        vec2 refractedOffsetScreen = rotateBy(refractedOffsetOrig, rot) * layerScale;
        vec2 refractedScreen = screenCoord + refractedOffsetScreen;
        vec2 refractedSampleCoord = refractedScreen;
        if (uIndicatorBackdrop < 0.5 && uUseToggleBackdrop < 0.5 &&
            (uContentScaleX < 0.999 || uContentScaleY < 0.999)) {
            refractedSampleCoord = elementCenter + (refractedScreen - elementCenter) * contentScale;
        }

        if (uChromaticAberration > 0.5) {
            // Faithful 7-path chromatic dispersion (ROYGBV + purple).
            // Original AGSL: dispersionIntensity = chromaticAberration * (cx*cy)/(hx*hy)
            //                dispersedCoord = d * grad * dispersionIntensity
            // 7 samples at dispersedCoord * {1, 2/3, 1/3, 0, -1/3, -2/3, -1}
            // with weighted channel accumulation.
            float dispersionIntensity = 1.0 * ((centeredOrigRot.x * centeredOrigRot.y) / (origHalfSize.x * origHalfSize.y));
            vec2 dispersedOffsetOrig = refractedOffsetOrig * dispersionIntensity;
            vec2 dispersedOffsetScreen = rotateBy(dispersedOffsetOrig, rot) * layerScale;

            // Sample helper \u2014 pick the right backdrop sampler.
            #define SAMPLE_DISPERSED(offset)                 (uIndicatorBackdrop > 0.5 ? sampleIndicatorBackdrop(refractedScreen + (offset), uBlurRadius) :                  uUseToggleBackdrop > 0.5 ? sampleToggleBackdrop(refractedScreen + (offset), uBlurRadius) :                  uUseMagnifier > 0.5 ? sampleMagnifier(refractedScreen + (offset), uBlurRadius) :                  sampleBackdrop(refractedSampleCoord + (offset), uBlurRadius))

            vec4 sRed    = SAMPLE_DISPERSED(+dispersedOffsetScreen);
            vec4 sOrange = SAMPLE_DISPERSED(+dispersedOffsetScreen * (2.0 / 3.0));
            vec4 sYellow = SAMPLE_DISPERSED(+dispersedOffsetScreen * (1.0 / 3.0));
            vec4 sGreen  = SAMPLE_DISPERSED(vec2(0.0));
            vec4 sCyan   = SAMPLE_DISPERSED(-dispersedOffsetScreen * (1.0 / 3.0));
            vec4 sBlue   = SAMPLE_DISPERSED(-dispersedOffsetScreen * (2.0 / 3.0));
            vec4 sPurple = SAMPLE_DISPERSED(-dispersedOffsetScreen);

            #undef SAMPLE_DISPERSED

            // Faithful channel weighting from the original AGSL shader.
            vec3 dispColor = vec3(0.0);
            float dispAlpha = 0.0;
            // red
            dispColor.r += sRed.r / 3.5;
            dispAlpha  += sRed.a / 7.0;
            // orange
            dispColor.r += sOrange.r / 3.5;
            dispColor.g += sOrange.g / 7.0;
            dispAlpha  += sOrange.a / 7.0;
            // yellow
            dispColor.r += sYellow.r / 3.5;
            dispColor.g += sYellow.g / 3.5;
            dispAlpha  += sYellow.a / 7.0;
            // green
            dispColor.g += sGreen.g / 3.5;
            dispAlpha  += sGreen.a / 7.0;
            // cyan
            dispColor.g += sCyan.g / 3.5;
            dispColor.b += sCyan.b / 3.0;
            dispAlpha  += sCyan.a / 7.0;
            // blue
            dispColor.b += sBlue.b / 3.0;
            dispAlpha  += sBlue.a / 7.0;
            // purple
            dispColor.r += sPurple.r / 7.0;
            dispColor.b += sPurple.b / 3.0;
            dispAlpha  += sPurple.a / 7.0;

            color = (uSkipColorControls > 0.5) ? dispColor : applyColorControls(dispColor, uBrightness, uContrast, uSaturation);
            // Magnifier chromatic aberration also forces opaque.
            alpha = (uUseMagnifier > 0.5) ? 1.0 : dispAlpha;
        } else {
            vec4 refracted;
            if (uIndicatorBackdrop > 0.5) {
                refracted = sampleIndicatorBackdrop(refractedScreen, uBlurRadius);
            } else if (uUseToggleBackdrop > 0.5) {
                refracted = sampleToggleBackdrop(refractedScreen, uBlurRadius);
            } else if (uUseMagnifier > 0.5) {
                refracted = sampleMagnifier(refractedScreen, uBlurRadius);
            } else {
                refracted = sampleBackdrop(refractedSampleCoord, uBlurRadius);
            }
            color = (uSkipColorControls > 0.5) ? refracted.rgb : applyColorControls(refracted.rgb, uBrightness, uContrast, uSaturation);
            // Magnifier refraction also forces opaque (see backdrop sample above).
            alpha = (uUseMagnifier > 0.5) ? 1.0 : refracted.a;
        }
    }

    // --- 3. onDrawSurface: tint (BlendMode.Hue + 0.75 alpha) -----
    // Faithful port of LiquidButton.kt onDrawSurface:
    //   drawRect(tint, blendMode = BlendMode.Hue)
    //   drawRect(tint.copy(alpha = 0.75f))
    // First pass: replace backdrop hue with tint hue (Hue blend, alpha = tint.a).
    // Second pass: overlay tint color at 0.75*alpha (SrcOver blend).
    if (uTintColor.a > 0.001) {
        vec3 hueBlended = blendHue(color, uTintColor.rgb);
        color = mix(color, hueBlended, uTintColor.a);
        color = mix(color, uTintColor.rgb, 0.75 * uTintColor.a);
    }

    // --- 4. onDrawSurface: surfaceColor (drawRect(surfaceColor)) --
    if (uSurfaceColor.a > 0.001) {
        color = mix(color, uSurfaceColor.rgb, uSurfaceColor.a);
    }

    // --- 5. Highlight (edge specular) -----------------------------
    // NOTE: The rim highlight is drawn as a SEPARATE pass (see
    // RIM_HIGHLIGHT_FRAGMENT_SHADER) with true Plus/SrcOver blend,
    // matching the original HighlightModifier.kt which records a separate
    // graphics layer. Doing it inline here would dim the highlight via the
    // element's edge AA, which is wrong \u2014 the highlight layer is composited
    // on top with its own blend mode.

    // --- 7. Edge anti-aliasing -----------------------------------
    // edgeAlpha was computed earlier (mask mode: direct coverage, analytic: smoothstep).
    gl_FragColor = vec4(color, alpha * edgeAlpha * uEnterAlpha);
}
`}var We=Yt(Me);var Xe=`
precision highp float;

uniform vec2  uCanvasSize;
uniform vec2  uElementOffset;   // SCALED rect top-left (where the quad is drawn)
uniform vec2  uElementSize;     // SCALED size (includes graphicsLayer scale)
uniform vec4  uCornerRadii;     // SCALED corner radii
uniform float uShadowRadius;    // ORIGINAL px (NOT scaled \u2014 faithful to BlurMaskFilter at original size)
uniform vec2  uShadowOffset;    // ORIGINAL px (offsetX, offsetY; +Y = downward)
uniform vec4  uShadowColor;     // rgba
// --- ORIGINAL-SPACE SDF (faithful to graphicsLayer { scaleX, scaleY }) ---
// Same approach as the element shader: compute the shadow SDF in ORIGINAL
// space (shape is a correct capsule, not stretched), then the graphicsLayer
// scales the entire shadow layer by (scaleX, scaleY). The shadow offset is
// in ORIGINAL px; we multiply by uLayerScale to map it to screen space for
// the SDF evaluation (offset_screen = offset_orig * layerScale). The shadow
// radius (blur sigma) stays in ORIGINAL px because the Gaussian falloff is
// computed in original space \u2014 the graphicsLayer then stretches the blurred
// result, which is the faithful behavior (BlurMaskFilter blurs at original
// resolution, then graphicsLayer scales the blurred pixels).
uniform vec2  uOriginalSize;        // element size in px (ORIGINAL, unscaled)
uniform float uOriginalCornerRadius; // corner radius in px (ORIGINAL, unscaled)
uniform vec2  uLayerScale;          // (scaleX, scaleY) from graphicsLayer
uniform float uElementRotation;     // rotation in radians (graphicsLayer rotationZ)

${U}

void main() {
    // Flip gl_FragCoord (bottom-left origin) to top-left origin, so +Y
    // points downward \u2014 matching CSS convention.
    vec2 screenCoord = vec2(gl_FragCoord.x, uCanvasSize.y - gl_FragCoord.y);
    // elementCenter is the SAME for scaled and original rects (scaling is
    // around the center), so uElementOffset + uElementSize*0.5 gives the
    // correct center.
    vec2 elementCenter = uElementOffset + uElementSize * 0.5;
    vec2 centeredScreen = screenCoord - elementCenter;
    // Map to ORIGINAL space (guard against divide-by-zero).
    vec2 layerScale = max(uLayerScale, vec2(1e-4));
    vec2 centeredOrig = centeredScreen / layerScale;
    // Un-rotate into local space so the shadow shape rotates with the element.
    // Also rotate the shadow offset into local space so it stays consistent.
    vec2 centeredOrigRot = rotateBy(centeredOrig, -uElementRotation);
    vec2 shadowOffsetRot = rotateBy(uShadowOffset, -uElementRotation);

    vec2 origHalfSize = uOriginalSize * 0.5;
    float origRadius = uOriginalCornerRadius;

    // Shadow offset: defined in ORIGINAL px, applied in screen space.
    // The original draws the shadow at original size with this offset, then
    // graphicsLayer scales the whole layer \u2014 so the offset effectively
    // becomes offset_orig * layerScale in screen space. We map it back to
    // original space for the SDF: offset_orig = offset_screen / layerScale,
    // which cancels \u2014 so we use uShadowOffset directly in original space.
    vec2 shadowCenteredOrig = centeredOrigRot - shadowOffsetRot;
    float sd = sdShape(shadowCenteredOrig, origHalfSize, origRadius);
    // SDF of the element itself (not offset) \u2014 used to mask the shadow
    // inside the element so it doesn't bleed through the AA edge.
    float elementSd = sdShape(centeredOrigRot, origHalfSize, origRadius);

    // Shadow intensity: Gaussian falloff from the shadow shape's edge.
    // uShadowRadius is in ORIGINAL px (faithful to BlurMaskFilter at original
    // size). sigma = radius/3 matches the BlurMaskFilter spread.
    float sigma = max(uShadowRadius / 3.0, 1.0);
    float shadow = 0.5 * exp(-sd * sd / (2.0 * sigma * sigma));
    // Mask out the shadow inside the element (the element covers it).
    shadow *= smoothstep(-1.0, 1.0, elementSd);

    gl_FragColor = vec4(uShadowColor.rgb, uShadowColor.a * shadow);
}
`,Vt=`
precision highp float;

uniform vec2  uCanvasSize;
uniform vec2  uElementOffset;
uniform vec2  uElementSize;
uniform vec4  uCornerRadii;
uniform float uInnerShadowRadius;
uniform float uInnerShadowAlpha;
uniform vec2  uInnerShadowOffset;

${U}

void main() {
    vec2 screenCoord = vec2(gl_FragCoord.x, uCanvasSize.y - gl_FragCoord.y);
    vec2 localCoord = screenCoord - uElementOffset;
    vec2 halfSize = uElementSize * 0.5;
    vec2 centeredCoord = localCoord - halfSize;

    float radius = radiusAt(centeredCoord, uCornerRadii);
    float sd = sdShape(centeredCoord, halfSize, radius);
    if (sd > 0.5) discard;

    vec2 innerCentered = centeredCoord - uInnerShadowOffset;
    float innerSd = sdShape(innerCentered, halfSize, radius);
    float band = smoothstep(uInnerShadowRadius, 0.0, innerSd);
    band *= step(0.0, innerSd);
    gl_FragColor = vec4(0.0, 0.0, 0.0, band * uInnerShadowAlpha * 0.5);
}
`;var Ye=`
precision highp float;

uniform vec2  uCanvasSize;
uniform vec2  uOffset;       // element top-left in canvas px (top-left origin) \u2014 SCALED rect
uniform vec2  uSize;         // element size in canvas px \u2014 SCALED
uniform vec4  uCornerRadii;  // capsule radii (topLeft, topRight, bottomRight, bottomLeft) in px \u2014 SCALED
uniform vec4  uColor;        // rgba; usually white * (alpha = 0.15 * progress)
uniform float uRadius;       // glow radius in canvas px (= minDim * 1.5, SCALED space)
uniform vec2  uPosition;     // finger position in element-local px (top-left origin, SCALED space)
// --- ORIGINAL-SPACE SDF clip (faithful to graphicsLayer { scaleX, scaleY }) ---
// The press glow (InteractiveHighlight) is drawn INSIDE the graphicsLayer, so
// it is clipped to the ORIGINAL capsule shape, then scaled with the layer.
// The glow position + radius are in SCALED space (they track the finger in
// screen px), but the clip SDF is in original space so the capsule clip stays
// correct when the button is stretched.
uniform vec2  uOriginalSize;
uniform float uOriginalCornerRadius;
uniform vec2  uLayerScale;
uniform float uElementRotation;

${U}

void main() {
    vec2 screenCoord = vec2(gl_FragCoord.x, uCanvasSize.y - gl_FragCoord.y);
    vec2 localCoord = screenCoord - uOffset;

    // --- Capsule clip in ORIGINAL space (faithful to graphicsLayer clip) ---
    vec2 elementCenter = uOffset + uSize * 0.5;
    vec2 centeredScreen = screenCoord - elementCenter;
    vec2 layerScale = max(uLayerScale, vec2(1e-4));
    vec2 centeredOrig = centeredScreen / layerScale;
    vec2 origHalfSize = uOriginalSize * 0.5;
    float sd = sdShape(rotateBy(centeredOrig, -uElementRotation), origHalfSize, uOriginalCornerRadius);
    if (sd > 0.5) discard;
    float clipAlpha = 1.0 - smoothstep(-0.5, 0.5, sd);

    // Faithful AGSL port: smoothstep(radius, radius*0.5, dist) means
    // intensity = 1 at dist <= radius*0.5, fading to 0 at dist >= radius.
    // dist + uPosition are in SCALED local space (finger tracks screen px).
    float dist = distance(localCoord, uPosition);
    float intensity = smoothstep(uRadius, uRadius * 0.5, dist);

    // Premultiplied Plus-blend contribution. Renderer uses blendFunc(ONE, ONE)
    // so result.rgb = contribution + dst.rgb (clamped to 1).
    vec3 contribution = uColor.rgb * uColor.a * intensity * clipAlpha;
    gl_FragColor = vec4(contribution, 1.0);
}
`,Ve=`
precision highp float;

uniform vec2  uCanvasSize;
uniform vec2  uOffset;
uniform vec2  uSize;
uniform vec4  uCornerRadii;
uniform vec4  uColor;
// --- ORIGINAL-SPACE SDF clip (faithful to graphicsLayer { scaleX, scaleY }) ---
// The white overlay (onDrawSurface drawRect) is drawn INSIDE the graphicsLayer,
// so it is clipped to the ORIGINAL capsule shape, then scaled with the layer.
// Computing the clip SDF in original space keeps the capsule clip correct when
// the button is stretched (no corner bleed, no stretched-clip artifacts).
uniform vec2  uOriginalSize;
uniform float uOriginalCornerRadius;
uniform vec2  uLayerScale;
uniform float uElementRotation;

${U}

void main() {
    vec2 screenCoord = vec2(gl_FragCoord.x, uCanvasSize.y - gl_FragCoord.y);
    vec2 elementCenter = uOffset + uSize * 0.5;
    vec2 centeredScreen = screenCoord - elementCenter;
    vec2 layerScale = max(uLayerScale, vec2(1e-4));
    vec2 centeredOrig = centeredScreen / layerScale;
    vec2 origHalfSize = uOriginalSize * 0.5;
    float sd = sdShape(rotateBy(centeredOrig, -uElementRotation), origHalfSize, uOriginalCornerRadius);
    if (sd > 0.5) discard;
    float clipAlpha = 1.0 - smoothstep(-0.5, 0.5, sd);

    gl_FragColor = vec4(uColor.rgb, uColor.a * clipAlpha);
}
`,qe=`
precision highp float;

uniform vec2  uCanvasSize;
uniform vec2  uOffset;          // element top-left in canvas px (top-left origin) \u2014 SCALED rect
uniform vec2  uSize;            // element size in canvas px \u2014 SCALED (includes graphicsLayer scale)
uniform vec4  uCornerRadii;     // (topLeft, topRight, bottomRight, bottomLeft) in px \u2014 SCALED
uniform vec4  uHighlightColor;  // rgb + 1.0
uniform float uHighlightAngle;  // radians
uniform float uHighlightFalloff;
uniform float uHighlightAlpha;
uniform float uHighlightMode;     // 0=Default, 1=Ambient, 2=Plain
uniform float uHighlightStrokeWidth;
uniform float uHighlightBlur;
// --- ORIGINAL-SPACE SDF (faithful to graphicsLayer { scaleX, scaleY }) ---
// Same approach as the element shader: compute SDF/stroke in ORIGINAL space
// (shape is correct, not stretched), so the highlight clip + stroke remain a
// correct capsule shape that is then scaled by graphicsLayer. Without this,
// a horizontally-stretched button would stretch the highlight clip too,
// making the stroke band uneven. See element.ts for the full rationale.
uniform vec2  uOriginalSize;        // element size in px (ORIGINAL, unscaled)
uniform float uOriginalCornerRadius; // corner radius in px (ORIGINAL, unscaled)
uniform vec2  uLayerScale;          // (scaleX, scaleY) from graphicsLayer
uniform float uElementRotation;     // rotation in radians (graphicsLayer rotationZ)

${U}

void main() {
    vec2 screenCoord = vec2(gl_FragCoord.x, uCanvasSize.y - gl_FragCoord.y);
    // elementCenter is the SAME for scaled and original rects (scaling is
    // around the center), so uOffset + uSize*0.5 gives the correct center.
    vec2 elementCenter = uOffset + uSize * 0.5;
    vec2 centeredScreen = screenCoord - elementCenter;
    // Map to ORIGINAL space (guard against divide-by-zero).
    vec2 layerScale = max(uLayerScale, vec2(1e-4));
    vec2 centeredOrig = centeredScreen / layerScale;
    // Un-rotate into the element's local space so the SDF shape rotates.
    vec2 centeredOrigRot = rotateBy(centeredOrig, -uElementRotation);

    vec2 origHalfSize = uOriginalSize * 0.5;
    float origRadius = uOriginalCornerRadius;

    // SDF for stroke \u2014 analytic sdRoundedRect (matches the pre-capsule
    // highlight implementation). When capsule is OFF, this is the exact
    // shape. When capsule is ON, this is a close approximation (circular
    // arc vs G2 Bezier \u2014 the difference is sub-pixel within the 2px stroke
    // band, invisible in the highlight).
    float sd = sdRoundedRect(centeredOrigRot, origHalfSize, origRadius);

    // Outside the shape \u2014 clip (hard discard, matching pre-capsule behavior).
    if (sd > 0.0) discard;

    // Stroke mask \u2014 faithful to HighlightModifier.kt:
    //   paint.style = Stroke
    //   paint.strokeWidth = ceil(width.toPx()) * 2     // full stroke, centered on edge
    //   paint.blur(blurRadius.toPx())                   // BlurMaskFilter, Blur.NORMAL
    //   canvas.clipOutline(outline)                     // clip to inside the shape
    //   canvas.drawOutline(outline, paint)              // stroke centered on edge
    //
    // Implementation: first compute a HARD-EDGE stroke mask (1.0 inside the
    // stroke band, 0.0 outside), then convolve it with a Gaussian kernel by
    // sampling the SDF at multiple offsets along the gradient direction.
    // This mirrors the original's two-step process (draw stroke \u2192 blur),
    // rather than using an analytic erf approximation.
    //
    // The hard stroke band: sd in [-strokeHalf, +strokeHalf].
    // After clip (sd > 0 discarded by the outer if), only [-strokeHalf, 0] shows.
    //
    // Faithful to the original BlurMaskFilter:
    //   paint.blur(blurRadius.toPx())  \u2192  BlurMaskFilter(NORMAL, sigma=blurRadius_px)
    // In Skia/Android, BlurMaskFilter's radius param IS the Gaussian sigma
    // (not radius/3). blurRadius = width/2 = 0.25dp, so sigma = 0.25*dpr px.
    // uHighlightBlur is already in device px (set by the renderer as widthDp*dpr*0.5).
    float strokeHalf = uHighlightStrokeWidth * 0.5;
    float sigma = max(uHighlightBlur, 0.1);

    // Gaussian convolution of the hard stroke mask \u2014 3-tap (\u03C3-spaced).
    // The original's BlurMaskFilter has \u03C3 = blurRadius = 0.25dp \u2192 0.25px at
    // dpr=1. At this sub-pixel sigma, only 3 taps (at -\u03C3, 0, +\u03C3) are needed
    // \u2014 the Gaussian weight at \xB12\u03C3 is exp(-2) \u2248 0.14, negligible. This
    // replaces the old 65-tap loop (which computed 65 exp() calls per pixel,
    // ~650 cycles \u2014 the single biggest shader cost). 3 taps = 3 exp() = ~30
    // cycles, a 20\xD7 reduction with identical visual result at \u03C3=0.25.
    //   hardMask(sd) = 1.0 if |sd| < strokeHalf, else 0.0
    //   blurred(sd) = \u03A3 hardMask(sd - offset_k) * gauss(offset_k, \u03C3)
    // CLIP HALVING: the stroke is centered on sd=0; clip removes sd>0 (outer
    // half), so peak \u2248 0.5. We halve to match.
    float strokeMask = 0.0;
    float wSum = 0.0;
    for (int i = -1; i <= 1; i++) {
        float offset = float(i) * sigma;  // taps at -\u03C3, 0, +\u03C3
        float sampleSd = sd - offset;
        float hard = (abs(sampleSd) < strokeHalf) ? 1.0 : 0.0;
        float w = exp(-0.5 * (offset * offset) / (sigma * sigma));
        strokeMask += hard * w;
        wSum += w;
    }
    strokeMask /= wSum;
    strokeMask *= 0.5;  // clip halves the symmetric stroke at the edge

    if (uHighlightMode < 0.5) {
        // Default \u2014 shader returns color * intensity, Plus blend.
        float gradRadius = min(origRadius * 1.5, min(origHalfSize.x, origHalfSize.y));
        vec2 grad = gradSdRoundedRect(centeredOrigRot, origHalfSize, gradRadius);
        vec2 normal = vec2(cos(uHighlightAngle), sin(uHighlightAngle));
        float d = dot(grad, normal);
        float intensity = pow(abs(d), uHighlightFalloff);
        vec3 c = uHighlightColor.rgb * intensity * strokeMask * uHighlightAlpha;
        gl_FragColor = vec4(c, 1.0);
    } else if (uHighlightMode < 1.5) {
        // Ambient \u2014 premultiplied SrcOver blend (renderer uses ONE, ONE_MINUS_SRC_ALPHA).
        // Faithful to AmbientHighlightShaderString:
        //   float d = dot(grad, normal);
        //   float intensity = pow(abs(d), falloff);
        //   float t = step(0.0, d);  \u2190 half-black-half-white split
        //   return half4(t, t, t, 1.0) * intensity;
        // Output is premultiplied: vec4(color.rgb * t * i, i).
        // Bright side: adds white light. Dark side: dims scene \u2192 3D sphere.
        // paint.color(0.38) is overridden by shader; alpha = 1.0 not 0.38.
        float gradRadius = min(origRadius * 1.5, min(origHalfSize.x, origHalfSize.y));
        vec2 grad = gradSdRoundedRect(centeredOrigRot, origHalfSize, gradRadius);
        vec2 normal = vec2(cos(uHighlightAngle), sin(uHighlightAngle));
        float d = dot(grad, normal);
        float intensity = pow(abs(d), uHighlightFalloff);
        float t = step(0.0, d);  // 0 on dark side (d<0), 1 on bright side (d>=0)
        float i = intensity * strokeMask * uHighlightAlpha;
        gl_FragColor = vec4(uHighlightColor.rgb * t * i, i);
    } else {
        // Plain \u2014 even stroke, paint.color, Plus blend.
        vec3 c = uHighlightColor.rgb * strokeMask * uHighlightAlpha;
        gl_FragColor = vec4(c, 1.0);
    }
}
`,Ke=`
precision highp float;

uniform vec2  uCanvasSize;
uniform vec2  uOffset;          // element top-left (top-left origin) \u2014 SCALED
uniform vec2  uSize;            // element size \u2014 SCALED
uniform vec4  uCornerRadii;     // SCALED
uniform float uHighlightStrokeWidth;  // ceil(width*dpr)*2, device px
uniform vec2  uOriginalSize;
uniform float uOriginalCornerRadius;
uniform vec2  uLayerScale;
uniform float uElementRotation;
// uCornerStyle, uUseContinuousSdf, uContinuousSdf, uContinuousSdfTexSize,
// uContinuousSdfElementSize are declared in SDF_GLSL (do NOT redeclare here).

${U}

void main() {
    vec2 screenCoord = vec2(gl_FragCoord.x, uCanvasSize.y - gl_FragCoord.y);
    vec2 elementCenter = uOffset + uSize * 0.5;
    vec2 centeredScreen = screenCoord - elementCenter;
    vec2 layerScale = max(uLayerScale, vec2(1e-4));
    vec2 centeredOrig = centeredScreen / layerScale;
    vec2 centeredOrigRot = rotateBy(centeredOrig, -uElementRotation);

    vec2 origHalfSize = uOriginalSize * 0.5;
    float origRadius = uOriginalCornerRadius;

    float sd = sdShape(centeredOrigRot, origHalfSize, origRadius);

    // clipOutline \u2014 clip to INSIDE the shape. Outside (sd > 0) is discarded.
    float edgeAA;
    if (uUseContinuousSdf > 0.5) {
        float mask = sampleClipMask(centeredOrigRot, origHalfSize, origRadius);
        if (mask < 0.01) discard;
        edgeAA = mask;
    } else {
        if (sd > 0.0) discard;
        edgeAA = 1.0 - smoothstep(-0.5, 0.5, sd);
    }

    // Stroke band centered on the edge (sd = 0), with 0.5px coverage AA on
    // the inner boundary. The outer boundary (sd = +strokeHalf) is clipped
    // away by edgeAA above. Faithful to Skia Paint.Stroke's coverage AA.
    // The BlurMaskFilter pass (when sigma >= 0.5px) softens this further;
    // at sub-pixel sigma (0.25px) the blur is skipped and this 0.5px AA
    // is what matches the original's look (Skia's 0.25px blur is negligibly
    // soft \u2014 essentially just AA).
    float strokeHalf = uHighlightStrokeWidth * 0.5;
    float strokeAA = 1.0 - smoothstep(strokeHalf - 0.5, strokeHalf, abs(sd));

    gl_FragColor = vec4(0.0, 0.0, 0.0, strokeAA * edgeAA);
}
`,$e=`
precision highp float;

uniform vec2  uCanvasSize;
uniform vec2  uOffset;
uniform vec2  uSize;
uniform vec4  uCornerRadii;
uniform sampler2D uBlurredMask;   // the 2-pass-blurred stroke mask FBO
uniform vec2  uMaskTexSize;       // size of the mask FBO (= canvas size)
uniform vec4  uHighlightColor;    // rgb + 1.0
uniform float uHighlightAngle;
uniform float uHighlightFalloff;
uniform float uHighlightAlpha;
uniform float uHighlightMode;     // 0=Default, 1=Ambient, 2=Plain
uniform vec2  uOriginalSize;
uniform float uOriginalCornerRadius;
uniform vec2  uLayerScale;
uniform float uElementRotation;
// uCornerStyle, uUseContinuousSdf, uContinuousSdf, uContinuousSdfTexSize,
// uContinuousSdfElementSize are declared in SDF_GLSL (do NOT redeclare here).

${U}

void main() {
    vec2 screenCoord = vec2(gl_FragCoord.x, uCanvasSize.y - gl_FragCoord.y);

    // Sample the blurred stroke mask at this pixel. The mask FBO covers the
    // full canvas (same size), so UV = gl_FragCoord / maskTexSize.
    // Mask FBO is Y-down (top-left origin, like our scene FBOs), so flip Y
    // to match the screenCoord convention.
    vec2 maskUv = vec2(gl_FragCoord.x / uMaskTexSize.x, gl_FragCoord.y / uMaskTexSize.y);
    float mask = texture2D(uBlurredMask, maskUv).a;
    if (mask < 0.001) discard;

    // Compute intensity from the SDF gradient (AGSL DefaultHighlightShaderString).
    vec2 elementCenter = uOffset + uSize * 0.5;
    vec2 centeredScreen = screenCoord - elementCenter;
    vec2 layerScale = max(uLayerScale, vec2(1e-4));
    vec2 centeredOrig = centeredScreen / layerScale;
    vec2 centeredOrigRot = rotateBy(centeredOrig, -uElementRotation);
    vec2 origHalfSize = uOriginalSize * 0.5;
    float origRadius = uOriginalCornerRadius;

    // Faithful clip-after-blur: the original does clipOutline \u2192 stroke(blur),
    // but Skia applies clip at the canvas level AFTER the BlurMaskFilter
    // spreads alpha. So alpha that blurred OUTSIDE the shape is clipped away.
    // Our stroke shader clips before blur (discard sd>0), then blur spreads
    // alpha back outside \u2014 we must clip AGAIN here to match. Without this,
    // the highlight "leaks" outside the shape, making it brighter than the
    // original (which has zero contribution outside the clip region).
    float sd = sdShape(centeredOrigRot, origHalfSize, origRadius);
    float clipAA;
    if (uUseContinuousSdf > 0.5) {
        clipAA = sampleClipMask(centeredOrigRot, origHalfSize, origRadius);
    } else {
        clipAA = 1.0 - smoothstep(-0.5, 0.5, sd);
    }
    mask *= clipAA;
    if (mask < 0.001) discard;

    // Compute d (with sign) for Default + Ambient modes \u2014 needed for
    // Ambient's step(0,d) half-black-half-white split.
    float d = 0.0;  // signed dot(grad, normal) \u2014 0 for Plain mode
    float intensity;
    if (uHighlightMode < 1.5) {
        // Default + Ambient use the SDF gradient \xB7 normal.
        float gradRadius = min(origRadius * 1.5, min(origHalfSize.x, origHalfSize.y));
        vec2 grad = gradSdRoundedRect(centeredOrigRot, origHalfSize, gradRadius);
        vec2 normal = vec2(cos(uHighlightAngle), sin(uHighlightAngle));
        d = dot(grad, normal);
        intensity = pow(abs(d), uHighlightFalloff);
    } else {
        // Plain \u2014 no directional intensity (even stroke).
        intensity = 1.0;
    }

    float a = mask * uHighlightAlpha;

    if (uHighlightMode < 0.5) {
        // Default \u2014 Plus blend. Output premultiplied rgb (alpha=1 so blendFunc
        // (ONE, ONE) adds rgb directly).
        vec3 c = uHighlightColor.rgb * intensity * a;
        gl_FragColor = vec4(c, 1.0);
    } else if (uHighlightMode < 1.5) {
        // Ambient \u2014 PREMULTIPLIED SrcOver blend (renderer uses ONE, ONE_MINUS_SRC_ALPHA).
        // Faithful to AmbientHighlightShaderString:
        //   float t = step(0.0, d);  \u2190 half-black-half-white split
        // Bright side (d>=0): t=1 \u2192 white highlight. Dark side (d<0): t=0 \u2192
        // black overlay that reduces scene brightness via premultiplied SrcOver \u2192 3D sphere.
        // Output is premultiplied: vec4(color.rgb * t * i, i).
        // IMPORTANT: paint.color = White(0.38) is overridden by the shader.
        // The 0.38 does NOT scale the output; layer alpha (Highlight.alpha) is the
        // only modulation. For Ambient highlight, alpha = 1.0 (not 0.38).
        float t = step(0.0, d);
        float i = intensity * a;
        gl_FragColor = vec4(uHighlightColor.rgb * t * i, i);
    } else {
        // Plain \u2014 Plus blend, no intensity.
        vec3 c = uHighlightColor.rgb * a;
        gl_FragColor = vec4(c, 1.0);
    }
}
`,Ze=`
precision highp float;

uniform vec2  uCanvasSize;
uniform vec2  uOffset;
uniform vec2  uSize;
uniform vec4  uCornerRadii;
uniform sampler2D uStrokeMask;
uniform vec2  uMaskOffset;
uniform vec2  uMaskSize;
uniform vec4  uHighlightColor;
uniform float uHighlightAngle;
uniform float uHighlightFalloff;
uniform float uHighlightAlpha;
uniform float uHighlightMode;
uniform vec2  uOriginalSize;
uniform float uOriginalCornerRadius;
uniform vec2  uLayerScale;
uniform float uElementRotation;

${U}

void main() {
    vec2 screenCoord = vec2(gl_FragCoord.x, uCanvasSize.y - gl_FragCoord.y);

    // Map screen coord \u2192 element-local ORIGINAL space (un-scale, un-rotate).
    // The stroke mask is drawn in original space (origSizeX \xD7 origSizeY + margin).
    // elementCenter is the same in scaled and original space (scaling is around center).
    vec2 elementCenter = uOffset + uSize * 0.5;
    vec2 centeredScreen = screenCoord - elementCenter;
    vec2 layerScale = max(uLayerScale, vec2(1e-4));
    vec2 centeredOrig = centeredScreen / layerScale;
    vec2 centeredOrigRot = rotateBy(centeredOrig, -uElementRotation);

    // Mask UV: map original-space coord \u2192 mask texture UV.
    // The mask was drawn with translate(margin, margin), so mask (0,0) =
    // element-local (-margin). Element-local coord 0..origSize maps to
    // mask UV (0+margin)/maskSize .. (origSize+margin)/maskSize.
    // uMaskOffset = margin (scalar, passed as vec2 for convenience).
    // uMaskSize = (origSize + 2*margin).
    vec2 origHalfSize = uOriginalSize * 0.5;
    vec2 maskTexCoord = centeredOrigRot + origHalfSize;  // 0..origSize (element-local)
    vec2 maskUv = (maskTexCoord + uMaskOffset) / uMaskSize;
    if (maskUv.x < 0.0 || maskUv.x > 1.0 || maskUv.y < 0.0 || maskUv.y > 1.0) discard;
    float mask = texture2D(uStrokeMask, maskUv).a;
    if (mask < 0.001) discard;

    float origRadius = uOriginalCornerRadius;

    // Compute d (with sign) for Default + Ambient modes \u2014 needed for
    // Ambient's step(0,d) half-black-half-white split.
    float d = 0.0;  // signed dot(grad, normal) \u2014 0 for Plain mode
    float intensity;
    if (uHighlightMode < 1.5) {
        float gradRadius = min(origRadius * 1.5, min(origHalfSize.x, origHalfSize.y));
        vec2 grad = gradSdRoundedRect(centeredOrigRot, origHalfSize, gradRadius);
        vec2 normal = vec2(cos(uHighlightAngle), sin(uHighlightAngle));
        d = dot(grad, normal);
        intensity = pow(abs(d), uHighlightFalloff);
    } else {
        intensity = 1.0;
    }

    float a = mask * uHighlightAlpha;
    if (uHighlightMode < 0.5) {
        gl_FragColor = vec4(uHighlightColor.rgb * intensity * a, 1.0);
    } else if (uHighlightMode < 1.5) {
        // Ambient \u2014 premultiplied SrcOver (renderer uses ONE, ONE_MINUS_SRC_ALPHA).
        // Faithful to AmbientHighlightShaderString:
        //   float t = step(0.0, d);  \u2190 bright/dark split
        // Bright side: t=1 \u2192 white highlight. Dark side: t=0 \u2192 dims scene.
        // Output is premultiplied: vec4(color.rgb * t * i, i).
        // paint.color(0.38) is overridden by shader; alpha should be 1.0 not 0.38.
        float t = step(0.0, d);
        float i = intensity * a;
        gl_FragColor = vec4(uHighlightColor.rgb * t * i, i);
    } else {
        gl_FragColor = vec4(uHighlightColor.rgb * a, 1.0);
    }
}
`;var je=`
precision highp float;

uniform vec2  uCanvasSize;
uniform vec2  uOffset;           // element top-left in canvas px (top-left origin) \u2014 SCALED rect
uniform vec2  uSize;             // element size in canvas px \u2014 SCALED
uniform vec4  uCornerRadii;      // (topLeft, topRight, bottomRight, bottomLeft) \u2014 SCALED
uniform sampler2D uInnerShadowMask; // Canvas2D-generated blurred ring mask
uniform vec2  uMaskOffset;       // margin in device px (for UV mapping: element-local \u2192 mask UV)
uniform vec2  uMaskSize;         // total mask size in device px (w+2*margin, h+2*margin)
uniform vec3  uInnerShadowColor; // shadow color RGB
uniform float uInnerShadowAlpha; // shadow alpha
// --- ORIGINAL-SPACE SDF clip (faithful to graphicsLayer { scaleX, scaleY }) ---
uniform vec2  uOriginalSize;        // element size in px (ORIGINAL, unscaled)
uniform float uOriginalCornerRadius; // corner radius in px (ORIGINAL, unscaled)
uniform vec2  uLayerScale;          // (scaleX, scaleY) from graphicsLayer
uniform float uElementRotation;     // rotation in radians (graphicsLayer rotationZ)

${U}

void main() {
    vec2 screenCoord = vec2(gl_FragCoord.x, uCanvasSize.y - gl_FragCoord.y);

    // Map screen coord \u2192 element-local ORIGINAL space (un-scale, un-rotate).
    // The inner shadow mask is drawn in original space (origSize + margin).
    // elementCenter is the same in scaled and original space (scaling is
    // around center).
    vec2 elementCenter = uOffset + uSize * 0.5;
    vec2 centeredScreen = screenCoord - elementCenter;
    vec2 layerScale = max(uLayerScale, vec2(1e-4));
    vec2 centeredOrig = centeredScreen / layerScale;
    vec2 centeredOrigRot = rotateBy(centeredOrig, -uElementRotation);

    // SDF for shape clip \u2014 faithful to InnerShadowModifier.kt's final
    // clipOutline call before drawLayer. The original uses Skia's
    // geometric clip with smooth AA (sub-pixel transition).
    // We replicate with smoothstep \u2014 NO hard discard.
    vec2 origHalfSize = uOriginalSize * 0.5;
    float sd = sdShape(centeredOrigRot, origHalfSize, uOriginalCornerRadius);

    // Smooth clipAlpha: 1.0 fully inside (sd \u2264 0), smoothly fading
    // across the boundary (sd 0\u21921.5), 0.0 outside (sd \u2265 1.5).
    // The 1.5px transition width matches Skia's clipOutline AA behavior
    // \u2014 pixels at the exact boundary (sd=0) retain FULL intensity, with
    // a gentle fade that removes outward blur leakage smoothly.
    // This is NOT a hard discard \u2014 it's a smooth clip that matches the
    // original's geometric clipOutline exactly.
    float clipAlpha = 1.0 - smoothstep(0.0, 1.5, sd);

    // Skip truly invisible pixels for performance (not a visual clip)
    if (clipAlpha < 0.004) discard;

    // Map to mask UV: original-space coord \u2192 mask texture UV.
    vec2 maskTexCoord = centeredOrigRot + origHalfSize;  // 0..origSize (element-local)
    vec2 maskUv = (maskTexCoord + uMaskOffset) / uMaskSize;

    // Sample the mask texture. CLAMP_TO_EDGE wrapping handles UV values
    // slightly outside (0..1) gracefully \u2014 returns transparent at edges.
    float mask = texture2D(uInnerShadowMask, maskUv).a;

    // Skip truly invisible pixels for performance (not a visual clip)
    // Threshold is very low to avoid cutting off faint but visible shadow edges.
    if (mask < 0.003) discard;

    // Premultiplied SrcOver composite: shadowColor \xD7 mask \xD7 shadowAlpha \xD7 clipAlpha.
    // clipAlpha provides smooth shape-boundary transition (faithful to original's
    // clipOutline AA). Output is premultiplied (rgb = color * alpha).
    // Renderer uses gl.blendFunc(ONE, ONE_MINUS_SRC_ALPHA) \u2014 premultiplied SrcOver.
    float a = mask * uInnerShadowAlpha * clipAlpha;
    gl_FragColor = vec4(uInnerShadowColor * a, a);
}
`;var D=`
attribute vec2 aPos;
void main() {
    gl_Position = vec4(aPos, 0.0, 1.0);
}
`,Qe=`
precision highp float;

uniform sampler2D uBackdrop;
uniform vec2 uCanvasSize;
uniform vec2 uWallpaperSize;

${he}

void main() {
    vec2 screenCoord = vec2(gl_FragCoord.x, uCanvasSize.y - gl_FragCoord.y);
    vec2 uv = coverUv(screenCoord);
    gl_FragColor = texture2D(uBackdrop, uv);
}
`,Je=`
precision highp float;

uniform sampler2D uTexture;
uniform vec2 uCanvasSize;

void main() {
    vec2 uv = vec2(gl_FragCoord.x / uCanvasSize.x, gl_FragCoord.y / uCanvasSize.y);
    gl_FragColor = texture2D(uTexture, uv);
}
`,et=`
precision highp float;

uniform vec4 uColor;

void main() {
    gl_FragColor = uColor;
}
`,tt=`
precision highp float;

uniform sampler2D uTexture;
uniform vec2 uTexSize;
uniform float uBrightness;
uniform float uContrast;
uniform float uSaturation;

void main() {
    vec2 uv = vec2(gl_FragCoord.x / uTexSize.x, gl_FragCoord.y / uTexSize.y);
    vec4 c = texture2D(uTexture, uv);
    float invSat = 1.0 - uSaturation;
    float r = 0.213 * invSat;
    float g = 0.715 * invSat;
    float b = 0.072 * invSat;
    float t = (0.5 - uContrast * 0.5 + uBrightness);
    float cs = uContrast * uSaturation;
    float cr = uContrast * r;
    float cg = uContrast * g;
    float cb = uContrast * b;
    vec3 outc;
    outc.r = (cr + cs) * c.r + cg * c.g + cb * c.b + t;
    outc.g = cr * c.r + (cg + cs) * c.g + cb * c.b + t;
    outc.b = cr * c.r + cg * c.g + (cb + cs) * c.b + t;
    gl_FragColor = vec4(outc, c.a);
}
`,rt=`
precision highp float;

uniform sampler2D uTexture;
uniform vec2 uCanvasSize;
uniform vec3 uTintColor;   // rgb 0..1 (accentColor)

// ColorFilter.tint(color, blendMode = BlendMode.SrcIn):
//   result.rgb = src.rgb (the tint color)
//   result.a   = dst.a * src.a
// SrcIn replaces the destination's RGB with the tint color while
// preserving its alpha \u2014 opaque content becomes solid tint, transparent
// areas stay transparent. This matches Compose's ColorFilter.tint default.
void main() {
    vec2 uv = vec2(gl_FragCoord.x / uCanvasSize.x, gl_FragCoord.y / uCanvasSize.y);
    vec4 src = texture2D(uTexture, uv);
    gl_FragColor = vec4(uTintColor, src.a);
}
`;var it=`
precision highp float;

uniform sampler2D uTexture;
uniform vec2 uCanvasSize;
uniform vec2 uOffset;   // foreground texture top-left in canvas px (top-left origin) \u2014 SCALED rect
uniform vec2 uSize;     // foreground texture size in canvas px \u2014 SCALED
uniform vec4 uCornerRadii;  // capsule radii (topLeft, topRight, bottomRight, bottomLeft) in px \u2014 SCALED
uniform float uAlpha;   // global alpha multiplier (used for press fade)
// --- ORIGINAL-SPACE SDF clip (faithful to graphicsLayer { scaleX, scaleY }) ---
// The original wraps everything (text included) in a graphicsLayer clipped to
// the capsule shape, THEN scales the layer. So the clip shape is the ORIGINAL
// capsule, not the stretched one. We compute the clip SDF in original space so
// a stretched button keeps correct capsule clipping (no corner bleed). The
// texture UV still uses the scaled rect (uOffset/uSize) since the foreground
// texture is rendered at the element's scaled on-screen size.
uniform vec2  uOriginalSize;        // element size in px (ORIGINAL, unscaled)
uniform float uOriginalCornerRadius; // corner radius in px (ORIGINAL, unscaled)
uniform vec2  uLayerScale;          // (scaleX, scaleY) from graphicsLayer

${U}

void main() {
    // gl_FragCoord is bottom-left origin in WebGL framebuffer space.
    // Flip Y to get top-left origin (matching CSS / 2D canvas convention).
    vec2 screenCoord = vec2(gl_FragCoord.x, uCanvasSize.y - gl_FragCoord.y);
    vec2 localCoord = screenCoord - uOffset;
    // Scissor to the (scaled) foreground rectangle.
    if (localCoord.x < 0.0 || localCoord.x > uSize.x ||
        localCoord.y < 0.0 || localCoord.y > uSize.y) {
        discard;
    }

    // --- Capsule clip in ORIGINAL space (faithful to graphicsLayer clip) ---
    // elementCenter is the SAME for scaled and original rects (scaling is
    // around the center). Map screen coord \u2192 original space for the SDF so
    // the clip shape is the original capsule, not the stretched one.
    vec2 elementCenter = uOffset + uSize * 0.5;
    vec2 centeredScreen = screenCoord - elementCenter;
    vec2 layerScale = max(uLayerScale, vec2(1e-4));
    vec2 centeredOrig = centeredScreen / layerScale;
    vec2 origHalfSize = uOriginalSize * 0.5;
    float clipAlpha;
    if (uUseContinuousSdf > 0.5) {
        float mask = sampleClipMask(centeredOrig, origHalfSize, uOriginalCornerRadius);
        if (mask < 0.01) discard;
        clipAlpha = mask;
    } else {
        float sdClip = sdClipShape(centeredOrig, origHalfSize, uOriginalCornerRadius);
        if (sdClip > 0.5) discard;
        clipAlpha = 1.0 - smoothstep(-0.5, 0.5, sdClip);
    }

    // The texture is uploaded from a 2D canvas with UNPACK_FLIP_Y_WEBGL=false,
    // so texture row 0 (= v=0) is the TOP row of the source canvas. Combined
    // with the Y flip above, uv.y=0 corresponds to the top of the button rect
    // (which is what we want \u2014 text drawn at the middle of the source canvas
    // appears at the middle of the button).
    //
    // The texture is uploaded with UNPACK_PREMULTIPLY_ALPHA_WEBGL=true, so
    // c is already in premultiplied form (c.rgb <= c.a). We scale both
    // rgb and a by uAlpha * clipAlpha and output premultiplied rgba, paired
    // with blendFunc(ONE, ONE_MINUS_SRC_ALPHA) at the draw site.
    vec2 uv = localCoord / uSize;
    vec4 c = texture2D(uTexture, uv);
    float a = c.a * uAlpha * clipAlpha;
    gl_FragColor = vec4(c.rgb * uAlpha * clipAlpha, a);
}
`,ot=`
precision highp float;

uniform vec2  uCanvasSize;
uniform vec2  uOffset;
uniform vec2  uSize;
uniform vec4  uCornerRadii;
uniform vec4  uColor;       // rgba (premultiplied not required; alpha used as-is)

${U}

void main() {
    vec2 screenCoord = vec2(gl_FragCoord.x, uCanvasSize.y - gl_FragCoord.y);
    vec2 localCoord = screenCoord - uOffset;
    vec2 halfSize = uSize * 0.5;
    vec2 centeredCoord = localCoord - halfSize;

    float radius = radiusAt(centeredCoord, uCornerRadii);
    float alpha;
    if (uUseContinuousSdf > 0.5) {
        float mask = sampleClipMask(centeredCoord, halfSize, radius);
        if (mask < 0.01) discard;
        alpha = mask;
    } else {
        float sdClip = sdClipShape(centeredCoord, halfSize, radius);
        if (sdClip > 0.5) discard;
        alpha = 1.0 - smoothstep(-0.5, 0.5, sdClip);
    }
    gl_FragColor = vec4(uColor.rgb, uColor.a * alpha);
}
`,at=`
precision highp float;

uniform sampler2D uBackdrop;
uniform vec2  uCanvasSize;
uniform vec2  uWallpaperSize;
uniform vec2  uOffset;          // band top-left in canvas px (top-left origin)
uniform vec2  uSize;            // band size in canvas px
uniform float uBlurRadius;      // px in canvas space
uniform vec4  uTintColor;       // rgba
uniform float uTintIntensity;   // 0..1

${he}

// 9-tap poisson disc \u2014 offsets are inlined because GLSL ES 1.00 (WebGL 1)
// does not support array constructors or const-array initializers.
// The offsets are normalized (unit disc), multiplied by step (radius in UV).
vec4 sampleBackdrop(vec2 canvasPx, float radius) {
    vec2 uvScale = canvasPxToUvScale();
    vec2 uv = coverUv(canvasPx);
    vec2 st = radius * uvScale;
    vec4 sum = vec4(0.0);
    sum += texture2D(uBackdrop, uv + vec2( 0.0000,  0.0000) * st);
    sum += texture2D(uBackdrop, uv + vec2( 0.5000,  0.0000) * st);
    sum += texture2D(uBackdrop, uv + vec2(-0.5000,  0.0000) * st);
    sum += texture2D(uBackdrop, uv + vec2( 0.0000,  0.5000) * st);
    sum += texture2D(uBackdrop, uv + vec2( 0.0000, -0.5000) * st);
    sum += texture2D(uBackdrop, uv + vec2( 0.3536,  0.3536) * st);
    sum += texture2D(uBackdrop, uv + vec2(-0.3536,  0.3536) * st);
    sum += texture2D(uBackdrop, uv + vec2( 0.3536, -0.3536) * st);
    sum += texture2D(uBackdrop, uv + vec2(-0.3536, -0.3536) * st);
    return sum / 9.0;
}

void main() {
    vec2 screenCoord = vec2(gl_FragCoord.x, uCanvasSize.y - gl_FragCoord.y);
    vec2 localCoord = screenCoord - uOffset;
    // Outside the band \u2014 nothing to draw.
    if (localCoord.x < 0.0 || localCoord.x > uSize.x ||
        localCoord.y < 0.0 || localCoord.y > uSize.y) {
        discard;
    }

    // Alpha mask: opaque at top (coord.y = size.y, i.e. BOTTOM in top-left
    // origin = size.y in AGSL coord), transparent at bottom. Matches the
    // Kotlin smoothstep(size.y, size.y * 0.5, coord.y).
    float a = smoothstep(uSize.y, uSize.y * 0.5, localCoord.y);

    // Sample the (cover-fit) backdrop at the canvas pixel, blurred.
    vec4 blurred = sampleBackdrop(screenCoord, uBlurRadius);

    // Faithful to AlphaMask shader: mix(content * blurAlpha, tint * tintAlpha, tintIntensity)
    // This is PREMULTIPLIED (rgb already scaled by alpha). The renderer uses
    // premultiplied alpha blending for the progressive blur pass, so we output
    // premultiplied rgb with the mask alpha.
    vec3 premulRgb = mix(blurred.rgb * a, uTintColor.rgb * a, uTintIntensity);
    gl_FragColor = vec4(premulRgb, a);
}
`;function qt(i){if(i<=1)return[{offset:0,weight:1}];let e=[],t=Math.floor(i/2),a=3,o=0;for(let r=0;r<i;r++){let s=(i%2===1?r-t:r-t+.5)/t*a,n=Math.exp(-.5*s*s);e.push({offset:s,weight:n}),o+=n}if(o>0)for(let r of e)r.weight/=o;return e}function Fe(i,e){let t=qt(i),o=e==="horizontal"?"vec2(1.0, 0.0)":"vec2(0.0, 1.0)",r="";if(t.length===1)r=`    gl_FragColor = texture2D(uTexture, uv);
`;else{r=`    vec3 rgbSum = vec3(0.0);
    float rgbW = 0.0;
`;for(let l of t){let s=l.offset.toFixed(6),n=l.weight.toFixed(8);r+=`    { vec4 s = texture2D(uTexture, uv + ${o} * ${s} * pxToUv); float aw = s.a * ${n}; rgbSum += s.rgb * aw; rgbW += aw; }
`}r+=`    float origA = texture2D(uTexture, uv).a;
    gl_FragColor = vec4(rgbW > 0.001 ? rgbSum / rgbW : vec3(0.0), origA);
`}return`
precision highp float;

uniform sampler2D uTexture;
uniform vec2 uTexSize;
uniform float uRadius;

void main() {
    vec2 uv = vec2(gl_FragCoord.x / uTexSize.x, gl_FragCoord.y / uTexSize.y);
    if (uRadius < 0.5) {
        gl_FragColor = texture2D(uTexture, uv);
        return;
    }
    vec2 pxToUv = vec2(uRadius / uTexSize.x, uRadius / uTexSize.y);
${r}}
`}function st(i){if(i<.5)return 1;let e=i*.57735+.5,t=2*Math.ceil(3*e)+1;return Math.min(33,Math.max(1,t))}function Kt(i){if(i<=1)return[{offset:0,weight:1}];let e=[],t=Math.floor(i/2),a=0;for(let o=0;o<i;o++){let r=o-t,l=Math.exp(-.5*r*r);e.push({offset:r,weight:l}),a+=l}if(a>0)for(let o of e)o.weight/=a;return e}function _e(i,e){let t=Kt(i),o=e==="horizontal"?"vec2(1.0, 0.0)":"vec2(0.0, 1.0)",r="";if(t.length===1)r=`    gl_FragColor = texture2D(uTexture, uv);
`;else{r=`    float aSum = 0.0;
`;for(let l of t){let s=l.offset.toFixed(6),n=l.weight.toFixed(8);r+=`    aSum += texture2D(uTexture, uv + ${o} * ${s} * pxToUv).a * ${n};
`}r+=`    gl_FragColor = vec4(0.0, 0.0, 0.0, aSum);
`}return`
precision highp float;

uniform sampler2D uTexture;
uniform vec2 uTexSize;
uniform float uRadius;  // Gaussian sigma in pixels (Android BlurMaskFilter semantics)

void main() {
    vec2 uv = vec2(gl_FragCoord.x / uTexSize.x, gl_FragCoord.y / uTexSize.y);
    if (uRadius < 0.01) {
        gl_FragColor = texture2D(uTexture, uv);
        return;
    }
    // pxToUv converts a pixel offset to a UV offset. offset (in \u03C3 units) *
    // sigma_px = pixel offset; / uTexSize = UV offset.
    vec2 pxToUv = vec2(uRadius / uTexSize.x, uRadius / uTexSize.y);
${r}}
`}function nt(i){if(i<.01)return 1;let e=2*Math.ceil(3*i)+1;return Math.min(33,Math.max(3,e))}function le(i,e,t){let a=i.createShader(e);if(i.shaderSource(a,t),i.compileShader(a),!i.getShaderParameter(a,i.COMPILE_STATUS)){let o=i.getShaderInfoLog(a);throw i.deleteShader(a),new Error("Shader compile error: "+o)}return a}function N(i,e,t){let a=le(i,i.VERTEX_SHADER,e),o=le(i,i.FRAGMENT_SHADER,t),r=i.createProgram();if(i.attachShader(r,a),i.attachShader(r,o),i.linkProgram(r),!i.getProgramParameter(r,i.LINK_STATUS)){let l=i.getProgramInfoLog(r);throw i.deleteProgram(r),new Error("Program link error: "+l)}return r}function lt(i,e,t){let a=e.split(/\s+/),o=[],r="";for(let l of a){let s=r?r+" "+l:l;i.measureText(s).width<=t||!r?r=s:(o.push(r),r=l)}return r&&o.push(r),o}function Re(i){if(i<=0)return 0;if(i>=1)return 1;let e=.42,t=0,a=1,o=1,r=i;for(let l=0;l<8;l++){let s=3*(1-r)*(1-r)*r*e+3*(1-r)*r*r*a+r*r*r,n=3*(1-r)*(1-r)*e+6*(1-r)*r*(a-e)+3*r*r*(1-a);if(Math.abs(s-i)<.001||Math.abs(n)<1e-6)break;r-=(s-i)/n,r=Math.max(0,Math.min(1,r))}return 3*(1-r)*(1-r)*r*t+3*(1-r)*r*r*o+r*r*r}function Ct(i,e){return["is",i,e.useG2?"g2":"rr",e.w.toFixed(3),e.h.toFixed(3),e.radius.toFixed(3),e.offsetX.toFixed(3),e.offsetY.toFixed(3),e.blurSigma.toFixed(3),e.margin,Math.ceil(e.w+2*e.margin),Math.ceil(e.h+2*e.margin),`ss${e.supersample}`].join(":")}function Rt(i,e,t,a,o){let r=i.get(t);if(r)return r;let l=e.createTexture();if(!l)throw new Error("WebGL texture allocation failed");if(r={tex:l,w:a,h:o,ready:!1},i.set(t,r),i.size>32){let s=i.keys().next().value;if(s&&s!==t){let n=i.get(s);n&&e.deleteTexture(n.tex),i.delete(s)}}return r}function Et(i,e,t){i.bindTexture(i.TEXTURE_2D,e.tex),i.pixelStorei(i.UNPACK_FLIP_Y_WEBGL,!1),i.texImage2D(i.TEXTURE_2D,0,i.RGBA,i.RGBA,i.UNSIGNED_BYTE,t.canvas),i.texParameteri(i.TEXTURE_2D,i.TEXTURE_MIN_FILTER,i.LINEAR),i.texParameteri(i.TEXTURE_2D,i.TEXTURE_MAG_FILTER,i.LINEAR),i.texParameteri(i.TEXTURE_2D,i.TEXTURE_WRAP_S,i.CLAMP_TO_EDGE),i.texParameteri(i.TEXTURE_2D,i.TEXTURE_WRAP_T,i.CLAMP_TO_EDGE),e.ready=!0}function At(i,e){for(let t of e.values())i.deleteTexture(t.tex);e.clear()}var yt={createFBO(i,e){let t=this.gl,a=t.createTexture();t.bindTexture(t.TEXTURE_2D,a),t.texImage2D(t.TEXTURE_2D,0,t.RGBA,i,e,0,t.RGBA,t.UNSIGNED_BYTE,null),t.texParameteri(t.TEXTURE_2D,t.TEXTURE_MIN_FILTER,t.LINEAR),t.texParameteri(t.TEXTURE_2D,t.TEXTURE_MAG_FILTER,t.LINEAR),t.texParameteri(t.TEXTURE_2D,t.TEXTURE_WRAP_S,t.CLAMP_TO_EDGE),t.texParameteri(t.TEXTURE_2D,t.TEXTURE_WRAP_T,t.CLAMP_TO_EDGE);let o=t.createFramebuffer();return t.bindFramebuffer(t.FRAMEBUFFER,o),t.framebufferTexture2D(t.FRAMEBUFFER,t.COLOR_ATTACHMENT0,t.TEXTURE_2D,a,0),t.bindFramebuffer(t.FRAMEBUFFER,null),{fb:o,tex:a}},resizeFBOs(i,e){if(this.fboW===i&&this.fboH===e&&this.fboA&&this.fboB)return;let t=this.gl;this.fboA&&t.deleteFramebuffer(this.fboA),this.fboATex&&t.deleteTexture(this.fboATex),this.fboB&&t.deleteFramebuffer(this.fboB),this.fboBTex&&t.deleteTexture(this.fboBTex);let a=this.createFBO(i,e),o=this.createFBO(i,e);this.fboA=a.fb,this.fboATex=a.tex,this.fboB=o.fb,this.fboBTex=o.tex,this.tabsBackdropFbo&&t.deleteFramebuffer(this.tabsBackdropFbo),this.tabsBackdropTex&&t.deleteTexture(this.tabsBackdropTex);let r=this.createFBO(i,e);this.tabsBackdropFbo=r.fb,this.tabsBackdropTex=r.tex,this.tabsBackdropDirty=!0,this.gpElementFbo&&t.deleteFramebuffer(this.gpElementFbo),this.gpElementTex&&t.deleteTexture(this.gpElementTex),this.blurFboA&&t.deleteFramebuffer(this.blurFboA),this.blurFboATex&&t.deleteTexture(this.blurFboATex),this.blurFboB&&t.deleteFramebuffer(this.blurFboB),this.blurFboBTex&&t.deleteTexture(this.blurFboBTex);let l=this.createFBO(i,e),s=this.createFBO(i,e),n=this.createFBO(i,e);this.gpElementFbo=l.fb,this.gpElementTex=l.tex,this.blurFboA=s.fb,this.blurFboATex=s.tex,this.blurFboB=n.fb,this.blurFboBTex=n.tex,this.highlightMaskFbo&&t.deleteFramebuffer(this.highlightMaskFbo),this.highlightMaskTex&&t.deleteTexture(this.highlightMaskTex);let h=this.createFBO(i,e);this.highlightMaskFbo=h.fb,this.highlightMaskTex=h.tex,this.dialogBackdropFbo&&t.deleteFramebuffer(this.dialogBackdropFbo),this.dialogBackdropTex&&t.deleteTexture(this.dialogBackdropTex);let c=this.createFBO(i,e);this.dialogBackdropFbo=c.fb,this.dialogBackdropTex=c.tex,this.dialogBackdropKey=null,this.fboW=i,this.fboH=e},bindFBO(i){let e=this.gl;e.bindFramebuffer(e.FRAMEBUFFER,i),e.viewport(0,0,this.fboW,this.fboH)},drawCopy(i){let e=this.gl;e.useProgram(this.copyProgram),e.bindBuffer(e.ARRAY_BUFFER,this.quadBuffer),e.enableVertexAttribArray(this.aPosLocCp),e.vertexAttribPointer(this.aPosLocCp,2,e.FLOAT,!1,0,0),e.activeTexture(e.TEXTURE0),e.bindTexture(e.TEXTURE_2D,i),e.uniform1i(this.uCp.uTexture,0),e.uniform2f(this.uCp.uCanvasSize,this.fboW,this.fboH),e.disable(e.BLEND),e.drawArrays(e.TRIANGLES,0,6)},drawSolidFill(i,e,t,a){let o=this.gl;o.useProgram(this.solidFillProgram),o.bindBuffer(o.ARRAY_BUFFER,this.quadBuffer),o.enableVertexAttribArray(this.aPosLocSf),o.vertexAttribPointer(this.aPosLocSf,2,o.FLOAT,!1,0,0),o.uniform4f(this.uSf.uColor,i,e,t,a),o.disable(o.BLEND),o.drawArrays(o.TRIANGLES,0,6)},drawColorControls(i,e,t,a){let o=this.gl;o.useProgram(this.colorControlsProgram),o.bindBuffer(o.ARRAY_BUFFER,this.quadBuffer),o.enableVertexAttribArray(this.aPosLocCc),o.vertexAttribPointer(this.aPosLocCc,2,o.FLOAT,!1,0,0),o.activeTexture(o.TEXTURE0),o.bindTexture(o.TEXTURE_2D,i),o.uniform1i(this.uCc.uTexture,0),o.uniform2f(this.uCc.uTexSize,this.fboW,this.fboH),o.uniform1f(this.uCc.uBrightness,e),o.uniform1f(this.uCc.uContrast,t),o.uniform1f(this.uCc.uSaturation,a),o.disable(o.BLEND),o.drawArrays(o.TRIANGLES,0,6)}};var j=1.4142135623730951,$t=.7853981633974483,ee=.7071067811865476;function ut(i,e,t,a){let o=(3*t/i-e*e/(i*i))/3,r=(2*e*e*e/(i*i*i)-9*e*t/(i*i)+27*a/i)/27,l=r*r/4+o*o*o/27,s=Math.sqrt(l);return Math.cbrt(-r/2+s)+Math.cbrt(-r/2-s)-e/(3*i)}function Zt(i,e,t){let a=-i/2,o=-t,r=t*i/2-e*e/8,l=(3*o-a*a)/3,s=(2*a*a*a-9*a*o+27*r)/27,n=Math.sqrt(-l*l*l/27),h=Math.acos(-s/(2*n)),u=2*Math.sqrt(-l/3)*Math.cos(h/3)-a/3,d=Math.sqrt(2*u-i);return(d-Math.sqrt(d*d-4*(u+e/(2*d))))/2}var ct=class{constructor(e=2/3,t=.5){this.extendedFraction=e,this.arcFraction=t,this.theta=(1-t)*$t,this.cos=Math.cos(this.theta),this.sin=Math.sin(this.theta),this.cot=1/Math.tan(this.theta),this.cos2=this.cos*this.cos,this.sin2=this.sin*this.sin,this.cos3=this.cos2*this.cos,this.sin3=this.sin2*this.sin;let a=this.cos,o=this.sin,r=this.cot,l=this.cos2,s=this.sin2,n=this.cos3,h=this.sin3;this.k0=27*(j-6*a+6*j*l-4*n)*r+2*o*(-9+2*(j-2*o)*h+2*j*a*(9+s)-2*l*(9+2*s)),this.k1=-81*(-2+j+4*(-1+j)*a+2*(-2+j)*l)*r-4*o*(-9+9*j+j*h+(-2+j)*a*(9+s)),this.k2=9*(9*(-4+3*j+(-6+4*j)*a)*r+(-6+4*j)*o),this.k3=27*(10-7*j)*r}buildEvenCornerBezierPoints(e){let t=this.extendedFraction*e,a=ut(this.k3,this.k2,this.k1+8*-t*this.sin3*this.sin,this.k0),o=ee+(-ee+this.sin)/a,r=1-ee+(ee-this.cos)/a,l=o-r*this.cot,s=l-1.5*a*r*r/this.sin3,n=-t,h=1-r,c=1-o,u=1-l,d=1-s,f=1-n,g=1.5*a,p=this.cos2-this.sin2,C=h-o,R=c-r,M=-(this.cos*R-this.sin*C),b=(-p+Math.sqrt(p*p-4*g*M))/(2*g),I=o+b*this.cos,y=r+b*this.sin,S=h-b*this.sin,v=c-b*this.cos;return[n,0,s,0,l,0,o,r,I,y,S,v,h,c,1,u,1,d,1,f]}buildUnevenCornerBezierPoints(e,t){let a=this.extendedFraction*e,o=this.extendedFraction*t,r=ut(this.k3,this.k2,this.k1+8*-a*this.sin3*this.sin,this.k0),l=ut(this.k3,this.k2,this.k1+8*-o*this.sin3*this.sin,this.k0),s=ee+(-ee+this.sin)/r,n=1-ee+(ee-this.cos)/r,h=s-n*this.cot,c=h-1.5*r*n*n/this.sin3,u=-a,d=ee+(-ee+this.sin)/l,f=1-ee+(ee-this.cos)/l,g=d-f*this.cot,p=g-1.5*l*f*f/this.sin3,C=-o,R=1-f,M=1-d,b=1-g,I=1-p,y=1-C,S=1.5*r,v=1.5*l,O=this.cos2-this.sin2,H=R-s,_=M-n,T=-(this.cos*_-this.sin*H),E=this.sin*_-this.cos*H,m=2*(E/v),re=O*O*O/(S*v*v),K=(S*E*E+T*O*O)/(S*v*v),$=Zt(m,re,K),W=(-E-v*$*$)/O,x=s+W*this.cos,P=n+W*this.sin,L=R-$*this.sin,A=M-$*this.cos;return[u,0,c,0,h,0,s,n,x,P,L,A,R,M,1,b,1,I,1,y]}getCornerBezierPoints(e,t){let a=e===0?0:e===1?1:-1,o=t===0?0:t===1?1:-1;return a>=0&&o>=0?a===0&&o===0?this.buildEvenCornerBezierPoints(0):a===1&&o===1?this.buildEvenCornerBezierPoints(1):this.buildUnevenCornerBezierPoints(a===1?1:0,o===1?1:0):this.buildUnevenCornerBezierPoints(Math.max(0,Math.min(1,e)),Math.max(0,Math.min(1,t)))}};function be(i,e,t,a){let o=new ct,r=a,l=Math.max(0,Math.min(1,(e*.5-r)/r)),s=Math.max(0,Math.min(1,(t*.5-r)/r)),n=o.getCornerBezierPoints(l,s);if(n.length<20)return new Path2D;let h=new Path2D,c=e-r,u=0;return h.moveTo(c+n[0]*r,u+n[1]*r),h.bezierCurveTo(c+n[2]*r,u+n[3]*r,c+n[4]*r,u+n[5]*r,c+n[6]*r,u+n[7]*r),h.bezierCurveTo(c+n[8]*r,u+n[9]*r,c+n[10]*r,u+n[11]*r,c+n[12]*r,u+n[13]*r),h.bezierCurveTo(c+n[14]*r,u+n[15]*r,c+n[16]*r,u+n[17]*r,c+n[18]*r,u+n[19]*r),c=e-r,u=t,h.lineTo(c+n[18]*r,u-n[19]*r),h.bezierCurveTo(c+n[16]*r,u-n[17]*r,c+n[14]*r,u-n[15]*r,c+n[12]*r,u-n[13]*r),h.bezierCurveTo(c+n[10]*r,u-n[11]*r,c+n[8]*r,u-n[9]*r,c+n[6]*r,u-n[7]*r),h.bezierCurveTo(c+n[4]*r,u-n[5]*r,c+n[2]*r,u-n[3]*r,c+n[0]*r,u-n[1]*r),c=r,u=t,h.lineTo(c-n[0]*r,u-n[1]*r),h.bezierCurveTo(c-n[2]*r,u-n[3]*r,c-n[4]*r,u-n[5]*r,c-n[6]*r,u-n[7]*r),h.bezierCurveTo(c-n[8]*r,u-n[9]*r,c-n[10]*r,u-n[11]*r,c-n[12]*r,u-n[13]*r),h.bezierCurveTo(c-n[14]*r,u-n[15]*r,c-n[16]*r,u-n[17]*r,c-n[18]*r,u-n[19]*r),c=r,u=0,h.lineTo(c-n[18]*r,u+n[19]*r),h.bezierCurveTo(c-n[16]*r,u+n[17]*r,c-n[14]*r,u+n[15]*r,c-n[12]*r,u+n[13]*r),h.bezierCurveTo(c-n[10]*r,u+n[11]*r,c-n[8]*r,u+n[9]*r,c-n[6]*r,u+n[7]*r),h.bezierCurveTo(c-n[4]*r,u+n[5]*r,c-n[2]*r,u+n[3]*r,c-n[0]*r,u+n[1]*r),h.closePath(),h}var Lt=new Map;function kt(i,e,t,a=1){let o=Math.min(512,Math.max(128,Math.round(Math.max(i,e)*a))),r=`${i},${e},${t},${o}`,l=Lt.get(r);if(l)return{tex:l.tex,texSize:o};let s=Math.max(i,e),n=i/s,h=e/s,c=document.createElement("canvas");c.width=o,c.height=o;let u=c.getContext("2d");u.clearRect(0,0,o,o);let d=4,f=(o-2*d)*n,g=(o-2*d)*h,p=(o-f)/2,C=(o-g)/2,R=f/i,M=t*R,b=be(u,f,g,M);u.fillStyle="white",u.translate(p,C),u.fill(b),u.translate(-p,-C);let I=u.getImageData(0,0,o,o),y=new Uint8Array(o*o);for(let T=0;T<o*o;T++)y[T]=I.data[T*4+3];let S=new Float32Array(o*o),v=new Float32Array(o*o),O=1e10;for(let T=0;T<o*o;T++)y[T]>128?(S[T]=0,v[T]=O):(S[T]=O,v[T]=0);for(let T=0;T<o;T++)for(let E=0;E<o;E++){let m=T*o+E;E>0&&T>1&&(S[m]=Math.min(S[m],S[m-o-1-o]+11),v[m]=Math.min(v[m],v[m-o-1-o]+11)),E>0&&(S[m]=Math.min(S[m],S[m-1]+5),v[m]=Math.min(v[m],v[m-1]+5)),E>0&&T>0&&(S[m]=Math.min(S[m],S[m-o-1]+7),v[m]=Math.min(v[m],v[m-o-1]+7)),T>0&&(S[m]=Math.min(S[m],S[m-o]+5),v[m]=Math.min(v[m],v[m-o]+5)),E<o-1&&T>0&&(S[m]=Math.min(S[m],S[m-o+1]+7),v[m]=Math.min(v[m],v[m-o+1]+7)),E<o-2&&T>0&&(S[m]=Math.min(S[m],S[m-o+2]+11),v[m]=Math.min(v[m],v[m-o+2]+11))}for(let T=o-1;T>=0;T--)for(let E=o-1;E>=0;E--){let m=T*o+E;E<o-1&&T<o-2&&(S[m]=Math.min(S[m],S[m+o+1+o]+11),v[m]=Math.min(v[m],v[m+o+1+o]+11)),E<o-1&&(S[m]=Math.min(S[m],S[m+1]+5),v[m]=Math.min(v[m],v[m+1]+5)),E<o-1&&T<o-1&&(S[m]=Math.min(S[m],S[m+o+1]+7),v[m]=Math.min(v[m],v[m+o+1]+7)),T<o-1&&(S[m]=Math.min(S[m],S[m+o]+5),v[m]=Math.min(v[m],v[m+o]+5)),E>0&&T<o-1&&(S[m]=Math.min(S[m],S[m+o-1]+7),v[m]=Math.min(v[m],v[m+o-1]+7)),E>1&&T<o-1&&(S[m]=Math.min(S[m],S[m+o-2]+11),v[m]=Math.min(v[m],v[m+o-2]+11))}let H=M,_=new Uint8Array(o*o*4);for(let T=0;T<o*o;T++){_[T*4]=y[T];let E=(S[T]-v[T])/5,m=Math.max(-1,Math.min(1,E/H));_[T*4+1]=Math.round((m*.5+.5)*255),_[T*4+2]=0,_[T*4+3]=255}return Lt.set(r,{tex:_,texSize:o}),{tex:_,texSize:o}}var Pt={async loadWallpaper(i){let e=new Image;e.crossOrigin="anonymous",await new Promise((s,n)=>{e.onload=()=>s(),e.onerror=()=>n(new Error("Failed to load wallpaper: "+i)),e.src=i});let t=this.gl;this.wallpaperTexture&&t.deleteTexture(this.wallpaperTexture);let a=t.createTexture();t.bindTexture(t.TEXTURE_2D,a),t.pixelStorei(t.UNPACK_FLIP_Y_WEBGL,!1),t.texImage2D(t.TEXTURE_2D,0,t.RGBA,t.RGBA,t.UNSIGNED_BYTE,e);let o=e.naturalWidth,r=e.naturalHeight;(o&o-1)===0&&(r&r-1)===0?(t.generateMipmap(t.TEXTURE_2D),t.texParameteri(t.TEXTURE_2D,t.TEXTURE_MIN_FILTER,t.LINEAR_MIPMAP_LINEAR)):t.texParameteri(t.TEXTURE_2D,t.TEXTURE_MIN_FILTER,t.LINEAR),t.texParameteri(t.TEXTURE_2D,t.TEXTURE_MAG_FILTER,t.LINEAR),t.texParameteri(t.TEXTURE_2D,t.TEXTURE_WRAP_S,t.CLAMP_TO_EDGE),t.texParameteri(t.TEXTURE_2D,t.TEXTURE_WRAP_T,t.CLAMP_TO_EDGE),this.wallpaperTexture=a,this.wallpaperSize=[o||1,r||1],this.wallpaperReady=!0,this.requestRender()},async loadSdfTexture(i){let e=new Image;e.crossOrigin="anonymous",await new Promise((o,r)=>{e.onload=()=>o(),e.onerror=()=>r(new Error("Failed to load SDF texture: "+i)),e.src=i});let t=this.gl;this.sdfTexture&&t.deleteTexture(this.sdfTexture);let a=t.createTexture();t.bindTexture(t.TEXTURE_2D,a),t.pixelStorei(t.UNPACK_FLIP_Y_WEBGL,!1),t.texImage2D(t.TEXTURE_2D,0,t.RGBA,t.RGBA,t.UNSIGNED_BYTE,e),t.texParameteri(t.TEXTURE_2D,t.TEXTURE_MIN_FILTER,t.LINEAR),t.texParameteri(t.TEXTURE_2D,t.TEXTURE_MAG_FILTER,t.LINEAR),t.texParameteri(t.TEXTURE_2D,t.TEXTURE_WRAP_S,t.CLAMP_TO_EDGE),t.texParameteri(t.TEXTURE_2D,t.TEXTURE_WRAP_T,t.CLAMP_TO_EDGE),this.sdfTexture=a,this.sdfTextureSize=[e.naturalWidth||1,e.naturalHeight||1],this.sdfTextureReady=!0,this.requestRender()},loadContinuousSdf(i,e,t){let a=`${i},${e},${t},${this.dpr}`,o=this.continuousSdfPool.get(a);if(!o){let{tex:r,texSize:l}=kt(i,e,t,this.dpr),s=this.gl,n=s.createTexture();if(s.bindTexture(s.TEXTURE_2D,n),s.pixelStorei(s.UNPACK_FLIP_Y_WEBGL,!0),s.texImage2D(s.TEXTURE_2D,0,s.RGBA,l,l,0,s.RGBA,s.UNSIGNED_BYTE,r),s.texParameteri(s.TEXTURE_2D,s.TEXTURE_MIN_FILTER,s.LINEAR),s.texParameteri(s.TEXTURE_2D,s.TEXTURE_MAG_FILTER,s.LINEAR),s.texParameteri(s.TEXTURE_2D,s.TEXTURE_WRAP_S,s.CLAMP_TO_EDGE),s.texParameteri(s.TEXTURE_2D,s.TEXTURE_WRAP_T,s.CLAMP_TO_EDGE),o={tex:n,texSize:l},this.continuousSdfPool.set(a,o),this.continuousSdfPool.size>16){let h=this.continuousSdfPool.keys().next().value;if(h){let c=this.continuousSdfPool.get(h);c&&s.deleteTexture(c.tex),this.continuousSdfPool.delete(h)}}}this.continuousSdfTexture=o.tex,this.continuousSdfTexSize=[o.texSize,o.texSize],this.continuousSdfKey=a},resize(i,e){this.dpr<=0&&(this.dpr=Math.min(window.devicePixelRatio||1,1.5));let t=Math.round(i*this.dpr),a=Math.round(e*this.dpr);(this.canvas.width!==t||this.canvas.height!==a)&&(this.canvas.width=t,this.canvas.height=a,this.gl.viewport(0,0,t,a),this.resizeFBOs(t,a));for(let o of this.buttonConfigs)this.fgDirtyIds.add(o.id);this.cssWidth=i,this.cssHeight=e,this.requestRender()}};var wt={setContentHeight(i){this.contentHeight=i,this.clampScrollY(),this.requestRender()},setScrollY(i){this.scrollVelocity=0,this.scrollY=this.clampScrollValue(i),this.requestRender()},setScrollVelocity(i){this.scrollVelocity=Math.max(-4e3,Math.min(4e3,i)),this.startAnimation()},getScrollY(){return this.scrollY},getScrollVelocity(){return this.scrollVelocity},clampScrollValue(i){let e=Math.max(0,this.contentHeight-this.cssHeight);return i<0?0:i>e?e:i},clampScrollY(){this.scrollY=this.clampScrollValue(this.scrollY)},setBackgroundColor(i){this.backgroundColor=i,this.requestRender()},setGravityAngle(i){this.gravityAngle!==i&&(this.gravityAngle=i,this.requestRender())}};var Oe=class{constructor(){this.samples=[]}resetTracking(){this.samples.length=0}addPosition(e,t){this.samples.push({t:e,p:t}),this.samples.length>20&&this.samples.shift()}calculateVelocity(e=100){let t=this.samples;if(t.length<2)return 0;let a=t[t.length-1].t,o=a-e,r=0,l=0,s=0,n=0,h=0;for(let d=t.length-1;d>=0;d--){let f=t[d];if(f.t<o)break;let g=(f.t-a)/1e3;l+=g,s+=f.p,n+=g*g,h+=g*f.p,r++}if(r<2)return 0;let c=r*n-l*l;return Math.abs(c)<1e-9?0:(r*h-l*s)/c}};var Mt={ensureToggleState(i,e,t=1.5,a=1){let o=this.toggleStates.get(i);return o?(t!==1.5&&(o.pressedScale=t),a!==1&&(o.valueRangeSpan=a)):(o={fraction:e,fractionVelocity:0,targetFraction:e,pressProgress:0,pressVelocity:0,targetPress:0,scaleX:1,scaleXVelocity:0,targetScaleX:1,scaleY:1,scaleYVelocity:0,targetScaleY:1,velocity:0,velocityVelocity:0,targetVelocity:0,isDragging:!1,trackVelocityAfterRelease:!1,velocityTracker:new Oe,lastFractionForVelocity:e,lastFractionTime:0,pressedScale:t,valueRangeSpan:a,panelOffset:0,panelOffsetVelocity:0,targetPanelOffset:0},this.toggleStates.set(i,o)),o},setToggleTarget(i,e){let t=this.ensureToggleState(i,e);t.isDragging||t.targetFraction!==e&&(t.targetFraction=e,t.trackVelocityAfterRelease=!1,t.targetVelocity=0,t.velocity=0,t.velocityVelocity=0,t.velocityTracker.resetTracking(),t.targetPress===0&&(t.targetPress=1,t.targetScaleX=t.pressedScale,t.targetScaleY=t.pressedScale),this.startAnimation())},beginToggleDrag(i,e){let t=this.ensureToggleState(i,e);t.isDragging=!0,t.targetPress=1,t.targetScaleX=t.pressedScale,t.targetScaleY=t.pressedScale,t.velocityTracker.resetTracking(),t.targetVelocity=0,t.velocity=0,t.velocityVelocity=0,this.startAnimation()},dragToggle(i,e,t,a,o){let r=this.ensureToggleState(i,e);if(!r.isDragging)return;let l=(t-a)/Math.max(1,o),s=Math.max(0,Math.min(1,e+l));r.targetFraction=s,this.startAnimation()},endToggleDrag(i){let e=this.toggleStates.get(i);if(!e)return 0;e.isDragging=!1;let t=e.targetFraction>=.5?1:0;return e.targetFraction=t,e.trackVelocityAfterRelease=!0,this.startAnimation(),t},endSliderDrag(i){let e=this.toggleStates.get(i);if(!e)return 0;e.isDragging=!1;let t=e.targetFraction;return e.trackVelocityAfterRelease=!0,this.startAnimation(),t},getToggleFraction(i){return this.toggleStates.get(i)?.fraction??0},setSliderDragPosition(i,e){let t=this.toggleStates.get(i);if(!t)return;let a=Math.max(0,Math.min(1,e));t.targetFraction!==a&&(t.targetFraction=a,this.startAnimation())},getToggleTarget(i){return this.toggleStates.get(i)?.targetFraction??0}};var Ee=Math.sqrt(300),Se=Ee*Math.sqrt(1-.5*.5),B=5e-4,jt=1e3,dt=Math.sqrt(jt),Qt=250,Ge=.6,ht=Math.sqrt(Qt),Nr=ht*Math.sqrt(1-Ge*Ge),Jt=250,De=.7,ft=Math.sqrt(Jt),Wr=ft*Math.sqrt(1-De*De),er=300,Be=.5,gt=Math.sqrt(er),Xr=gt*Math.sqrt(1-Be*Be);function Ae(i,e,t,a){let o=i-t,r=e,l=Math.exp(-.5*Ee*a),s=Math.cos(Se*a),n=Math.sin(Se*a),h=o*l*s+(r+.5*Ee*o)/Se*l*n,c=(r+.5*Ee*o)/Se,u=-.5*Ee*h+l*(-o*Se*n+c*Se*s);return{current:t+h,velocity:u}}function Ie(i,e,t,a,o){let r=i-t,l=e,s=Math.exp(-o*a),n=r*s+(l+o*r)*a*s,h=-o*r*s+(l+o*r)*(s-o*a*s);return{current:t+n,velocity:h}}function He(i,e,t,a,o,r){let l=i-t,s=e,n=o*Math.sqrt(1-r*r),h=Math.exp(-r*o*a),c=Math.cos(n*a),u=Math.sin(n*a),d=l*h*c+(s+r*o*l)/n*h*u,f=(s+r*o*l)/n,g=-r*o*d+h*(-l*n*u+f*n*c);return{current:t+d,velocity:g}}var Ft={setTabSelected(i,e,t){let a=this.ensureToggleState(i,e,ue.TAB_PRESSED_SCALE,t-1);a.isDragging||a.targetFraction!==e&&(a.targetFraction=e,a.trackVelocityAfterRelease=!1,a.targetVelocity=0,a.velocity=0,a.velocityVelocity=0,a.velocityTracker.resetTracking(),a.targetPress===0&&(a.targetPress=1,a.targetScaleX=a.pressedScale,a.targetScaleY=a.pressedScale),this.startAnimation())},beginTabDrag(i,e,t){let a=this.ensureToggleState(i,e,ue.TAB_PRESSED_SCALE,t-1);a.isDragging=!0,a.targetPress=1,a.targetScaleX=a.pressedScale,a.targetScaleY=a.pressedScale,a.velocityTracker.resetTracking(),a.targetVelocity=0,a.velocity=0,a.velocityVelocity=0,this.startAnimation()},dragTab(i,e,t,a,o,r){let l=this.ensureToggleState(i,e,ue.TAB_PRESSED_SCALE,r-1);if(!l.isDragging)return;let s=(t-a)/Math.max(1,o),n=Math.max(0,Math.min(r-1,e+s));l.targetFraction=n;let h=o*r,c=Math.max(-1,Math.min(1,(t-a)/Math.max(1,h))),u=1-Math.pow(1-Math.abs(c),2);l.targetPanelOffset=4*1*Math.sign(c)*u,this.startAnimation()},endTabDrag(i,e){let t=this.toggleStates.get(i);if(!t)return 0;t.isDragging=!1;let a=Math.round(t.targetFraction),o=Math.max(0,Math.min(e-1,a));return t.targetFraction=o,t.velocityTracker.resetTracking(),t.trackVelocityAfterRelease=!1,t.targetVelocity=0,t.targetPanelOffset=0,this.startAnimation(),o},getTabFraction(i){return this.toggleStates.get(i)?.fraction??0},getTabTarget(i){return this.toggleStates.get(i)?.targetFraction??0}};var _t={setElements(i){this.setButtons(i)},setButtons(i){let e=new Set(this.buttonConfigs.map(a=>a.id)),t=new Set(i.map(a=>a.id));for(let a of t)e.has(a)||this.fgDirtyIds.add(a);for(let a of i){let o=this.buttonConfigs.find(p=>p.id===a.id);if(!o)continue;let r=(p,C)=>{if(!p||!C)return p===C;if(p.length!==C.length)return!1;for(let R=0;R<p.length;R++)if(p[R]!==C[R])return!1;return!0},l=o.text?.icon,s=a.text?.icon,n=!!l!=!!s||l&&s&&(l.path!==s.path||l.size!==s.size||!r(l.color,s.color)),h=o.icon,c=a.icon,u=!!h!=!!c||h&&c&&(h.path!==c.path||h.size!==c.size||!r(h.color,c.color)),d=o.text,f=a.text,g=!!d!=!!f||d&&f&&(!r(d.color,f.color)||d.halo!==f.halo||d.fontSizePx!==f.fontSizePx||d.fontWeight!==f.fontWeight||d.align!==f.align||d.wrap!==f.wrap||d.paddingPx!==f.paddingPx||d.valign!==f.valign||d.maxLines!==f.maxLines);(o.label!==a.label||!r(o.labelColor,a.labelColor)||o.showChevron!==a.showChevron||o.rect.w!==a.rect.w||o.rect.h!==a.rect.h||a.text&&o.text&&o.text.content!==a.text.content||a.text&&!o.text||!a.text&&o.text||n||u||g)&&this.fgDirtyIds.add(a.id)}for(let a of e)if(!t.has(a)){this.buttonStates.delete(a);let o=this.fgTextures.get(a);o&&(this.gl.deleteTexture(o),this.fgTextures.delete(a)),this.fgDirtyIds.delete(a)}for(let a of i)this.buttonStates.has(a.id)||this.buttonStates.set(a.id,{pressProgress:0,pressVelocity:0,targetPress:0,dragX:0,dragY:0,dragVx:0,dragVy:0,targetDragX:0,targetDragY:0,startDragX:0,startDragY:0,interactiveValue:0,interactiveVelocity:0,targetInteractiveValue:0});this.buttonConfigs=i,this.requestRender()},setInteractiveValue(i,e){let t=this.buttonStates.get(i);t&&t.targetInteractiveValue!==e&&(t.targetInteractiveValue=e,this.startAnimation(),this.requestRender())},setPressed(i,e,t){let a=this.buttonStates.get(i);if(a){if(e){let o=this.buttonConfigs.find(r=>r.id===i);if(o&&t){let r=t.x-o.rect.x,l=t.y-o.rect.y;a.targetPress===0&&(a.startDragX=r,a.startDragY=l,a.dragX=r,a.dragY=l,a.dragVx=0,a.dragVy=0),a.dragX=r,a.dragY=l,a.dragVx=0,a.dragVy=0,a.targetDragX=r,a.targetDragY=l}a.targetPress=1}else a.targetPress=0,a.targetDragX=a.startDragX,a.targetDragY=a.startDragY;this.startAnimation()}},setDragPosition(i,e){let t=this.buttonStates.get(i);if(!t||t.targetPress===0)return;let a=this.buttonConfigs.find(l=>l.id===i);if(!a)return;let o=e.x-a.rect.x,r=e.y-a.rect.y;t.dragX=o,t.dragY=r,t.dragVx=0,t.dragVy=0,t.targetDragX=o,t.targetDragY=r,this.requestRender()}};var Ot={startAnimation(){if(this.animRafId!==null)return;let i=performance.now(),e=()=>{let t=performance.now(),a=Math.min((t-i)/1e3,.05);i=t;let o=!1;for(let r of this.buttonStates.values()){if(Math.abs(r.targetPress-r.pressProgress)>B||Math.abs(r.pressVelocity)>B){let n=Ae(r.pressProgress,r.pressVelocity,r.targetPress,a);r.pressProgress=n.current,r.pressVelocity=n.velocity,o=!0}else r.pressProgress=r.targetPress,r.pressVelocity=0;if(Math.abs(r.targetDragX-r.dragX)>B||Math.abs(r.dragVx)>B){let n=Ae(r.dragX,r.dragVx,r.targetDragX,a);r.dragX=n.current,r.dragVx=n.velocity,o=!0}else r.dragX=r.targetDragX,r.dragVx=0;if(Math.abs(r.targetDragY-r.dragY)>B||Math.abs(r.dragVy)>B){let n=Ae(r.dragY,r.dragVy,r.targetDragY,a);r.dragY=n.current,r.dragVy=n.velocity,o=!0}else r.dragY=r.targetDragY,r.dragVy=0;if(Math.abs(r.targetInteractiveValue-r.interactiveValue)>B||Math.abs(r.interactiveVelocity)>B){let n=Ae(r.interactiveValue,r.interactiveVelocity,r.targetInteractiveValue,a);r.interactiveValue=n.current,r.interactiveVelocity=n.velocity,o=!0}else r.interactiveValue=r.targetInteractiveValue,r.interactiveVelocity=0}for(let r of this.toggleStates.values()){if(r.targetPress===1&&!r.isDragging&&Math.abs(r.targetFraction-r.fraction)<.02&&(r.targetPress=0,r.targetScaleX=1,r.targetScaleY=1,this.startAnimation()),Math.abs(r.targetFraction-r.fraction)>B||Math.abs(r.fractionVelocity)>B){let d=Ie(r.fraction,r.fractionVelocity,r.targetFraction,a,dt);if(r.fraction=d.current,r.fractionVelocity=d.velocity,r.trackVelocityAfterRelease||r.isDragging){let f=performance.now();r.velocityTracker.addPosition(f,r.fraction);let g=r.velocityTracker.calculateVelocity(),p=r.valueRangeSpan||1;r.targetVelocity=g/p}o=!0}else r.fraction=r.targetFraction,r.fractionVelocity=0,r.isDragging||(r.targetVelocity=0,r.trackVelocityAfterRelease=!1,r.velocityTracker.resetTracking());if(Math.abs(r.targetPress-r.pressProgress)>B||Math.abs(r.pressVelocity)>B){let d=Ie(r.pressProgress,r.pressVelocity,r.targetPress,a,dt);r.pressProgress=d.current,r.pressVelocity=d.velocity,o=!0}else r.pressProgress=r.targetPress,r.pressVelocity=0;if(Math.abs(r.targetScaleX-r.scaleX)>B||Math.abs(r.scaleXVelocity)>B){let d=He(r.scaleX,r.scaleXVelocity,r.targetScaleX,a,ht,Ge);r.scaleX=d.current,r.scaleXVelocity=d.velocity,o=!0}else r.scaleX=r.targetScaleX,r.scaleXVelocity=0;if(Math.abs(r.targetScaleY-r.scaleY)>B||Math.abs(r.scaleYVelocity)>B){let d=He(r.scaleY,r.scaleYVelocity,r.targetScaleY,a,ft,De);r.scaleY=d.current,r.scaleYVelocity=d.velocity,o=!0}else r.scaleY=r.targetScaleY,r.scaleYVelocity=0;if(Math.abs(r.targetVelocity-r.velocity)>B||Math.abs(r.velocityVelocity)>B){let d=He(r.velocity,r.velocityVelocity,r.targetVelocity,a,gt,Be);r.velocity=d.current,r.velocityVelocity=d.velocity,o=!0}else r.velocity=r.targetVelocity,r.velocityVelocity=0;if(Math.abs(r.targetPanelOffset-r.panelOffset)>B||Math.abs(r.panelOffsetVelocity)>B){let d=Ie(r.panelOffset,r.panelOffsetVelocity,r.targetPanelOffset,a,Math.sqrt(300));r.panelOffset=d.current,r.panelOffsetVelocity=d.velocity,o=!0}else r.panelOffset=r.targetPanelOffset,r.panelOffsetVelocity=0}if(Math.abs(this.scrollVelocity)>.5){let l=this.scrollY+this.scrollVelocity*a,s=this.clampScrollValue(l);s!==l?(this.scrollY=s,this.scrollVelocity=0):(this.scrollY=s,this.scrollVelocity*=Math.exp(-4*a)),o=!0}else this.scrollVelocity=0;this.requestRender(),o?this.animRafId=requestAnimationFrame(e):this.animRafId=null};this.animRafId=requestAnimationFrame(e)},requestRender(){this.needsRedraw=!0,this.rafId===null&&(this.rafId=requestAnimationFrame(()=>{this.rafId=null,this.render()}))}};var Gt={rasterizeForeground(i){if(i.kind==="text"&&i.text){this.rasterizeText(i);return}if(i.kind!=="button"&&!i.label&&!i.icon){this.fgDirtyIds.delete(i.id);return}let e=this.dpr,t=Math.max(1,Math.round(i.rect.w*e)),a=Math.max(1,Math.round(i.rect.h*e));this.fgCanvas.width!==t&&(this.fgCanvas.width=t),this.fgCanvas.height!==a&&(this.fgCanvas.height=a);let o=this.fgCtx;o.setTransform(1,0,0,1,0,0),o.clearRect(0,0,t,a),o.scale(e,e);let r=i.rect.w,l=i.rect.h;if(i.icon){let u=i.icon.size,d=i.icon.color;o.save(),o.translate(r/2-u/2,l/2-u/2);let f=i.icon.viewport??24;o.scale(u/f,u/f);let g=new Path2D(i.icon.path);o.fillStyle=`rgba(${Math.round(d[0]*255)}, ${Math.round(d[1]*255)}, ${Math.round(d[2]*255)}, ${d[3]})`,o.fill(g),o.restore(),this.uploadForegroundTexture(i.id),this.fgDirtyIds.delete(i.id);return}let s=i.labelFontSizePx??l*(15/48),n='-apple-system, BlinkMacSystemFont, "SF Pro Text", "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif';o.font=`400 ${s}px ${n}`,o.textBaseline="middle",o.textAlign="center";let h=`rgba(${Math.round(i.labelColor[0]*255)}, ${Math.round(i.labelColor[1]*255)}, ${Math.round(i.labelColor[2]*255)}, ${i.labelColor[3]})`,c=i.labelColor[0]+i.labelColor[1]+i.labelColor[2]<1.5;if(o.save(),o.shadowColor=c?"rgba(255,255,255,0.45)":"rgba(0,0,0,0.15)",o.shadowBlur=c?s*.12:s*.05,o.fillStyle=h,o.fillText(i.label,r/2,l/2+.5),o.restore(),i.showChevron){let u=s*.93,d=o.measureText(i.label).width,f=r/2+d/2+s*.53+u/2,g=l/2;o.save(),o.strokeStyle=h,o.globalAlpha=.6,o.lineWidth=s*.107,o.lineCap="round",o.lineJoin="round",o.beginPath(),o.moveTo(f-u*.3,g-u*.4),o.lineTo(f+u*.2,g),o.lineTo(f-u*.3,g+u*.4),o.stroke(),o.restore()}this.uploadForegroundTexture(i.id),this.fgDirtyIds.delete(i.id)},rasterizeText(i){if(!i.text)return;let e=this.dpr,t=Math.max(1,Math.round(i.rect.w*e)),a=Math.max(1,Math.round(i.rect.h*e));this.fgCanvas.width!==t&&(this.fgCanvas.width=t),this.fgCanvas.height!==a&&(this.fgCanvas.height=a);let o=this.fgCtx;o.setTransform(1,0,0,1,0,0),o.clearRect(0,0,t,a),o.scale(e,e);let r=i.text,l=i.rect.w,s=i.rect.h,n='-apple-system, BlinkMacSystemFont, "SF Pro Text", "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif';o.font=`${r.fontWeight} ${r.fontSizePx}px ${n}`,o.textBaseline="middle";let h=r.paddingPx??0,c="none";r.halo==="light"?c="light":r.halo==="dark"?c="dark":(r.halo==="auto"||r.halo===void 0)&&(c=r.color[0]+r.color[1]+r.color[2]<1.5?"light":"dark"),c==="light"?(o.shadowColor="rgba(255,255,255,0.55)",o.shadowBlur=r.fontSizePx*.16):c==="dark"?(o.shadowColor="rgba(0,0,0,0.28)",o.shadowBlur=r.fontSizePx*.1):(o.shadowColor="transparent",o.shadowBlur=0);let u=`rgba(${Math.round(r.color[0]*255)}, ${Math.round(r.color[1]*255)}, ${Math.round(r.color[2]*255)}, ${r.color[3]})`;o.fillStyle=u;let d=0;if(r.icon){let f=r.icon.size,g=r.icon.layoutSize??f,p=r.content?2:0,C=g+p+(r.content?r.fontSizePx:0),R=s/2-C/2,M=l/2,b=R+g/2;o.save(),o.translate(M-f/2,b-f/2);let I=r.icon.viewport??24;o.scale(f/I,f/I);let y=new Path2D(r.icon.path),S=r.icon.color;o.fillStyle=`rgba(${Math.round(S[0]*255)}, ${Math.round(S[1]*255)}, ${Math.round(S[2]*255)}, ${S[3]})`,o.fill(y),o.restore(),d=(g+p)/2}if(r.align==="center")if(o.textAlign="center",r.wrap){let f=lt(o,r.content,l-h*2);r.maxLines!=null&&f.length>r.maxLines&&(f=f.slice(0,r.maxLines));let g=r.fontSizePx*1.35,p=g*f.length,C;r.valign==="top"?C=g/2+d:r.valign==="bottom"?C=s-p+g/2+d:C=s/2-p/2+g/2+d;for(let R of f)o.fillText(R,l/2,C),C+=g}else o.fillText(r.content,l/2,s/2+.5+d);else if(r.align==="left")if(o.textAlign="left",r.wrap){let f=lt(o,r.content,l-h*2);r.maxLines!=null&&f.length>r.maxLines&&(f=f.slice(0,r.maxLines));let g=r.fontSizePx*1.35,p=g*f.length,C;r.valign==="top"?C=g/2+d:r.valign==="bottom"?C=s-p+g/2+d:C=s/2-p/2+g/2+d;for(let R of f)o.fillText(R,h,C),C+=g}else o.fillText(r.content,h,s/2+.5+d);else o.textAlign="right",o.fillText(r.content,l-h,s/2+.5+d);this.uploadForegroundTexture(i.id),this.fgDirtyIds.delete(i.id)},uploadForegroundTexture(i){let e=this.gl,t=this.fgTextures.get(i);t||(t=e.createTexture(),this.fgTextures.set(i,t)),e.bindTexture(e.TEXTURE_2D,t),e.pixelStorei(e.UNPACK_PREMULTIPLY_ALPHA_WEBGL,!0),e.pixelStorei(e.UNPACK_FLIP_Y_WEBGL,!1),e.texImage2D(e.TEXTURE_2D,0,e.RGBA,e.RGBA,e.UNSIGNED_BYTE,this.fgCanvas),e.texParameteri(e.TEXTURE_2D,e.TEXTURE_MIN_FILTER,e.LINEAR),e.texParameteri(e.TEXTURE_2D,e.TEXTURE_MAG_FILTER,e.LINEAR),e.texParameteri(e.TEXTURE_2D,e.TEXTURE_WRAP_S,e.CLAMP_TO_EDGE),e.texParameteri(e.TEXTURE_2D,e.TEXTURE_WRAP_T,e.CLAMP_TO_EDGE),e.pixelStorei(e.UNPACK_PREMULTIPLY_ALPHA_WEBGL,!1)}};var Dt={render(){if(!this.needsRedraw||(this.needsRedraw=!1,!this.wallpaperReady&&!this.backgroundColor))return;let i=this.gl;this.resizeFBOs(this.canvas.width,this.canvas.height);for(let u of this.buttonConfigs)this.fgDirtyIds.has(u.id)&&this.rasterizeForeground(u);if(this.renderBackground(),this.buttonConfigs.length===0){this.bindFBO(null),this.drawCopy(this.fboATex);return}let e=this.buttonConfigs.find(u=>(u.sceneBlurRadius??0)>=.5);if(e){let u=e.sceneBlurRadius*this.dpr,d=this.blurTexture(this.fboATex,u);this.bindFBO(this.fboA),this.drawCopy(d)}i.enable(i.BLEND),i.blendFunc(i.SRC_ALPHA,i.ONE_MINUS_SRC_ALPHA);let t=this.scrollY,a=120,o=-a,r=this.cssHeight+a,l=u=>{let d=u.scroll?u.rect.y-t:u.rect.y;return{x:u.rect.x,y:d,w:u.rect.w,h:u.rect.h}},s=this.fboA,n=this.fboATex,h=this.fboB,c=this.fboBTex;for(let u of this.buttonConfigs){if(u.renderOnTop)continue;let d=u.scroll?u.rect.y-t:u.rect.y;if(d+u.rect.h<o||d>r)continue;let f=l(u),g=this.buttonStates.get(u.id);if(this.renderNonGlassElement(u,f,g,s))continue;u.backdropFbo&&u.scrimColor&&this.renderDialogBackdrop(u.scrimColor,u.brightness,u.contrast,u.saturation),u.useContinuousSdf&&this.loadContinuousSdf(u.rect.w,u.rect.h,u.cornerRadius);let p=this.renderGlassElement(u,g,s,n,h,c,f);s=p.curFbo,n=p.curTex,h=p.otherFbo,c=p.otherTex,u.isBottomTabContainer&&this.tabsBackdropFbo&&this.tabsBackdropTex&&(this.bindFBO(this.tabsBackdropFbo),this.gl.clearColor(0,0,0,0),this.gl.clear(this.gl.COLOR_BUFFER_BIT),this.drawCopy(n),this.bindFBO(s),this.gl.enable(this.gl.BLEND),this.gl.blendFunc(this.gl.SRC_ALPHA,this.gl.ONE_MINUS_SRC_ALPHA))}for(let u of this.buttonConfigs){if(!u.renderOnTop)continue;let d=u.scroll?u.rect.y-t:u.rect.y;if(d+u.rect.h<o||d>r)continue;let f=l(u),g=this.buttonStates.get(u.id);if(this.renderNonGlassElement(u,f,g,s))continue;let p=this.renderGlassElement(u,g,s,n,h,c,f);s=p.curFbo,n=p.curTex,h=p.otherFbo,c=p.otherTex}this.bindFBO(null),this.drawCopy(n)},setSdfUniforms(i,e,t,a){let o=this.gl;o.bindBuffer(o.ARRAY_BUFFER,this.quadBuffer),o.enableVertexAttribArray(e),o.vertexAttribPointer(e,2,o.FLOAT,!1,0,0),o.uniform2f(i.uCanvasSize,this.canvas.width,this.canvas.height),o.uniform2f(i.uOffset,t.x*this.dpr,t.y*this.dpr),o.uniform2f(i.uSize,t.w*this.dpr,t.h*this.dpr),o.uniform4f(i.uCornerRadii,a*this.dpr,a*this.dpr,a*this.dpr,a*this.dpr)},renderBackground(){let i=this.gl;if(this.bindFBO(this.fboA),i.disable(i.BLEND),this.backgroundColor){let[e,t,a]=this.backgroundColor;this.drawSolidFill(e,t,a,1)}else i.useProgram(this.wallpaperProgram),i.bindBuffer(i.ARRAY_BUFFER,this.quadBuffer),i.enableVertexAttribArray(this.aPosLocWp),i.vertexAttribPointer(this.aPosLocWp,2,i.FLOAT,!1,0,0),i.activeTexture(i.TEXTURE0),i.bindTexture(i.TEXTURE_2D,this.wallpaperTexture),i.uniform1i(this.uWp.uBackdrop,0),i.uniform2f(this.uWp.uCanvasSize,this.canvas.width,this.canvas.height),i.uniform2f(this.uWp.uWallpaperSize,this.wallpaperSize[0],this.wallpaperSize[1]),i.drawArrays(i.TRIANGLES,0,6)},renderDialogBackdrop(i,e,t,a){let o=`${i.join(",")}|${e},${t},${a}`;if(this.dialogBackdropKey===o)return;this.dialogBackdropKey=o;let r=this.gl;if(this.bindFBO(this.dialogBackdropFbo),r.disable(r.BLEND),this.backgroundColor){let[l,s,n]=this.backgroundColor;this.drawSolidFill(l,s,n,1)}else r.useProgram(this.wallpaperProgram),r.bindBuffer(r.ARRAY_BUFFER,this.quadBuffer),r.enableVertexAttribArray(this.aPosLocWp),r.vertexAttribPointer(this.aPosLocWp,2,r.FLOAT,!1,0,0),r.activeTexture(r.TEXTURE0),r.bindTexture(r.TEXTURE_2D,this.wallpaperTexture),r.uniform1i(this.uWp.uBackdrop,0),r.uniform2f(this.uWp.uCanvasSize,this.canvas.width,this.canvas.height),r.uniform2f(this.uWp.uWallpaperSize,this.wallpaperSize[0],this.wallpaperSize[1]),r.drawArrays(r.TRIANGLES,0,6);i[3]>.001&&(r.enable(r.BLEND),r.blendFuncSeparate(r.SRC_ALPHA,r.ONE_MINUS_SRC_ALPHA,r.ONE,r.ONE_MINUS_SRC_ALPHA),this.drawSolidFill(i[0],i[1],i[2],i[3]),r.blendFunc(r.SRC_ALPHA,r.ONE_MINUS_SRC_ALPHA)),this.bindFBO(this.blurFboA),this.drawColorControls(this.dialogBackdropTex,e,t,a),this.bindFBO(this.dialogBackdropFbo),this.drawCopy(this.blurFboATex)},renderNonGlassElement(i,e,t,a){let o=this.gl,r=e;if(i.enterProgress!=null){let l=i.enterProgress,s=l<0?(1-Math.exp(-Math.abs(l)))*-1:l<=1?l:1+(1-Math.exp(-(l-1))),n=-48*1*(1-s),h=i.enterStretchFactor!=null&&s>1?i.enterStretchFactor*(s-1)*32*1:0;r={x:e.x,y:e.y+n+h,w:e.w,h:e.h}}if(i.kind==="plain-rect"&&i.plainRect){let l=i.isToggleTrack?null:i.plainRect.color;if(l&&l[3]<=0)return!0;this.bindFBO(a);let s;if(i.isToggleTrack){let c=this.toggleStates.get(i.isToggleTrack.groupId),u=c?c.fraction:0,d=i.isToggleTrack.offColor,f=i.isToggleTrack.onColor;s=[d[0]+(f[0]-d[0])*u,d[1]+(f[1]-d[1])*u,d[2]+(f[2]-d[2])*u,d[3]+(f[3]-d[3])*u]}else s=i.plainRect.color;let n=r;if(i.isSliderFill){let c=this.toggleStates.get(i.isSliderFill.groupId),u=c?c.fraction:0,d=Math.max(i.isSliderFill.minW,i.isSliderFill.trackW*u);n={x:e.x,y:e.y,w:d,h:e.h}}o.useProgram(this.plainRectProgram),this.setSdfUniforms(this.uPr,this.aPosLocPr,n,i.cornerRadius),o.blendFuncSeparate(o.SRC_ALPHA,o.ONE_MINUS_SRC_ALPHA,o.ONE,o.ONE_MINUS_SRC_ALPHA);let h=i.enterProgress!=null?(()=>{let c=i.enterSafeProgress!=null?Math.max(0,Math.min(1,i.enterSafeProgress)):Math.max(0,Math.min(1,i.enterProgress));return Re(c)})():1;return o.uniform4f(this.uPr.uColor,s[0],s[1],s[2],s[3]*h),o.uniform1f(this.uPr.uCornerStyle,this.cornerStyle),i.useContinuousSdf&&this.continuousSdfTexture?(o.activeTexture(o.TEXTURE2),o.bindTexture(o.TEXTURE_2D,this.continuousSdfTexture),o.uniform1i(this.uPr.uContinuousSdf,2),o.uniform1f(this.uPr.uUseContinuousSdf,1),o.uniform2f(this.uPr.uContinuousSdfTexSize,this.continuousSdfTexSize[0],this.continuousSdfTexSize[1]),o.uniform2f(this.uPr.uContinuousSdfElementSize,r.w*this.dpr,r.h*this.dpr)):o.uniform1f(this.uPr.uUseContinuousSdf,0),o.drawArrays(o.TRIANGLES,0,6),!0}if(i.kind==="progressive-blur"&&i.progressiveBlur){this.bindFBO(a),o.useProgram(this.progressiveBlurProgram),this.setSdfUniforms(this.uPb,this.aPosLocPb,r,i.cornerRadius),o.blendFunc(o.ONE,o.ONE_MINUS_SRC_ALPHA),o.activeTexture(o.TEXTURE0),o.bindTexture(o.TEXTURE_2D,this.wallpaperTexture),o.uniform1i(this.uPb.uBackdrop,0),o.uniform2f(this.uPb.uWallpaperSize,this.wallpaperSize[0],this.wallpaperSize[1]),o.uniform1f(this.uPb.uBlurRadius,i.progressiveBlur.blurRadius*this.dpr);let l=i.progressiveBlur.tintColor;return o.uniform4f(this.uPb.uTintColor,l[0],l[1],l[2],l[3]),o.uniform1f(this.uPb.uTintIntensity,i.progressiveBlur.tintIntensity),o.drawArrays(o.TRIANGLES,0,6),!0}if(i.kind==="text"){this.bindFBO(a);let l=r,s=1,n=1;if(i.isBottomTabContent){let u=this.toggleStates.get(i.isBottomTabContent.groupId);if(u){let d=i.isBottomTabContent.containerWidth??i.rect.w*4,f=1+16*1/d*u.pressProgress;s=f,n=f;let g=i.isBottomTabContent.containerCenterX??i.rect.x+i.rect.w/2,p=i.isBottomTabContent.containerCenterY??i.rect.y+i.rect.h/2,C=i.rect.x+i.rect.w/2,R=i.rect.y+i.rect.h/2,M=g+(C-g)*f+u.panelOffset,b=p+(R-p)*f,I=i.rect.w*s,y=i.rect.h*n;l={x:M-I/2,y:b-y/2,w:I,h:y}}}let h=t?.pressProgress??0;if(i.isInteractive&&h>.001){let u=i.pressTintColor;o.useProgram(this.tintProgram),o.bindBuffer(o.ARRAY_BUFFER,this.quadBuffer),o.enableVertexAttribArray(this.aPosLocTn),o.vertexAttribPointer(this.aPosLocTn,2,o.FLOAT,!1,0,0),u?o.blendFunc(o.SRC_ALPHA,o.ONE_MINUS_SRC_ALPHA):o.blendFunc(o.SRC_ALPHA,o.ONE),o.uniform2f(this.uTn.uCanvasSize,this.canvas.width,this.canvas.height),o.uniform2f(this.uTn.uOffset,l.x*this.dpr,l.y*this.dpr),o.uniform2f(this.uTn.uSize,l.w*this.dpr,l.h*this.dpr),o.uniform4f(this.uTn.uCornerRadii,0,0,0,0),o.uniform2f(this.uTn.uOriginalSize,l.w*this.dpr,l.h*this.dpr),o.uniform1f(this.uTn.uOriginalCornerRadius,0),o.uniform2f(this.uTn.uLayerScale,1,1),u?o.uniform4f(this.uTn.uColor,u[0],u[1],u[2],.1*h):o.uniform4f(this.uTn.uColor,1,1,1,.1*h),o.drawArrays(o.TRIANGLES,0,6),o.blendFunc(o.SRC_ALPHA,o.ONE_MINUS_SRC_ALPHA)}let c=this.fgTextures.get(i.id);return c&&(o.useProgram(this.foregroundProgram),o.bindBuffer(o.ARRAY_BUFFER,this.quadBuffer),o.enableVertexAttribArray(this.aPosLocFg),o.vertexAttribPointer(this.aPosLocFg,2,o.FLOAT,!1,0,0),o.blendFunc(o.ONE,o.ONE_MINUS_SRC_ALPHA),o.activeTexture(o.TEXTURE0),o.bindTexture(o.TEXTURE_2D,c),o.uniform1i(this.uFg.uTexture,0),o.uniform2f(this.uFg.uCanvasSize,this.canvas.width,this.canvas.height),o.uniform2f(this.uFg.uOffset,l.x*this.dpr,l.y*this.dpr),o.uniform2f(this.uFg.uSize,l.w*this.dpr,l.h*this.dpr),o.uniform4f(this.uFg.uCornerRadii,i.cornerRadius*this.dpr,i.cornerRadius*this.dpr,i.cornerRadius*this.dpr,i.cornerRadius*this.dpr),o.uniform2f(this.uFg.uOriginalSize,i.rect.w*this.dpr,i.rect.h*this.dpr),o.uniform1f(this.uFg.uOriginalCornerRadius,i.cornerRadius*this.dpr),o.uniform2f(this.uFg.uLayerScale,s,n),o.uniform1f(this.uFg.uCornerStyle,this.cornerStyle),o.uniform1f(this.uFg.uAlpha,i.enterProgress!=null?(()=>{let u=i.enterSafeProgress!=null?Math.max(0,Math.min(1,i.enterSafeProgress)):Math.max(0,Math.min(1,i.enterProgress));return Re(u)})():1),o.drawArrays(o.TRIANGLES,0,6),o.blendFunc(o.SRC_ALPHA,o.ONE_MINUS_SRC_ALPHA)),!0}return!1}};var Bt={renderGlassElement(i,e,t,a,o,r,l){let s=this.gl,n=i.kind==="button",h=e?.pressProgress??0,c=4/48,u=1,d=0,f=0,g=1,p=1;if(i.enterProgress!=null){let x=i.enterProgress,P=x<0?(1-Math.exp(-Math.abs(x)))*-1:x<=1?x:1+(1-Math.exp(-(x-1)));f+=-48*1*(1-P),i.enterStretchFactor!=null&&P>1&&(f+=i.enterStretchFactor*(P-1)*32*1);let L=1+.1*Math.max(0,P-1);g/=L,p*=L}if(n&&i.isInteractive&&e){let x=i.rect.w,P=i.rect.h,L=Math.max(x,P),k=Math.min(x,P),w=.05,X=c;u=1+c*h;let Q=e.dragX-e.startDragX,fe=e.dragY-e.startDragY;d=k*Math.tanh(w*Q/k),f=k*Math.tanh(w*fe/k);let Le=Math.atan2(fe,Q),F=Math.min(x/P,1),J=Math.min(P/x,1);g=u+X*Math.abs(Math.cos(Le)*Q/L)*F,p=u+X*Math.abs(Math.sin(Le)*fe/L)*J}else i.enterProgress==null&&(g=u,p=u);let C=0,R=1,M=1,b=0;if(i.isToggleKnob){let x=this.toggleStates.get(i.isToggleKnob.groupId);if(x){C=x.fraction*i.isToggleKnob.dragWidth,R=x.scaleX,M=x.scaleY,b=x.pressProgress;let P=i.isToggleKnob.velocityDivisor??50,L=x.velocity/P,A=Math.max(-.2,Math.min(.2,L*.75)),k=Math.max(-.2,Math.min(.2,L*.25));R=R/(1-A),M=M*(1-k)}}if(g*=R,p*=M,i.isBottomTabContainer){let x=this.toggleStates.get(i.isBottomTabContainer.groupId);if(x){let P=1+16*1/i.rect.w*x.pressProgress;g*=P,p*=P,d+=x.panelOffset,b=x.pressProgress}}if(i.isBottomTabContent){let x=this.toggleStates.get(i.isBottomTabContent.groupId);if(x){let P=i.isBottomTabContent.containerWidth??i.rect.w,L=1+16*1/P*x.pressProgress;g*=L;let A=1+.2*x.pressProgress;g*=A,p*=L*A,d+=x.panelOffset}}if(i.isBottomTabIndicator){let x=this.toggleStates.get(i.isBottomTabIndicator.groupId);if(x){C+=x.fraction*i.isBottomTabIndicator.dragWidth,C+=x.panelOffset;let P=x.scaleX,L=x.scaleY,A=x.velocity/10,k=Math.max(-.2,Math.min(.2,A*.75)),w=Math.max(-.2,Math.min(.2,A*.25)),X=P/(1-k),Q=L*(1-w);g*=X,p*=Q,b=Math.max(b,x.pressProgress)}}i.elementScaleX!=null&&(g*=i.elementScaleX),i.elementScaleY!=null&&(p*=i.elementScaleY);let I,y;I=l.x+i.rect.w/2+d+C,y=l.y+i.rect.h/2+f;let S=i.rect.w*g,v=i.rect.h*p,O=I-S/2,H=y-v/2,_=i.cornerRadius*Math.min(g,p),T=[_,_,_,_];this.bindFBO(o),this.drawCopy(a),s.enable(s.BLEND),s.blendFunc(s.SRC_ALPHA,s.ONE_MINUS_SRC_ALPHA);let E=60,m=Math.max(0,Math.round((O-E)*this.dpr)),re=Math.max(0,Math.round((this.cssHeight-(H+v+E))*this.dpr)),K=Math.min(this.fboW-m,Math.round((S+2*E)*this.dpr)),$=Math.min(this.fboH-re,Math.round((v+2*E)*this.dpr));s.enable(s.SCISSOR_TEST),s.scissor(m,re,K,$);let W={el:i,st:e,isButton:n,p:h,sx:O,sy:H,sw:S,sh:v,radii:T,togglePressProgress:b,elHighlightAlpha:i.isToggleKnob||i.isBottomTabIndicator?0:i.highlight?i.highlight.alpha:0,enterAlpha:i.enterProgress!=null?(()=>{let x=i.enterSafeProgress!=null?Math.max(0,Math.min(1,i.enterSafeProgress)):Math.max(0,Math.min(1,i.enterProgress));return Re(x)})():1,layerScaleX:g,layerScaleY:p,layerScale:Math.min(g,p),origW:i.rect.w,origH:i.rect.h,origCornerRadius:i.cornerRadius,elementRotation:i.elementRotation??0};if(this.renderGlassShadowPass(W),i.useSeparableBlur&&i.blurRadius>=.5){let x=i.blurRadius*W.layerScale*this.dpr,P=i.backdropFbo&&this.dialogBackdropTex?this.dialogBackdropTex:a,L=this.blurTexture(P,x);this.gl.enable(this.gl.BLEND),this.gl.blendFunc(this.gl.SRC_ALPHA,this.gl.ONE_MINUS_SRC_ALPHA),this.bindFBO(o),this.gl.viewport(0,0,this.fboW,this.fboH);let A=i.backdropFbo?{...W,el:{...i,backdropFbo:!1}}:W;this.renderGlassElementPass(A,L)}else this.renderGlassElementPass(W,a);return this.renderGlassPostPasses(W),s.disable(s.SCISSOR_TEST),{curFbo:o,curTex:r,otherFbo:t,otherTex:a}},renderGlassShadowPass(i){let e=this.gl,{el:t,sx:a,sy:o,sw:r,sh:l,radii:s}=i;if(!t.outerShadow||t.outerShadow.radius<=.5)return;let n=t.outerShadow.alpha;t.isBottomTabIndicator&&(n*=i.togglePressProgress),!(n<=.001)&&(e.useProgram(this.shadowProgram),e.bindBuffer(e.ARRAY_BUFFER,this.quadBuffer),e.enableVertexAttribArray(this.aPosLocSh),e.vertexAttribPointer(this.aPosLocSh,2,e.FLOAT,!1,0,0),e.blendFunc(e.SRC_ALPHA,e.ONE_MINUS_SRC_ALPHA),e.uniform2f(this.uSh.uCanvasSize,this.canvas.width,this.canvas.height),e.uniform2f(this.uSh.uElementOffset,a*this.dpr,o*this.dpr),e.uniform2f(this.uSh.uElementSize,r*this.dpr,l*this.dpr),e.uniform4f(this.uSh.uCornerRadii,s[0]*this.dpr,s[1]*this.dpr,s[2]*this.dpr,s[3]*this.dpr),e.uniform2f(this.uSh.uOriginalSize,i.origW*this.dpr,i.origH*this.dpr),e.uniform1f(this.uSh.uOriginalCornerRadius,i.origCornerRadius*this.dpr),e.uniform2f(this.uSh.uLayerScale,i.layerScaleX,i.layerScaleY),e.uniform1f(this.uSh.uElementRotation,i.elementRotation),e.uniform1f(this.uSh.uCornerStyle,this.cornerStyle),e.uniform1f(this.uSh.uShadowRadius,t.outerShadow.radius*this.dpr),e.uniform2f(this.uSh.uShadowOffset,t.outerShadow.offsetX*this.dpr,t.outerShadow.offsetY*this.dpr),e.uniform4f(this.uSh.uShadowColor,t.outerShadow.color[0],t.outerShadow.color[1],t.outerShadow.color[2],n),e.drawArrays(e.TRIANGLES,0,6))}};var It={renderGlassElementPass(i,e){let t=this.gl,{el:a,sx:o,sy:r,sw:l,sh:s,radii:n,togglePressProgress:h,layerScale:c}=i;t.useProgram(this.elementProgram),t.bindBuffer(t.ARRAY_BUFFER,this.quadBuffer),t.enableVertexAttribArray(this.aPosLocEl),t.vertexAttribPointer(this.aPosLocEl,2,t.FLOAT,!1,0,0),t.blendFunc(t.SRC_ALPHA,t.ONE_MINUS_SRC_ALPHA),t.activeTexture(t.TEXTURE0),t.bindTexture(t.TEXTURE_2D,e),t.uniform1i(this.uEl.uBackdrop,0),this.wallpaperTexture&&(t.activeTexture(t.TEXTURE1),t.bindTexture(t.TEXTURE_2D,this.wallpaperTexture),t.uniform1i(this.uEl.uWallpaperSampler,1)),t.uniform2f(this.uEl.uCanvasSize,this.canvas.width,this.canvas.height),t.uniform2f(this.uEl.uWallpaperSize,this.wallpaperSize[0],this.wallpaperSize[1]),t.uniform2f(this.uEl.uElementOffset,o*this.dpr,r*this.dpr),t.uniform2f(this.uEl.uElementSize,l*this.dpr,s*this.dpr),t.uniform4f(this.uEl.uCornerRadii,n[0]*this.dpr,n[1]*this.dpr,n[2]*this.dpr,n[3]*this.dpr),t.uniform2f(this.uEl.uOriginalSize,i.origW*this.dpr,i.origH*this.dpr),t.uniform1f(this.uEl.uOriginalCornerRadius,i.origCornerRadius*this.dpr),t.uniform2f(this.uEl.uLayerScale,i.layerScaleX,i.layerScaleY),t.uniform1f(this.uEl.uElementRotation,a.elementRotation??0);let u=a.refractionHeight,d=a.refractionAmount,f=a.blurRadius,g=a.highlight?a.highlight.alpha:0,p=a.surfaceColor[3];if(a.isBottomTabIndicator){let F=h;u=a.refractionHeight*F,d=a.refractionAmount*F,f=0,g=(a.highlight?.alpha??0)*F}let C=1,R=1,M=0,b=0,I=1,y=1,S=1,v=1,O=0,H=0,_=0,T=0,E=0,m=0,re=0,K=0,$=0;if(a.isToggleKnob){let F=h;u=a.refractionHeight*F,d=a.refractionAmount*F,f=8*(1-F),g=(a.highlight?.alpha??0)*F,p=0;let J=a.isToggleKnob.velocityDivisor===10,Z=J?1:.75,ke=J?1:.75;if(C=2/3+(Z-2/3)*F,R=0+(ke-0)*F,a.isToggleKnob.trackColorOff&&a.isToggleKnob.trackColorOn&&a.isToggleKnob.trackW&&a.isToggleKnob.trackH){let Pe=this.toggleStates.get(a.isToggleKnob.groupId),ce=Pe?Pe.fraction:0,ie=a.isToggleKnob.trackColorOff,ge=a.isToggleKnob.trackColorOn;O=ie[0]+(ge[0]-ie[0])*ce,H=ie[1]+(ge[1]-ie[1])*ce,_=ie[2]+(ge[2]-ie[2])*ce,T=ie[3]+(ge[3]-ie[3])*ce;let ne=(o+l/2)*this.dpr,G=(r+s/2)*this.dpr,V=a.isToggleKnob.trackOriginalX??a.rect.x,oe=a.isToggleKnob.trackOriginalY??a.rect.y,Ue=(V+a.isToggleKnob.trackW/2)*this.dpr,Te=(oe+a.isToggleKnob.trackH/2)*this.dpr,de=2/3+(Z-2/3)*F,ae=0+(ke-0)*F;E=ne+(Ue-ne)*de,m=G+(Te-G)*ae;let xe=a.isToggleKnob.trackW*this.dpr,me=a.isToggleKnob.trackH*this.dpr;if(re=xe*de*.5,K=me*ae*.5,$=me*.5*Math.min(de,ae),M=1,a.isToggleKnob.solidBackdropColor){let pe=a.isToggleKnob.solidBackdropColor;I=pe[0],y=pe[1],S=pe[2],v=pe[3],b=1}C=1,R=1}}let W=0,x=0,P=0,L=0,A=0,k=0,w=0,X=0,Q=0,fe=0;if(a.isBottomTabIndicator){let F=h;if(u=a.refractionHeight*F,d=a.refractionAmount*F,g=(a.highlight?.alpha??0)*F,a.isBottomTabIndicator.accentColor&&a.isBottomTabIndicator.containerRect){let J=a.isBottomTabIndicator.accentColor,Z=a.isBottomTabIndicator.containerRect;w=J[0],X=J[1],Q=J[2],fe=1,x=(Z.x+Z.w/2)*this.dpr,P=(Z.y+Z.h/2)*this.dpr,L=Z.w/2*this.dpr,A=Z.h/2*this.dpr,k=Z.h/2*this.dpr,W=1}}if(t.uniform1f(this.uEl.uUseToggleBackdrop,M),t.uniform1f(this.uEl.uUseSolidBackdrop,b),t.uniform4f(this.uEl.uSolidBackdropColor,I,y,S,v),t.uniform4f(this.uEl.uTrackColor,O,H,_,T),t.uniform4f(this.uEl.uTrackRect,E,m,re,K),t.uniform1f(this.uEl.uTrackCornerRadius,$),t.uniform1f(this.uEl.uIndicatorBackdrop,W),t.uniform4f(this.uEl.uContainerRect,x,P,L,A),t.uniform1f(this.uEl.uContainerCornerRadius,k),t.uniform4f(this.uEl.uIndicatorAccent,w,X,Q,fe),t.uniform1f(this.uEl.uInsetPx,4*this.dpr),a.isBottomTabIndicator){let F=this.toggleStates.get(a.isBottomTabIndicator.groupId);t.uniform1f(this.uEl.uIndicatorPressProgress,F?F.pressProgress:0),t.uniform1f(this.uEl.uIndicatorPanelOffset,F?F.panelOffset*this.dpr:0),t.uniform1f(this.uEl.uDpr,this.dpr);let J=a.isBottomTabIndicator.containerCenterX??0,Z=a.isBottomTabIndicator.containerCenterY??0,ke=a.isBottomTabIndicator.containerWidth??a.rect.w,Pe=F?1+16*1/ke*F.pressProgress:1;t.uniform2f(this.uEl.uContainerCenter,J*this.dpr,Z*this.dpr),t.uniform1f(this.uEl.uContainerScale,Pe);let ce=a.isBottomTabIndicator.tabContentIds??[],ie=a.isBottomTabIndicator.tabContentRects??[],ge=Math.min(ce.length,ie.length,8),ne=0;for(let G=0;G<8;G++)if(G<ge){let V=this.fgTextures.get(ce[G]);if(V){t.activeTexture(t.TEXTURE3+ne),t.bindTexture(t.TEXTURE_2D,V),t.uniform1i(this.uEl[`uTabContentTex${ne}`],3+ne);let oe=ie[G];t.uniform4f(this.uEl[`uTabContentRects[${ne}]`],(oe.x+oe.w/2)*this.dpr,(oe.y+oe.h/2)*this.dpr,oe.w/2*this.dpr,oe.h/2*this.dpr),ne++}}for(let G=ne;G<8;G++)t.uniform4f(this.uEl[`uTabContentRects[${G}]`],0,0,0,0);t.uniform1f(this.uEl.uTabContentCount,ne),this.tabsBackdropTex&&(t.activeTexture(t.TEXTURE11),t.bindTexture(t.TEXTURE_2D,this.tabsBackdropTex),t.uniform1i(this.uEl.uTabsGlassLayer,11));{let G=2*L,V=2*A,oe=k,Ue=Math.min(.5*this.dpr,Math.min(G,V)*.5),Te=Math.max(1,Math.ceil(Ue)*2),de=Math.max(0,.25*this.dpr),ae=Math.ceil(Te)+4,xe=Math.max(1,Math.ceil(G+2*ae)),me=Math.max(1,Math.ceil(V+2*ae)),pe=window.devicePixelRatio||1,Ce=Math.min(2,Math.max(1,Math.floor(pe/this.dpr))),pt=xe*Ce,bt=me*Ce,Ne=["inner-rr",G.toFixed(3),V.toFixed(3),oe.toFixed(3),Te,de.toFixed(3),ae,xe,me,`ss${Ce}`].join(":"),se=this.strokeMaskCache.get(Ne);if(!se){let z=document.createElement("canvas");z.width=pt,z.height=bt;let Y=z.getContext("2d",{alpha:!0});if(!Y)throw new Error("2D canvas not supported");let q=t.createTexture();if(!q)throw new Error("WebGL texture allocation failed");if(se={tex:q,canvas:z,ctx:Y,w:xe,h:me,ready:!1},this.strokeMaskCache.set(Ne,se),this.strokeMaskCache.size>32){let we=this.strokeMaskCache.keys().next().value;if(we&&we!==Ne){let St=this.strokeMaskCache.get(we);St&&t.deleteTexture(St.tex),this.strokeMaskCache.delete(we)}}}if(!se.ready){let z=se.ctx;z.clearRect(0,0,pt,bt),z.save(),z.scale(Ce,Ce),z.translate(ae,ae);let Y=Math.min(oe,G/2,V/2),q=new Path2D;q.moveTo(Y,0),q.lineTo(G-Y,0),q.arcTo(G,0,G,Y,Y),q.lineTo(G,V-Y),q.arcTo(G,V,G-Y,V,Y),q.lineTo(Y,V),q.arcTo(0,V,0,V-Y,Y),q.lineTo(0,Y),q.arcTo(0,0,Y,0,Y),q.closePath(),z.clip(q),z.lineWidth=Te,z.strokeStyle="rgba(255,255,255,1)",z.lineJoin="round",z.lineCap="round",z.filter=de>.01?`blur(${de}px)`:"none",z.stroke(q),z.filter="none",z.restore(),t.bindTexture(t.TEXTURE_2D,se.tex),t.pixelStorei(t.UNPACK_FLIP_Y_WEBGL,!1),t.texImage2D(t.TEXTURE_2D,0,t.RGBA,t.RGBA,t.UNSIGNED_BYTE,se.canvas),t.texParameteri(t.TEXTURE_2D,t.TEXTURE_MIN_FILTER,t.LINEAR),t.texParameteri(t.TEXTURE_2D,t.TEXTURE_MAG_FILTER,t.LINEAR),t.texParameteri(t.TEXTURE_2D,t.TEXTURE_WRAP_S,t.CLAMP_TO_EDGE),t.texParameteri(t.TEXTURE_2D,t.TEXTURE_WRAP_T,t.CLAMP_TO_EDGE),se.ready=!0}t.activeTexture(t.TEXTURE12),t.bindTexture(t.TEXTURE_2D,se.tex),t.uniform1i(this.uEl.uInnerStrokeMask,12),t.uniform2f(this.uEl.uInnerStrokeMaskOffset,ae,ae),t.uniform2f(this.uEl.uInnerStrokeMaskSize,se.w,se.h)}}else t.uniform1f(this.uEl.uIndicatorPressProgress,0),t.uniform1f(this.uEl.uIndicatorPanelOffset,0),t.uniform1f(this.uEl.uDpr,this.dpr),t.uniform2f(this.uEl.uContainerCenter,0,0),t.uniform1f(this.uEl.uContainerScale,1),t.uniform1f(this.uEl.uTabContentCount,0),t.uniform2f(this.uEl.uInnerStrokeMaskOffset,1,1),t.uniform2f(this.uEl.uInnerStrokeMaskSize,1,1);t.uniform1f(this.uEl.uRefractionHeight,u*this.dpr),t.uniform1f(this.uEl.uRefractionAmount,d*this.dpr),t.uniform1f(this.uEl.uDepthEffect,a.depthEffect?1:0),t.uniform1f(this.uEl.uChromaticAberration,a.chromaticAberration?1:0);let Le=a.useSeparableBlur&&a.blurRadius>=.5?0:f;if(t.uniform1f(this.uEl.uBlurRadius,Le*c*this.dpr),t.uniform1f(this.uEl.uSaturation,a.saturation),t.uniform1f(this.uEl.uBrightness,a.brightness),t.uniform1f(this.uEl.uContrast,a.contrast),t.uniform1f(this.uEl.uContentScaleX,C),t.uniform1f(this.uEl.uContentScaleY,R),t.uniform4f(this.uEl.uTintColor,a.tintColor[0],a.tintColor[1],a.tintColor[2],a.tintColor[3]),t.uniform4f(this.uEl.uSurfaceColor,a.surfaceColor[0],a.surfaceColor[1],a.surfaceColor[2],p),a.highlight){t.uniform3f(this.uEl.uHighlightColor,a.highlight.color[0],a.highlight.color[1],a.highlight.color[2]),t.uniform1f(this.uEl.uHighlightAngle,a.highlight.angle),t.uniform1f(this.uEl.uHighlightFalloff,a.highlight.falloff),t.uniform1f(this.uEl.uHighlightAlpha,g),t.uniform1f(this.uEl.uHighlightMode,a.highlight.mode);let F=Math.min(i.origW,i.origH)*this.dpr,J=Math.min(a.highlight.widthDp*this.dpr,F*.5),Z=(a.highlight.blurRadiusDp??a.highlight.widthDp/2)*this.dpr;t.uniform1f(this.uEl.uHighlightStrokeWidth,Math.ceil(J)*2),t.uniform1f(this.uEl.uHighlightBlur,Z)}else t.uniform1f(this.uEl.uHighlightAlpha,0),t.uniform1f(this.uEl.uHighlightMode,0),t.uniform1f(this.uEl.uHighlightStrokeWidth,0),t.uniform1f(this.uEl.uHighlightBlur,0);a.isSdfTexture&&this.sdfTexture?(t.activeTexture(t.TEXTURE2),t.bindTexture(t.TEXTURE_2D,this.sdfTexture),t.uniform1i(this.uEl.uSdfTexSampler,2),t.uniform1f(this.uEl.uUseSdfTexture,1),t.uniform2f(this.uEl.uSdfTexSize,this.sdfTextureSize[0],this.sdfTextureSize[1]),t.uniform1f(this.uEl.uSdfLightAngle,a.isSdfTexture.lightAngle),t.uniform1f(this.uEl.uRefractionHeight,a.isSdfTexture.refractionHeight*this.dpr)):t.uniform1f(this.uEl.uUseSdfTexture,0),a.useContinuousSdf&&this.continuousSdfTexture?(t.activeTexture(t.TEXTURE2),t.bindTexture(t.TEXTURE_2D,this.continuousSdfTexture),t.uniform1i(this.uEl.uContinuousSdf,2),t.uniform1f(this.uEl.uUseContinuousSdf,1),t.uniform2f(this.uEl.uContinuousSdfTexSize,this.continuousSdfTexSize[0],this.continuousSdfTexSize[1]),t.uniform2f(this.uEl.uContinuousSdfElementSize,i.origW*this.dpr,i.origH*this.dpr)):t.uniform1f(this.uEl.uUseContinuousSdf,0),t.uniform1f(this.uEl.uEnterAlpha,i.enterAlpha),t.uniform1f(this.uEl.uCornerStyle,this.cornerStyle),a.isMagnifier?(t.uniform1f(this.uEl.uUseMagnifier,1),t.uniform1f(this.uEl.uMagnifierZoom,a.isMagnifier.zoom),t.uniform1f(this.uEl.uMagnifierOffsetY,a.isMagnifier.sampleOffsetY*this.dpr)):t.uniform1f(this.uEl.uUseMagnifier,0),t.uniform1f(this.uEl.uSkipColorControls,a.backdropFbo&&a.useSeparableBlur&&a.blurRadius>=.5?1:0),t.drawArrays(t.TRIANGLES,0,6),i.elHighlightAlpha=g}};function tr(i,e,t,a){if(a){let l=new OffscreenCanvas(1,1).getContext("2d");return be(l,i,e,t)}let o=new Path2D;if(typeof o.roundRect=="function")o.roundRect(0,0,i,e,t);else{let r=Math.min(t,i/2,e/2);o.moveTo(r,0),o.lineTo(i-r,0),o.arcTo(i,0,i,r,r),o.lineTo(i,e-r),o.arcTo(i,e,i-r,e,r),o.lineTo(r,e),o.arcTo(0,e,0,e-r,r),o.lineTo(0,r),o.arcTo(0,0,r,0,r),o.closePath()}return o}function Ht(i,e){let t=new OffscreenCanvas(i,e),a=t.getContext("2d",{alpha:!0});return{canvas:t,ctx:a}}function zt(i){let{w:e,h:t,radius:a,offsetX:o,offsetY:r,blurSigma:l,margin:s,useG2:n,supersample:h}=i,c=Math.max(1,Math.ceil(e+2*s)),u=Math.max(1,Math.ceil(t+2*s)),d=c*h,f=u*h,{canvas:g,ctx:p}=Ht(d,f),{canvas:C,ctx:R}=Ht(d,f);p.save(),p.scale(h,h),p.translate(s,s);let M=tr(e,t,a,n);return p.clip(M),p.globalCompositeOperation="source-over",p.fillStyle="white",p.fill(M),p.globalCompositeOperation="destination-out",p.save(),p.translate(o,r),p.fill(M),p.restore(),p.globalCompositeOperation="source-over",p.restore(),l>.01?R.filter=`blur(${l*h}px)`:R.filter="none",R.drawImage(g,0,0),R.filter="none",{canvas:C,maskW:c,maskH:u,margin:s}}var Ut={renderGlassPostPasses(i){let e=this.gl,{el:t,st:a,isButton:o,p:r,sx:l,sy:s,sw:n,sh:h,radii:c,togglePressProgress:u,elHighlightAlpha:d}=i,f=i.origW*this.dpr,g=i.origH*this.dpr,p=i.origCornerRadius*this.dpr,C=i.layerScaleX,R=i.layerScaleY,M=(y,S)=>{let v=t.isToggleKnob||t.isBottomTabIndicator?u:1,O=y.alpha*v*i.enterAlpha,H=y.radius*v,_=y.offsetX*v,T=y.offsetY*v;if(O<=.001||H<=.5)return;let E=H*this.dpr,m=Math.ceil(E*3)+2,re=Math.max(1,Math.ceil(f+2*m)),K=Math.max(1,Math.ceil(g+2*m)),$=window.devicePixelRatio||1,W=Math.min(2,Math.max(1,Math.floor($/this.dpr))),x=!!t.useContinuousSdf,P=_*this.dpr,L=T*this.dpr,A={w:f,h:g,radius:p,offsetX:P,offsetY:L,blurSigma:E,margin:m,useG2:x,supersample:W},k=Ct(S,A),w=Rt(this.innerShadowMaskCache,e,k,re,K);if(!w.ready){let Q=zt(A);Et(e,w,Q)}e.enable(e.BLEND),e.blendFunc(e.ONE,e.ONE_MINUS_SRC_ALPHA),e.useProgram(this.innerShadowMaskCompositeProgram),e.bindBuffer(e.ARRAY_BUFFER,this.quadBuffer),e.enableVertexAttribArray(this.aPosLocIs),e.vertexAttribPointer(this.aPosLocIs,2,e.FLOAT,!1,0,0),e.uniform2f(this.uIs.uCanvasSize,this.canvas.width,this.canvas.height),e.uniform2f(this.uIs.uOffset,l*this.dpr,s*this.dpr),e.uniform2f(this.uIs.uSize,n*this.dpr,h*this.dpr),e.uniform4f(this.uIs.uCornerRadii,c[0]*this.dpr,c[1]*this.dpr,c[2]*this.dpr,c[3]*this.dpr),e.activeTexture(e.TEXTURE0),e.bindTexture(e.TEXTURE_2D,w.tex),e.uniform1i(this.uIs.uInnerShadowMask,0),e.uniform2f(this.uIs.uMaskOffset,m,m),e.uniform2f(this.uIs.uMaskSize,w.w,w.h);let X=y.color??[0,0,0];e.uniform3f(this.uIs.uInnerShadowColor,X[0],X[1],X[2]),e.uniform1f(this.uIs.uInnerShadowAlpha,O),e.uniform2f(this.uIs.uOriginalSize,f,g),e.uniform1f(this.uIs.uOriginalCornerRadius,p),e.uniform2f(this.uIs.uLayerScale,C,R),e.uniform1f(this.uIs.uElementRotation,i.elementRotation),e.drawArrays(e.TRIANGLES,0,6)};t.innerShadow&&M(t.innerShadow,0);let b=!!t.isBottomTabContainer,I=o?r:b?u:0;if(o&&t.isInteractive&&a&&r>.001||b&&u>.001){e.useProgram(this.tintProgram),e.bindBuffer(e.ARRAY_BUFFER,this.quadBuffer),e.enableVertexAttribArray(this.aPosLocTn),e.vertexAttribPointer(this.aPosLocTn,2,e.FLOAT,!1,0,0),e.blendFunc(e.SRC_ALPHA,e.ONE),e.uniform2f(this.uTn.uCanvasSize,this.canvas.width,this.canvas.height),e.uniform2f(this.uTn.uOffset,l*this.dpr,s*this.dpr),e.uniform2f(this.uTn.uSize,n*this.dpr,h*this.dpr),e.uniform4f(this.uTn.uCornerRadii,c[0]*this.dpr,c[1]*this.dpr,c[2]*this.dpr,c[3]*this.dpr),e.uniform2f(this.uTn.uOriginalSize,f,g),e.uniform1f(this.uTn.uOriginalCornerRadius,p),e.uniform2f(this.uTn.uLayerScale,C,R),e.uniform1f(this.uTn.uElementRotation,i.elementRotation),e.uniform1f(this.uTn.uCornerStyle,this.cornerStyle),e.uniform4f(this.uTn.uColor,1,1,1,.08*I),e.drawArrays(e.TRIANGLES,0,6),e.useProgram(this.highlightProgram),e.bindBuffer(e.ARRAY_BUFFER,this.quadBuffer),e.enableVertexAttribArray(this.aPosLocHl),e.vertexAttribPointer(this.aPosLocHl,2,e.FLOAT,!1,0,0),e.blendFunc(e.ONE,e.ONE),e.uniform2f(this.uHl.uCanvasSize,this.canvas.width,this.canvas.height),e.uniform2f(this.uHl.uOffset,l*this.dpr,s*this.dpr),e.uniform2f(this.uHl.uSize,n*this.dpr,h*this.dpr),e.uniform4f(this.uHl.uCornerRadii,c[0]*this.dpr,c[1]*this.dpr,c[2]*this.dpr,c[3]*this.dpr),e.uniform2f(this.uHl.uOriginalSize,f,g),e.uniform1f(this.uHl.uOriginalCornerRadius,p),e.uniform2f(this.uHl.uLayerScale,C,R),e.uniform1f(this.uHl.uElementRotation,i.elementRotation),e.uniform1f(this.uHl.uCornerStyle,this.cornerStyle),e.uniform4f(this.uHl.uColor,1,1,1,.15*I);let y=Math.min(n,h)*this.dpr;e.uniform1f(this.uHl.uRadius,y*1.5);let S,v;if(b){let O=this.toggleStates.get(t.isBottomTabContainer.groupId),H=t.isBottomTabContainer.tabsCount??4,_=t.rect.w/H,E=((O?O.fraction:0)+.5)*_,m=n/t.rect.w;S=Math.max(0,Math.min(n,E*m))*this.dpr,v=h/2*this.dpr}else S=Math.max(0,Math.min(n,a.dragX*i.layerScaleX))*this.dpr,v=Math.max(0,Math.min(h,a.dragY*i.layerScaleY))*this.dpr;e.uniform2f(this.uHl.uPosition,S,v),e.drawArrays(e.TRIANGLES,0,6),e.blendFunc(e.SRC_ALPHA,e.ONE_MINUS_SRC_ALPHA)}if(t.isToggleKnob&&u<.999){let y=1*(1-u);e.useProgram(this.tintProgram),e.bindBuffer(e.ARRAY_BUFFER,this.quadBuffer),e.enableVertexAttribArray(this.aPosLocTn),e.vertexAttribPointer(this.aPosLocTn,2,e.FLOAT,!1,0,0),e.blendFunc(e.SRC_ALPHA,e.ONE_MINUS_SRC_ALPHA),e.uniform2f(this.uTn.uCanvasSize,this.canvas.width,this.canvas.height),e.uniform2f(this.uTn.uOffset,l*this.dpr,s*this.dpr),e.uniform2f(this.uTn.uSize,n*this.dpr,h*this.dpr),e.uniform4f(this.uTn.uCornerRadii,c[0]*this.dpr,c[1]*this.dpr,c[2]*this.dpr,c[3]*this.dpr),e.uniform2f(this.uTn.uOriginalSize,f,g),e.uniform1f(this.uTn.uOriginalCornerRadius,p),e.uniform2f(this.uTn.uLayerScale,C,R),e.uniform1f(this.uTn.uElementRotation,i.elementRotation),e.uniform1f(this.uTn.uCornerStyle,this.cornerStyle),e.uniform4f(this.uTn.uColor,1,1,1,y),e.drawArrays(e.TRIANGLES,0,6)}if(t.isBottomTabIndicator&&t.isBottomTabIndicator.dimColor){let y=t.isBottomTabIndicator.dimColor,S=u;e.useProgram(this.tintProgram),e.bindBuffer(e.ARRAY_BUFFER,this.quadBuffer),e.enableVertexAttribArray(this.aPosLocTn),e.vertexAttribPointer(this.aPosLocTn,2,e.FLOAT,!1,0,0),e.blendFunc(e.SRC_ALPHA,e.ONE_MINUS_SRC_ALPHA),e.uniform2f(this.uTn.uCanvasSize,this.canvas.width,this.canvas.height),e.uniform2f(this.uTn.uOffset,l*this.dpr,s*this.dpr),e.uniform2f(this.uTn.uSize,n*this.dpr,h*this.dpr),e.uniform4f(this.uTn.uCornerRadii,c[0]*this.dpr,c[1]*this.dpr,c[2]*this.dpr,c[3]*this.dpr),e.uniform2f(this.uTn.uOriginalSize,f,g),e.uniform1f(this.uTn.uOriginalCornerRadius,p),e.uniform2f(this.uTn.uLayerScale,C,R),e.uniform1f(this.uTn.uElementRotation,i.elementRotation),e.uniform1f(this.uTn.uCornerStyle,this.cornerStyle),e.uniform4f(this.uTn.uColor,y[0],y[1],y[2],.1*(1-S)),e.drawArrays(e.TRIANGLES,0,6),e.uniform4f(this.uTn.uColor,0,0,0,.03*S),e.drawArrays(e.TRIANGLES,0,6),e.blendFunc(e.SRC_ALPHA,e.ONE_MINUS_SRC_ALPHA)}if(o&&(t.label||t.icon)){let y=this.fgTextures.get(t.id);y&&(e.useProgram(this.foregroundProgram),e.bindBuffer(e.ARRAY_BUFFER,this.quadBuffer),e.enableVertexAttribArray(this.aPosLocFg),e.vertexAttribPointer(this.aPosLocFg,2,e.FLOAT,!1,0,0),e.blendFunc(e.ONE,e.ONE_MINUS_SRC_ALPHA),e.activeTexture(e.TEXTURE0),e.bindTexture(e.TEXTURE_2D,y),e.uniform1i(this.uFg.uTexture,0),e.uniform2f(this.uFg.uCanvasSize,this.canvas.width,this.canvas.height),e.uniform2f(this.uFg.uOffset,l*this.dpr,s*this.dpr),e.uniform2f(this.uFg.uSize,n*this.dpr,h*this.dpr),e.uniform4f(this.uFg.uCornerRadii,c[0]*this.dpr,c[1]*this.dpr,c[2]*this.dpr,c[3]*this.dpr),e.uniform2f(this.uFg.uOriginalSize,f,g),e.uniform1f(this.uFg.uOriginalCornerRadius,p),e.uniform2f(this.uFg.uLayerScale,C,R),e.uniform1f(this.uFg.uCornerStyle,this.cornerStyle),t.useContinuousSdf&&this.continuousSdfTexture?(e.activeTexture(e.TEXTURE2),e.bindTexture(e.TEXTURE_2D,this.continuousSdfTexture),e.uniform1i(this.uFg.uContinuousSdf,2),e.uniform1f(this.uFg.uUseContinuousSdf,1),e.uniform2f(this.uFg.uContinuousSdfTexSize,this.continuousSdfTexSize[0],this.continuousSdfTexSize[1]),e.uniform2f(this.uFg.uContinuousSdfElementSize,i.origW*this.dpr,i.origH*this.dpr)):e.uniform1f(this.uFg.uUseContinuousSdf,0),e.uniform1f(this.uFg.uAlpha,1-.15*r),e.drawArrays(e.TRIANGLES,0,6),e.blendFunc(e.SRC_ALPHA,e.ONE_MINUS_SRC_ALPHA))}if(t.highlight&&t.highlight.alpha>.001){let y=t.isToggleKnob||t.isBottomTabIndicator?d:t.highlight.alpha,S=t.highlight.mode===1?.38:1,v=y*i.enterAlpha*S;if(v>.001){let O=Math.min(t.highlight.widthDp*this.dpr,Math.min(f,g)*.5),H=Math.max(1,Math.ceil(O)*2),_=Math.max(0,(t.highlight.blurRadiusDp??t.highlight.widthDp/2)*this.dpr),T=Math.ceil(H)+4,E=Math.max(1,Math.ceil(f+2*T)),m=Math.max(1,Math.ceil(g+2*T)),re=window.devicePixelRatio||1,K=Math.min(2,Math.max(1,Math.floor(re/this.dpr))),$=E*K,W=m*K,x=!!t.useContinuousSdf,P=[x?"g2":"rr",f.toFixed(3),g.toFixed(3),p.toFixed(3),H,_.toFixed(3),T,E,m,`ss${K}`].join(":"),L=this.strokeMaskCache.get(P);if(!L){let A=document.createElement("canvas");A.width=$,A.height=W;let k=A.getContext("2d",{alpha:!0});if(!k)throw new Error("2D canvas not supported");let w=e.createTexture();if(!w)throw new Error("WebGL texture allocation failed");if(L={tex:w,canvas:A,ctx:k,w:E,h:m,ready:!1},this.strokeMaskCache.set(P,L),this.strokeMaskCache.size>32){let X=this.strokeMaskCache.keys().next().value;if(X&&X!==P){let Q=this.strokeMaskCache.get(X);Q&&e.deleteTexture(Q.tex),this.strokeMaskCache.delete(X)}}}if(!L.ready){let A=L.ctx;A.clearRect(0,0,$,W),A.save(),A.scale(K,K),A.translate(T,T);let k;if(x)k=be(A,f,g,p);else{k=new Path2D;let w=Math.min(p,f/2,g/2);k.moveTo(w,0),k.lineTo(f-w,0),k.arcTo(f,0,f,w,w),k.lineTo(f,g-w),k.arcTo(f,g,f-w,g,w),k.lineTo(w,g),k.arcTo(0,g,0,g-w,w),k.lineTo(0,w),k.arcTo(0,0,w,0,w),k.closePath()}A.clip(k),A.lineWidth=H,A.strokeStyle="rgba(255,255,255,1)",A.lineJoin="round",A.lineCap="round",A.filter=_>.01?`blur(${_}px)`:"none",A.stroke(k),A.filter="none",A.restore(),e.bindTexture(e.TEXTURE_2D,L.tex),e.pixelStorei(e.UNPACK_FLIP_Y_WEBGL,!1),e.texImage2D(e.TEXTURE_2D,0,e.RGBA,e.RGBA,e.UNSIGNED_BYTE,L.canvas),e.texParameteri(e.TEXTURE_2D,e.TEXTURE_MIN_FILTER,e.LINEAR),e.texParameteri(e.TEXTURE_2D,e.TEXTURE_MAG_FILTER,e.LINEAR),e.texParameteri(e.TEXTURE_2D,e.TEXTURE_WRAP_S,e.CLAMP_TO_EDGE),e.texParameteri(e.TEXTURE_2D,e.TEXTURE_WRAP_T,e.CLAMP_TO_EDGE),L.ready=!0}e.enable(e.BLEND),t.highlight.mode===1?e.blendFunc(e.ONE,e.ONE_MINUS_SRC_ALPHA):e.blendFunc(e.ONE,e.ONE),e.useProgram(this.strokeMaskCompositeProgram),e.bindBuffer(e.ARRAY_BUFFER,this.quadBuffer),e.enableVertexAttribArray(this.aPosLocSm),e.vertexAttribPointer(this.aPosLocSm,2,e.FLOAT,!1,0,0),e.uniform2f(this.uSm.uCanvasSize,this.canvas.width,this.canvas.height),e.uniform2f(this.uSm.uOffset,l*this.dpr,s*this.dpr),e.uniform2f(this.uSm.uSize,n*this.dpr,h*this.dpr),e.uniform4f(this.uSm.uCornerRadii,c[0]*this.dpr,c[1]*this.dpr,c[2]*this.dpr,c[3]*this.dpr),e.activeTexture(e.TEXTURE0),e.bindTexture(e.TEXTURE_2D,L.tex),e.uniform1i(this.uSm.uStrokeMask,0),e.uniform2f(this.uSm.uMaskOffset,T,T),e.uniform2f(this.uSm.uMaskSize,L.w,L.h),e.uniform4f(this.uSm.uHighlightColor,t.highlight.color[0],t.highlight.color[1],t.highlight.color[2],1),e.uniform1f(this.uSm.uHighlightAngle,t.useGravityAngle?this.gravityAngle:t.highlight.angle),e.uniform1f(this.uSm.uHighlightFalloff,t.highlight.falloff),e.uniform1f(this.uSm.uHighlightAlpha,v),e.uniform1f(this.uSm.uHighlightMode,t.highlight.mode),e.uniform2f(this.uSm.uOriginalSize,f,g),e.uniform1f(this.uSm.uOriginalCornerRadius,p),e.uniform2f(this.uSm.uLayerScale,C,R),e.uniform1f(this.uSm.uElementRotation,i.elementRotation),e.drawArrays(e.TRIANGLES,0,6),e.blendFunc(e.SRC_ALPHA,e.ONE_MINUS_SRC_ALPHA)}}}};var ue=class{constructor(e){this.wallpaperTexture=null;this.wallpaperReady=!1;this.wallpaperSize=[1,1];this.dpr=0;this.buttonConfigs=[];this.buttonStates=new Map;this.toggleStates=new Map;this.scrollY=0;this.scrollVelocity=0;this.contentHeight=0;this.cssWidth=0;this.cssHeight=0;this.wheelTarget=null;this.backgroundColor=null;this.needsRedraw=!0;this.fboA=null;this.fboATex=null;this.fboB=null;this.fboBTex=null;this.fboW=0;this.fboH=0;this.tabsBackdropFbo=null;this.tabsBackdropTex=null;this.tabsBackdropDirty=!0;this.gpElementFbo=null;this.gpElementTex=null;this.blurFboA=null;this.blurFboATex=null;this.blurFboB=null;this.blurFboBTex=null;this.highlightMaskFbo=null;this.highlightMaskTex=null;this.dialogBackdropFbo=null;this.dialogBackdropTex=null;this.dialogBackdropKey=null;this.blurPrograms=new Map;this.highlightBlurPrograms=new Map;this.gravityAngle=45*Math.PI/180;this.blurTapCap=17;this.blurDownsample=1;this.cornerStyle=1;this.sdfTexture=null;this.sdfTextureReady=!1;this.sdfTextureSize=[1,1];this.continuousSdfPool=new Map;this.continuousSdfTexture=null;this.continuousSdfTexSize=[256,256];this.continuousSdfKey=null;this.fgTextures=new Map;this.fgDirtyIds=new Set;this.strokeMaskCache=new Map;this.innerShadowMaskCache=new Map;this.rafId=null;this.animRafId=null;this.uEl={};this.uSh={};this.uWp={};this.uFg={};this.uHl={};this.uTn={};this.uRm={};this.uHs={};this.uHc={};this.uSm={};this.uIs={};this.uPr={};this.uPb={};this.uCp={};this.uSf={};this.uCc={};this.uSt={};this.canvas=e;let t=e.getContext("webgl",{premultipliedAlpha:!1,alpha:!1,antialias:!0,preserveDrawingBuffer:!1});if(!t)throw new Error("WebGL not supported");this.gl=t,this.elementProgram=N(t,D,We),this.shadowProgram=N(t,D,Xe),this.wallpaperProgram=N(t,D,Qe),this.foregroundProgram=N(t,D,it),this.highlightProgram=N(t,D,Ye),this.tintProgram=N(t,D,Ve),this.rimHighlightProgram=N(t,D,qe),this.highlightStrokeProgram=N(t,D,Ke),this.highlightCompositeProgram=N(t,D,$e),this.strokeMaskCompositeProgram=N(t,D,Ze),this.innerShadowMaskCompositeProgram=N(t,D,je),this.plainRectProgram=N(t,D,ot),this.progressiveBlurProgram=N(t,D,at),this.copyProgram=N(t,D,Je),this.solidFillProgram=N(t,D,et),this.colorControlsProgram=N(t,D,tt),this.sceneTintProgram=N(t,D,rt),this.quadBuffer=t.createBuffer(),t.bindBuffer(t.ARRAY_BUFFER,this.quadBuffer),t.bufferData(t.ARRAY_BUFFER,new Float32Array([-1,-1,1,-1,-1,1,-1,1,1,-1,1,1]),t.STATIC_DRAW),this.aPosLocEl=t.getAttribLocation(this.elementProgram,"aPos"),this.aPosLocSh=t.getAttribLocation(this.shadowProgram,"aPos"),this.aPosLocWp=t.getAttribLocation(this.wallpaperProgram,"aPos"),this.aPosLocFg=t.getAttribLocation(this.foregroundProgram,"aPos"),this.aPosLocHl=t.getAttribLocation(this.highlightProgram,"aPos"),this.aPosLocTn=t.getAttribLocation(this.tintProgram,"aPos"),this.aPosLocRm=t.getAttribLocation(this.rimHighlightProgram,"aPos"),this.aPosLocHs=t.getAttribLocation(this.highlightStrokeProgram,"aPos"),this.aPosLocHc=t.getAttribLocation(this.highlightCompositeProgram,"aPos"),this.aPosLocSm=t.getAttribLocation(this.strokeMaskCompositeProgram,"aPos"),this.aPosLocIs=t.getAttribLocation(this.innerShadowMaskCompositeProgram,"aPos"),this.aPosLocPr=t.getAttribLocation(this.plainRectProgram,"aPos"),this.aPosLocPb=t.getAttribLocation(this.progressiveBlurProgram,"aPos"),this.aPosLocCp=t.getAttribLocation(this.copyProgram,"aPos"),this.aPosLocSf=t.getAttribLocation(this.solidFillProgram,"aPos"),this.aPosLocCc=t.getAttribLocation(this.colorControlsProgram,"aPos"),this.aPosLocSt=t.getAttribLocation(this.sceneTintProgram,"aPos"),this.fgCanvas=typeof document<"u"?document.createElement("canvas"):null;let a=this.fgCanvas?.getContext("2d",{alpha:!0});if(!a)throw new Error("2D canvas not supported");this.fgCtx=a,this.cacheUniforms()}static{this.TAB_PRESSED_SCALE=78/56}cacheUniforms(){let e=this.gl,t=["uBackdrop","uWallpaperSampler","uTabsBackdropSampler","uCanvasSize","uWallpaperSize","uElementOffset","uElementSize","uCornerRadii","uRefractionHeight","uRefractionAmount","uDepthEffect","uChromaticAberration","uBlurRadius","uSaturation","uBrightness","uContrast","uTintColor","uSurfaceColor","uHighlightColor","uHighlightAngle","uHighlightFalloff","uHighlightAlpha","uHighlightMode","uHighlightStrokeWidth","uHighlightBlur","uContentScaleX","uContentScaleY","uUseToggleBackdrop","uUseSolidBackdrop","uSolidBackdropColor","uTrackColor","uTrackRect","uTrackCornerRadius","uOriginalSize","uOriginalCornerRadius","uLayerScale","uIndicatorBackdrop","uContainerRect","uContainerCornerRadius","uIndicatorAccent","uInsetPx","uIndicatorPressProgress","uIndicatorPanelOffset","uDpr","uContainerCenter","uContainerScale","uTabContentTex0","uTabContentTex1","uTabContentTex2","uTabContentTex3","uTabContentTex4","uTabContentTex5","uTabContentTex6","uTabContentTex7","uTabContentRects[0]","uTabContentRects[1]","uTabContentRects[2]","uTabContentRects[3]","uTabContentRects[4]","uTabContentRects[5]","uTabContentRects[6]","uTabContentRects[7]","uTabContentCount","uTabsGlassLayer","uSdfTexSampler","uUseSdfTexture","uSdfTexSize","uSdfLightAngle","uEnterAlpha","uCornerStyle","uSkipColorControls","uUseMagnifier","uMagnifierZoom","uMagnifierOffsetY","uElementRotation","uContinuousSdf","uUseContinuousSdf","uContinuousSdfTexSize","uContinuousSdfElementSize","uInnerStrokeMask","uInnerStrokeMaskOffset","uInnerStrokeMaskSize"];for(let b of t)this.uEl[b]=e.getUniformLocation(this.elementProgram,b);let a=["uCanvasSize","uElementOffset","uElementSize","uCornerRadii","uShadowRadius","uShadowOffset","uShadowColor","uOriginalSize","uOriginalCornerRadius","uLayerScale","uElementRotation","uCornerStyle"];for(let b of a)this.uSh[b]=e.getUniformLocation(this.shadowProgram,b);let o=["uBackdrop","uCanvasSize","uWallpaperSize"];for(let b of o)this.uWp[b]=e.getUniformLocation(this.wallpaperProgram,b);let r=["uTexture","uCanvasSize","uOffset","uSize","uCornerRadii","uAlpha","uOriginalSize","uOriginalCornerRadius","uLayerScale","uCornerStyle","uUseContinuousSdf","uContinuousSdf","uContinuousSdfTexSize","uContinuousSdfElementSize"];for(let b of r)this.uFg[b]=e.getUniformLocation(this.foregroundProgram,b);let l=["uCanvasSize","uOffset","uSize","uCornerRadii","uColor","uRadius","uPosition","uOriginalSize","uOriginalCornerRadius","uLayerScale","uElementRotation","uCornerStyle"];for(let b of l)this.uHl[b]=e.getUniformLocation(this.highlightProgram,b);let s=["uCanvasSize","uOffset","uSize","uCornerRadii","uColor","uOriginalSize","uOriginalCornerRadius","uLayerScale","uElementRotation","uCornerStyle"];for(let b of s)this.uTn[b]=e.getUniformLocation(this.tintProgram,b);let n=["uCanvasSize","uOffset","uSize","uCornerRadii","uHighlightColor","uHighlightAngle","uHighlightFalloff","uHighlightAlpha","uHighlightMode","uHighlightStrokeWidth","uHighlightBlur","uOriginalSize","uOriginalCornerRadius","uLayerScale","uElementRotation","uCornerStyle","uUseContinuousSdf","uContinuousSdf","uContinuousSdfTexSize","uContinuousSdfElementSize"];for(let b of n)this.uRm[b]=e.getUniformLocation(this.rimHighlightProgram,b);let h=["uCanvasSize","uOffset","uSize","uCornerRadii","uHighlightStrokeWidth","uOriginalSize","uOriginalCornerRadius","uLayerScale","uElementRotation","uCornerStyle","uUseContinuousSdf","uContinuousSdf","uContinuousSdfTexSize","uContinuousSdfElementSize"];for(let b of h)this.uHs[b]=e.getUniformLocation(this.highlightStrokeProgram,b);let c=["uCanvasSize","uOffset","uSize","uCornerRadii","uBlurredMask","uMaskTexSize","uHighlightColor","uHighlightAngle","uHighlightFalloff","uHighlightAlpha","uHighlightMode","uOriginalSize","uOriginalCornerRadius","uLayerScale","uElementRotation","uCornerStyle","uUseContinuousSdf","uContinuousSdf","uContinuousSdfTexSize","uContinuousSdfElementSize"];for(let b of c)this.uHc[b]=e.getUniformLocation(this.highlightCompositeProgram,b);let u=["uCanvasSize","uOffset","uSize","uCornerRadii","uStrokeMask","uMaskOffset","uMaskSize","uHighlightColor","uHighlightAngle","uHighlightFalloff","uHighlightAlpha","uHighlightMode","uOriginalSize","uOriginalCornerRadius","uLayerScale","uElementRotation"];for(let b of u)this.uSm[b]=e.getUniformLocation(this.strokeMaskCompositeProgram,b);let d=["uCanvasSize","uOffset","uSize","uCornerRadii","uInnerShadowMask","uMaskOffset","uMaskSize","uInnerShadowColor","uInnerShadowAlpha","uOriginalSize","uOriginalCornerRadius","uLayerScale","uElementRotation"];for(let b of d)this.uIs[b]=e.getUniformLocation(this.innerShadowMaskCompositeProgram,b);let f=["uCanvasSize","uOffset","uSize","uCornerRadii","uColor","uCornerStyle","uUseContinuousSdf","uContinuousSdf","uContinuousSdfTexSize","uContinuousSdfElementSize"];for(let b of f)this.uPr[b]=e.getUniformLocation(this.plainRectProgram,b);let g=["uBackdrop","uCanvasSize","uWallpaperSize","uOffset","uSize","uBlurRadius","uTintColor","uTintIntensity"];for(let b of g)this.uPb[b]=e.getUniformLocation(this.progressiveBlurProgram,b);let p=["uTexture","uCanvasSize"];for(let b of p)this.uCp[b]=e.getUniformLocation(this.copyProgram,b);let C=["uColor"];for(let b of C)this.uSf[b]=e.getUniformLocation(this.solidFillProgram,b);let R=["uTexture","uTexSize","uBrightness","uContrast","uSaturation"];for(let b of R)this.uCc[b]=e.getUniformLocation(this.colorControlsProgram,b);let M=["uTexture","uCanvasSize","uTintColor"];for(let b of M)this.uSt[b]=e.getUniformLocation(this.sceneTintProgram,b)}ensureBlurPrograms(e){if(this.blurPrograms.has(e))return;let t=this.gl,a=le(t,t.FRAGMENT_SHADER,Fe(e,"horizontal")),o=le(t,t.FRAGMENT_SHADER,Fe(e,"vertical")),r=c=>{let u=le(t,t.VERTEX_SHADER,D),d=t.createProgram();if(t.attachShader(d,u),t.attachShader(d,c),t.bindAttribLocation(d,0,"aPos"),t.linkProgram(d),t.deleteShader(u),t.deleteShader(c),!t.getProgramParameter(d,t.LINK_STATUS)){let f=t.getProgramInfoLog(d);throw t.deleteProgram(d),new Error("Blur program link error (taps="+e+"): "+f)}return d},l=r(a),s=r(o),n={uTexture:t.getUniformLocation(l,"uTexture"),uTexSize:t.getUniformLocation(l,"uTexSize"),uRadius:t.getUniformLocation(l,"uRadius")},h={uTexture:t.getUniformLocation(s,"uTexture"),uTexSize:t.getUniformLocation(s,"uTexSize"),uRadius:t.getUniformLocation(s,"uRadius")};this.blurPrograms.set(e,{hProg:l,vProg:s,uH:n,uV:h,aPosH:0,aPosV:0})}blurTexture(e,t){let a=this.gl,o=this.fboW,r=this.fboH,l=st(t);l=Math.min(l,Math.max(1,this.blurTapCap|0)),this.ensureBlurPrograms(l);let s=this.blurPrograms.get(l),n=a.getParameter(a.FRAMEBUFFER_BINDING);return a.disable(a.BLEND),a.bindFramebuffer(a.FRAMEBUFFER,this.blurFboA),a.viewport(0,0,o,r),a.useProgram(s.hProg),a.bindBuffer(a.ARRAY_BUFFER,this.quadBuffer),a.enableVertexAttribArray(s.aPosH),a.vertexAttribPointer(s.aPosH,2,a.FLOAT,!1,0,0),a.activeTexture(a.TEXTURE0),a.bindTexture(a.TEXTURE_2D,e),a.uniform1i(s.uH.uTexture,0),a.uniform2f(s.uH.uTexSize,o,r),a.uniform1f(s.uH.uRadius,t),a.drawArrays(a.TRIANGLES,0,6),a.bindFramebuffer(a.FRAMEBUFFER,this.blurFboB),a.viewport(0,0,o,r),a.useProgram(s.vProg),a.bindBuffer(a.ARRAY_BUFFER,this.quadBuffer),a.enableVertexAttribArray(s.aPosV),a.vertexAttribPointer(s.aPosV,2,a.FLOAT,!1,0,0),a.activeTexture(a.TEXTURE0),a.bindTexture(a.TEXTURE_2D,this.blurFboATex),a.uniform1i(s.uV.uTexture,0),a.uniform2f(s.uV.uTexSize,o,r),a.uniform1f(s.uV.uRadius,t),a.drawArrays(a.TRIANGLES,0,6),a.bindFramebuffer(a.FRAMEBUFFER,n),a.viewport(0,0,o,r),this.blurFboBTex}ensureHighlightBlurPrograms(e){if(this.highlightBlurPrograms.has(e))return;let t=this.gl,a=le(t,t.FRAGMENT_SHADER,_e(e,"horizontal")),o=le(t,t.FRAGMENT_SHADER,_e(e,"vertical")),r=c=>{let u=le(t,t.VERTEX_SHADER,D),d=t.createProgram();if(t.attachShader(d,u),t.attachShader(d,c),t.bindAttribLocation(d,0,"aPos"),t.linkProgram(d),t.deleteShader(u),t.deleteShader(c),!t.getProgramParameter(d,t.LINK_STATUS)){let f=t.getProgramInfoLog(d);throw t.deleteProgram(d),new Error("Highlight blur program link error (taps="+e+"): "+f)}return d},l=r(a),s=r(o),n={uTexture:t.getUniformLocation(l,"uTexture"),uTexSize:t.getUniformLocation(l,"uTexSize"),uRadius:t.getUniformLocation(l,"uRadius")},h={uTexture:t.getUniformLocation(s,"uTexture"),uTexSize:t.getUniformLocation(s,"uTexSize"),uRadius:t.getUniformLocation(s,"uRadius")};this.highlightBlurPrograms.set(e,{hProg:l,vProg:s,uH:n,uV:h,aPosH:0,aPosV:0})}blurHighlightMask(e,t){let a=this.gl,o=this.fboW,r=this.fboH,l=nt(t);l=Math.min(l,Math.max(3,this.blurTapCap|0)),this.ensureHighlightBlurPrograms(l);let s=this.highlightBlurPrograms.get(l),n=a.getParameter(a.FRAMEBUFFER_BINDING);return a.disable(a.BLEND),a.bindFramebuffer(a.FRAMEBUFFER,this.blurFboA),a.viewport(0,0,o,r),a.useProgram(s.hProg),a.bindBuffer(a.ARRAY_BUFFER,this.quadBuffer),a.enableVertexAttribArray(s.aPosH),a.vertexAttribPointer(s.aPosH,2,a.FLOAT,!1,0,0),a.activeTexture(a.TEXTURE0),a.bindTexture(a.TEXTURE_2D,e),a.uniform1i(s.uH.uTexture,0),a.uniform2f(s.uH.uTexSize,o,r),a.uniform1f(s.uH.uRadius,t),a.drawArrays(a.TRIANGLES,0,6),a.bindFramebuffer(a.FRAMEBUFFER,this.blurFboB),a.viewport(0,0,o,r),a.useProgram(s.vProg),a.bindBuffer(a.ARRAY_BUFFER,this.quadBuffer),a.enableVertexAttribArray(s.aPosV),a.vertexAttribPointer(s.aPosV,2,a.FLOAT,!1,0,0),a.activeTexture(a.TEXTURE0),a.bindTexture(a.TEXTURE_2D,this.blurFboATex),a.uniform1i(s.uV.uTexture,0),a.uniform2f(s.uV.uTexSize,o,r),a.uniform1f(s.uV.uRadius,t),a.drawArrays(a.TRIANGLES,0,6),a.bindFramebuffer(a.FRAMEBUFFER,n),a.viewport(0,0,o,r),this.blurFboBTex}dispose(){this.rafId!==null&&cancelAnimationFrame(this.rafId),this.rafId=null,this.animRafId!==null&&cancelAnimationFrame(this.animRafId),this.animRafId=null;let e=this.gl;this.wallpaperTexture&&e.deleteTexture(this.wallpaperTexture);for(let t of this.fgTextures.values())e.deleteTexture(t);this.fgTextures.clear();for(let t of this.strokeMaskCache.values())e.deleteTexture(t.tex);this.strokeMaskCache.clear(),At(e,this.innerShadowMaskCache),this.fboA&&e.deleteFramebuffer(this.fboA),this.fboATex&&e.deleteTexture(this.fboATex),this.fboB&&e.deleteFramebuffer(this.fboB),this.fboBTex&&e.deleteTexture(this.fboBTex),this.fboA=this.fboB=null,this.fboATex=this.fboBTex=null,this.tabsBackdropFbo&&e.deleteFramebuffer(this.tabsBackdropFbo),this.tabsBackdropTex&&e.deleteTexture(this.tabsBackdropTex),this.tabsBackdropFbo=null,this.tabsBackdropTex=null,this.gpElementFbo&&e.deleteFramebuffer(this.gpElementFbo),this.gpElementTex&&e.deleteTexture(this.gpElementTex),this.blurFboA&&e.deleteFramebuffer(this.blurFboA),this.blurFboATex&&e.deleteTexture(this.blurFboATex),this.blurFboB&&e.deleteFramebuffer(this.blurFboB),this.blurFboBTex&&e.deleteTexture(this.blurFboBTex),this.gpElementFbo=this.blurFboA=this.blurFboB=null,this.gpElementTex=this.blurFboATex=this.blurFboBTex=null,this.highlightMaskFbo&&e.deleteFramebuffer(this.highlightMaskFbo),this.highlightMaskTex&&e.deleteTexture(this.highlightMaskTex),this.highlightMaskFbo=null,this.highlightMaskTex=null,this.dialogBackdropFbo&&e.deleteFramebuffer(this.dialogBackdropFbo),this.dialogBackdropTex&&e.deleteTexture(this.dialogBackdropTex),this.dialogBackdropFbo=null,this.dialogBackdropTex=null,this.dialogBackdropKey=null;for(let{hProg:t,vProg:a}of this.blurPrograms.values())e.deleteProgram(t),e.deleteProgram(a);this.blurPrograms.clear();for(let{hProg:t,vProg:a}of this.highlightBlurPrograms.values())e.deleteProgram(t),e.deleteProgram(a);this.highlightBlurPrograms.clear(),this.sdfTexture&&e.deleteTexture(this.sdfTexture),this.sdfTexture=null;for(let{tex:t}of this.continuousSdfPool.values())e.deleteTexture(t);this.continuousSdfPool.clear(),this.continuousSdfTexture=null,this.continuousSdfKey=null,e.deleteProgram(this.elementProgram),e.deleteProgram(this.shadowProgram),e.deleteProgram(this.wallpaperProgram),e.deleteProgram(this.foregroundProgram),e.deleteProgram(this.highlightProgram),e.deleteProgram(this.tintProgram),e.deleteProgram(this.rimHighlightProgram),e.deleteProgram(this.highlightStrokeProgram),e.deleteProgram(this.highlightCompositeProgram),e.deleteProgram(this.strokeMaskCompositeProgram),e.deleteProgram(this.innerShadowMaskCompositeProgram),e.deleteProgram(this.plainRectProgram),e.deleteProgram(this.progressiveBlurProgram),e.deleteProgram(this.copyProgram),e.deleteProgram(this.solidFillProgram),e.deleteProgram(this.colorControlsProgram),e.deleteProgram(this.sceneTintProgram),e.deleteBuffer(this.quadBuffer)}};Object.assign(ue.prototype,yt,Pt,wt,Mt,Ft,_t,Ot,Gt,Dt,Bt,It,Ut);var Wt=document.querySelector("#liquid-glass");if(!Wt)throw new Error("Liquid Glass canvas was not found.");var ye=new ue(Wt),mt={mode:0,color:[1,1,1],angle:Math.PI/4,falloff:1,alpha:.62,widthDp:.7},ve=(i,e,t,a,o,r,l={})=>({id:i,kind:"glass-shape",rect:{x:e,y:t,w:Math.max(1,a),h:Math.max(1,o)},cornerRadius:r,refractionHeight:18,refractionAmount:-30,depthEffect:!0,chromaticAberration:!0,blurRadius:5,saturation:1.28,brightness:.015,contrast:1.04,tintColor:[.82,.91,1,.035],surfaceColor:[1,1,1,.11],highlight:{...mt},outerShadow:{radius:28,alpha:.14,offsetX:0,offsetY:6,color:[.09,.16,.3]},innerShadow:null,label:"",labelColor:[0,0,0,1],showChevron:!1,isInteractive:!1,scroll:!1,...l}),rr=()=>`data:image/svg+xml;charset=utf-8,${encodeURIComponent(`
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
  </svg>`)}`,ze=()=>{let i=Math.max(320,window.innerWidth),e=Math.max(240,window.innerHeight);ye.resize(i,e);let t=Math.min(410,Math.max(330,i*.245)),a=70,o=36,r=[ve("app-bar",12,10,i-24,a,22,{refractionHeight:22,refractionAmount:-34,blurRadius:7,surfaceColor:[1,1,1,.13]}),ve("tool-rail",12,90,74,e-144,22,{refractionHeight:16,refractionAmount:-26,blurRadius:5}),ve("inspector",i-t-12,90,t,e-144,24,{refractionHeight:20,refractionAmount:-32,blurRadius:7,surfaceColor:[1,1,1,.16]}),ve("status-bar",12,e-o-8,i-24,o,14,{refractionHeight:11,refractionAmount:-20,blurRadius:4,surfaceColor:[1,1,1,.12]}),ve("ambient-orb-left",94,e*.57,72,72,36,{refractionHeight:22,refractionAmount:-40,blurRadius:2,surfaceColor:[.78,.9,1,.055],outerShadow:null,highlight:{...mt,alpha:.44,widthDp:.55}}),ve("ambient-orb-right",i-t-83,e*.22,54,54,27,{refractionHeight:18,refractionAmount:-36,blurRadius:2,surfaceColor:[.9,.84,1,.05],outerShadow:null,highlight:{...mt,alpha:.4,widthDp:.5}})];ye.setElements(r)},Nt=0;window.addEventListener("resize",()=>{cancelAnimationFrame(Nt),Nt=requestAnimationFrame(ze)});window.geometryGridGlass={refresh:ze,setQuality(i){ye.dpr=Math.max(.75,Math.min(1.5,i)),ze()}};ye.setGravityAngle(Math.PI/4);ye.loadWallpaper(rr()).then(()=>{ze(),window.chrome?.webview?.postMessage({type:"liquid-glass-ready",renderer:"martin65536/liquid-glass-webgl"})}).catch(i=>{window.chrome?.webview?.postMessage({type:"liquid-glass-error",message:i instanceof Error?i.message:String(i)})});})();
