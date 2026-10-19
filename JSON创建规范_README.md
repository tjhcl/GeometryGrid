# GeometryGrid JSON 创建规范

适用程序：`GeometryGrid-v.2.96.exe`  
Schema 版本：`1.0`  
文件编码：UTF-8  
建议扩展名：`.json`

本文档说明如何从零创建可由 GeometryGrid 打开、编辑和导出的几何项目。程序启动时显示的内置演示来自 `translated_geometry_8x8.json`；它也是一份可直接复制修改的完整范例。

## 1. 最快开始

将下面内容保存为 UTF-8 编码的 `my_geometry.json`，再在程序中点击“打开 JSON”。

```json
{
  "schemaVersion": "1.0",
  "canvas": {
    "minX": 0,
    "maxX": 8,
    "minY": 0,
    "maxY": 8,
    "padding": 34,
    "background": "#FFFFFF"
  },
  "grid": {
    "visible": true,
    "step": 1,
    "color": "#202020",
    "strokeWidth": 1,
    "borderWidth": 1.2,
    "showAxes": false,
    "showTickLabels": false
  },
  "styles": {
    "global": {
      "visible": true,
      "strokeWidth": 3.5,
      "strokeStyle": "solid",
      "color": "#000000",
      "opacity": 1,
      "pointRadius": 2,
      "zIndex": 10
    },
    "types": {
      "fixedPoint": {
        "zIndex": 30
      },
      "segment": {
        "zIndex": 10
      }
    }
  },
  "parameters": [],
  "objects": [
    {
      "id": "A",
      "type": "fixedPoint",
      "x": "1",
      "y": "1",
      "draggable": false,
      "label": {
        "text": "A",
        "visible": true,
        "fontSize": 45,
        "offsetX": 8,
        "offsetY": -30
      }
    },
    {
      "id": "B",
      "type": "fixedPoint",
      "x": "7",
      "y": "5",
      "draggable": false,
      "label": {
        "text": "B",
        "visible": true,
        "fontSize": 45,
        "offsetX": 8,
        "offsetY": -30
      }
    },
    {
      "id": "AB",
      "type": "segment",
      "pointA": "A",
      "pointB": "B",
      "draggable": false
    }
  ],
  "solver": {
    "targets": [
      {
        "name": "P",
        "x": "4",
        "y": "3"
      }
    ],
    "minSteps": 1,
    "maxSteps": 5,
    "maxSolutionsPerTarget": 3,
    "maxStates": 200000,
    "useStraightedge": true,
    "useCompass": false,
    "useGrid": true,
    "searchStrategy": 0,
    "validateParameters": false,
    "parameterSampleCount": 5
  },
  "exports": {
    "width": 1600,
    "height": 1600,
    "dpi": 144,
    "jpegQuality": 92,
    "transparentPng": false,
    "background": "#FFFFFF",
    "emfTextMode": "text"
  }
}
```

JSON 的基本语法要求：

- 属性名和字符串必须使用英文双引号 `"..."`。
- 数组或对象的最后一项后面不能保留逗号。
- 布尔值写作 `true` 或 `false`，不能加引号。
- 普通设置中的数值不加引号，例如 `"width": 1600`。
- 坐标、半径、角度、比例和参数值是表达式字符串，需要加引号，例如 `"x": "3/2"`。
- JSON 不支持注释；不要加入 `//` 或 `/* ... */`。

## 2. 顶层结构

建议始终保留以下八个顶层字段：

| 字段 | 类型 | 作用 |
|---|---:|---|
| `schemaVersion` | 字符串 | 当前必须为 `"1.0"` |
| `canvas` | 对象 | 数学世界范围、留白和画布背景 |
| `grid` | 对象 | 网格、边框及其样式 |
| `styles` | 对象 | 全局样式和按对象类型设置的样式 |
| `parameters` | 数组 | 可被表达式引用的参数 |
| `objects` | 数组 | 点、线、圆、弧及其依赖关系 |
| `solver` | 对象 | 求解目标、搜索上限和工具开关 |
| `exports` | 对象 | PNG、JPG、EMF 的导出设置 |

字段名称读取时不区分大小写，但为了便于检查和交流，请严格使用本文档中的大小写。对象 `type`、对象 ID 和参数 ID 区分大小写。

## 3. `canvas`：数学画布

```json
"canvas": {
  "minX": 0,
  "maxX": 8,
  "minY": 0,
  "maxY": 8,
  "padding": 34,
  "background": "#FFFFFF"
}
```

| 字段 | 类型 | 默认值 | 说明 |
|---|---:|---:|---|
| `minX` | 数值 | `0` | 左边界的世界坐标 |
| `maxX` | 数值 | `8` | 右边界的世界坐标，必须大于 `minX` |
| `minY` | 数值 | `0` | 下边界的世界坐标 |
| `maxY` | 数值 | `8` | 上边界的世界坐标，必须大于 `minY` |
| `padding` | 数值 | `28` | 图形区域到输出边缘的留白 |
| `background` | 字符串 | `"#FFFFFF"` | 画布背景色 |

程序始终保持 X、Y 等比例缩放，所以圆不会因窗口或输出尺寸不同而变成椭圆。如果输出宽高比与世界范围不同，多余空间会平均分布在两侧。

颜色推荐使用：

- `#RRGGBB`，例如黑色 `#000000`。
- `#AARRGGBB`，前两位为 Alpha，例如半透明红色 `#80FF0000`。

## 4. `grid`：网格与边框

```json
"grid": {
  "visible": true,
  "step": 1,
  "color": "#202020",
  "strokeWidth": 1,
  "borderWidth": 1.2,
  "showAxes": false,
  "showTickLabels": false
}
```

| 字段 | 类型 | 默认值 | 说明 |
|---|---:|---:|---|
| `visible` | 布尔 | `true` | 是否显示内部网格线 |
| `step` | 数值 | `1` | 网格间距，必须大于 0 |
| `color` | 字符串 | `"#202020"` | 网格和外边框颜色 |
| `strokeWidth` | 数值 | `1` | 网格线基准粗细 |
| `borderWidth` | 数值 | `1.2` | 画布边框基准粗细 |
| `showAxes` | 布尔 | `false` | 预留字段；当前版本尚未绘制独立坐标轴 |
| `showTickLabels` | 布尔 | `false` | 预留字段；当前版本尚未绘制刻度数字 |

即使 `visible` 为 `false`，外边框仍会绘制。若不希望看到边框，可将 `borderWidth` 设为很小的正数，或把颜色设为背景色。

## 5. `styles`：样式继承

最终样式按以下优先级合并，右侧覆盖左侧：

```text
程序默认值 → styles.global → styles.types[对象 type] → objects[].style
```

示例：

```json
"styles": {
  "global": {
    "visible": true,
    "strokeWidth": 3.5,
    "strokeStyle": "solid",
    "color": "#000000",
    "opacity": 1,
    "pointRadius": 2,
    "zIndex": 10
  },
  "types": {
    "fixedPoint": {
      "color": "#1565C0",
      "zIndex": 30
    },
    "segment": {
      "strokeWidth": 3.5,
      "zIndex": 10
    }
  }
}
```

通用样式字段：

| 字段 | 类型 | 程序默认值 | 说明 |
|---|---:|---:|---|
| `visible` | 布尔 | `true` | 是否绘制该对象 |
| `strokeWidth` | 数值 | `3.5` | 线、射线、圆和圆弧的基准线宽 |
| `strokeStyle` | 字符串 | `"solid"` | `solid`、`dash` 或 `dot` |
| `color` | 字符串 | `"#000000"` | 对象及其标签的颜色 |
| `opacity` | 数值 | `1` | 不透明度，程序按 0–1 夹取 |
| `pointRadius` | 数值 | `2` | 点的基准半径 |
| `zIndex` | 整数 | `0` | 绘制层级；数值大的后画，因此显示在上方 |

注意：

- `visible: false` 只隐藏对象，不停止计算。辅助线可隐藏后继续供交点等对象引用。
- 点的标签使用同一个对象的 `color` 和 `opacity`。
- 同一 `zIndex` 下按 `objects` 中的先后顺序绘制。
- 类型样式的键应与对象 `type` 相同，例如 `intersectionPoint`。

对象级覆盖示例：

```json
{
  "id": "helper_x2",
  "type": "verticalLine",
  "x": "2",
  "style": {
    "visible": false,
    "color": "#999999",
    "strokeStyle": "dash"
  }
}
```

## 6. `parameters`：参数与可拖动范围

```json
"parameters": [
  {
    "id": "t",
    "value": "1/3",
    "min": "0",
    "max": "4/5"
  }
]
```

| 字段 | 类型 | 说明 |
|---|---:|---|
| `id` | 字符串 | 唯一参数名，区分大小写 |
| `value` | 表达式字符串 | 当前参数值 |
| `min` | 表达式字符串 | 画布拖动该参数时的下界 |
| `max` | 表达式字符串 | 画布拖动该参数时的上界 |

规则：

- 每个参数 ID 必须非空且唯一。
- 参数可引用已经能够求值的其他参数；程序会自动按依赖关系求值。
- 参数既可保存精确有理数，也可保存根式或超越值，例如 `"1/3"`、`"sqrt(5)"`、`"cosd(50)"`。
- 参数求值会保留“精确有理数（若存在）+ 双精度近似值”；三角函数等超越结果用稳定的双精度值继续参与后续计算。
- `min`、`max` 主要用于鼠标拖动时夹取；直接手工写入的 `value` 不会自动被夹取。
- 对线段上的动点，通常使用 `min: "0"`、`max: "1"`。
- 对整圆或完整圆弧上的动点，通常也使用 0–1。

## 7. 表达式规范

下列对象字段使用表达式字符串：

```text
x, y, radius, ratio, startAngle, endAngle
parameters[].value, parameters[].min, parameters[].max
```

支持：

- 整数：`"6"`、`"-2"`
- 小数：`"1.5"`、`".25"`
- 分数与四则运算：`"3/8"`、`"1 + 5*t/8"`
- 一元正负号：`"-t"`、`"+3"`
- 括号：`"(a + b) / 2"`
- 平方根：`"sqrt(5)"`、`"sqrt(9/4)"`
- 幂：`"2^3"`、`"(a+b)^2"`；也可写 `"pow(2,3)"`
- 数学常量：`"pi"`、`"e"`、`"tau"`（即 `2*pi`）
- 弧度三角函数：`sin`、`cos`、`tan`，例如 `"cos(50*pi/180)"`
- 角度三角函数：`sind`、`cosd`、`tand`，例如 `"cosd(50)"`
- 反三角函数：`asin/acos/atan/atan2` 返回弧度，`asind/acosd/atand/atan2d` 返回角度
- 其他函数：`abs`、`exp`、`ln`（或 `log`）、`log10`、`min`、`max`
- 参数标识符：字母或下划线开头，后接字母、数字或下划线

运算优先级：

```text
括号 / 函数 → 幂 ^ → 一元正负号 → 乘除 → 加减
```

不支持：

- 对象坐标引用，例如 `A.x`
- 隐式乘法，例如 `2t`；必须写成 `2*t`
- 未列入白名单的函数、成员访问或任何脚本代码

表达式只能引用 `parameters` 中的参数，不能直接引用对象 ID。对象之间的几何依赖应通过 `pointA`、`pointB`、`center`、`support` 等字段表达。

角度注意事项：

- `sin/cos/tan` 遵循标准数学约定，输入单位是弧度。
- 网格题按“度”给角时，优先使用 `sind/cosd/tand`，例如 `"sqrt(17)/(4*cosd(50))"`。
- 不要先把超越坐标截成 6 位小数再交给求解器。搜索默认容差很小，截断坐标可能使正确构造永远无法命中；应把原公式直接写进参数或 `solver.targets`。

## 8. `objects`：通用规则

每个对象至少需要：

```json
{
  "id": "A",
  "type": "fixedPoint"
}
```

通用字段：

| 字段 | 类型 | 说明 |
|---|---:|---|
| `id` | 字符串 | 对象唯一 ID，非空、区分大小写 |
| `name` | 字符串 | 用户可修改的显示名称；不参与对象依赖 |
| `type` | 字符串 | 对象类型，必须使用本文列出的精确拼写 |
| `displayMinParameter` | 数字 | 可选，仅限制线对象的显示起点，不改变几何求交语义 |
| `displayMaxParameter` | 数字 | 可选，仅限制线对象的显示终点；求解动画用它生成有限射线 |
| `draggable` | 布尔 | 是否允许在画布上拖动几何位置 |
| `style` | 对象 | 当前对象的样式覆盖 |
| `label` | 对象 | 对象在画布上的显示名称标签 |

依赖规则：

- 引用字段必须填写已存在的对象 ID。
- JSON 中对象的排列顺序不影响计算，程序会自动拓扑排序。
- 不允许循环依赖，例如 A 依赖 B，同时 B 又依赖 A。
- 上游对象无效时，下游对象也会变为无效。
- 隐藏的辅助对象仍然可以被引用。

## 9. 点对象

### 9.1 `fixedPoint`：固定点

```json
{
  "id": "A",
  "type": "fixedPoint",
  "x": "1",
  "y": "3/2",
  "draggable": false
}
```

必需字段：`x`、`y`。

`draggable: true` 表示独立自由点，可直接在画布中拖动并更新 `x/y`；`draggable: false` 表示固定点。v.1.31 起，软件内点击空白创建的独立点默认写入 `draggable: true`，而两条背景格线的交点写入不可拖动的固定点。

### 9.2 `parametricPoint`：参数点

```json
{
  "id": "N",
  "type": "parametricPoint",
  "x": "1 + 15*t/2",
  "y": "1 + 5*t/8",
  "draggable": false
}
```

必需字段：`x`、`y`。表达式可引用任意已定义参数。

### 9.3 `midpoint`：中点

```json
{
  "id": "M",
  "type": "midpoint",
  "pointA": "A",
  "pointB": "B"
}
```

必需字段：`pointA`、`pointB`，两者都必须引用点对象。

### 9.4 `ratioPoint`：比例点

```json
{
  "id": "P",
  "type": "ratioPoint",
  "pointA": "A",
  "pointB": "B",
  "ratio": "1/3"
}
```

计算公式：

```text
P = A + ratio × (B - A)
```

`ratio = 0` 得到 A，`ratio = 1` 得到 B；数值可以小于 0 或大于 1。

### 9.5 `intersectionPoint`：交点

```json
{
  "id": "X",
  "type": "intersectionPoint",
  "objectA": "circle_1",
  "objectB": "line_1",
  "selector": "upper"
}
```

必需字段：`objectA`、`objectB`。它们可形成以下组合：

- 线—线
- 线—圆或圆弧
- 圆或圆弧—圆或圆弧

`selector` 可选值：

| 值 | 说明 |
|---|---|
| `first` | 计算结果中的第一个交点；默认值 |
| `second` | 第二个交点；若只有一个则仍取该点 |
| `upper` | Y 最大的交点 |
| `lower` | Y 最小的交点 |
| `left` | X 最小的交点 |
| `right` | X 最大的交点 |
| `closestTo` | 取离 `closestTo` 指定参考点最近的交点 |

`closestTo` 示例：

```json
{
  "id": "X",
  "type": "intersectionPoint",
  "objectA": "circle_1",
  "objectB": "circle_2",
  "selector": "closestTo",
  "closestTo": "referencePoint"
}
```

当存在两个交点时，优先使用 `upper/lower/left/right/closestTo`，它们比 `first/second` 更直观、也更不依赖内部计算顺序。

### 9.6 `gridIntersectionPoint`：对象与背景网格线的交点

v.1.1 起，可直接把背景网格线用于求交，不需要创建隐藏的 `verticalLine` 或 `horizontalLine`。

```json
{
  "id": "X",
  "name": "X",
  "type": "gridIntersectionPoint",
  "objectA": "circle_1",
  "gridAxis": "x",
  "gridValue": "2",
  "selector": "upper"
}
```

| 字段 | 说明 |
|---|---|
| `objectA` | 已知直线、线段、射线、圆或圆弧 |
| `gridAxis` | `"x"` 表示竖直网格线，`"y"` 表示水平网格线 |
| `gridValue` | 网格线坐标表达式，例如 `"2"` 或 `"3/2"` |
| `selector` | 与普通交点相同，支持 `first/second/upper/lower/left/right/closestTo` |

例如 `gridAxis: "x", gridValue: "2"` 直接表示背景中的 `x=2` 网格线。该网格线不会出现在 `objects` 数组或“全部对象”侧边栏中。

### 9.7 `pointOnSegment` / `pointOnLine`：线上的参数点

```json
{
  "id": "M",
  "type": "pointOnSegment",
  "support": "AB",
  "parameter": "t",
  "draggable": true
}
```

必需字段：`support`、`parameter`。

点的位置为：

```text
support 起点 + t × support 方向向量
```

若 `support` 是由 A、B 定义的线：

- `t = 0` 对应 A。
- `t = 1` 对应 B。
- `pointOnSegment` 和 `pointOnLine` 的计算方式相同；是否限制在线段内由参数的 `min/max` 决定。

### 9.8 `pointOnCircle` / `pointOnArc`：圆或弧上的参数点

```json
{
  "id": "Q",
  "type": "pointOnCircle",
  "support": "circle_O",
  "parameter": "t",
  "draggable": true
}
```

必需字段：`support`、`parameter`。

- 整圆：`t = 0` 位于圆心正右方，`t = 1/4` 位于正上方，`t = 1/2` 位于正左方。
- 圆弧：`t = 0` 为弧起点，`t = 1` 为弧终点。
- 推荐给参数设置 `min: "0"` 和 `max: "1"`。

### 9.9 `pointOnGridLine`：背景格线上的可拖动点

```json
{
  "id": "G",
  "type": "pointOnGridLine",
  "gridAxis": "x",
  "gridValue": "2",
  "parameter": "t1",
  "draggable": true
}
```

`gridAxis: "x"` 表示点固定在竖直格线 `x=gridValue` 上，参数控制 y 坐标；`gridAxis: "y"` 表示固定在水平格线上，参数控制 x 坐标。格线仍然只是背景，不会生成隐藏的直线对象。参数的 `min/max` 建议对应画布在移动方向上的边界。

## 10. 线对象

### 10.1 `segment`：线段

```json
{
  "id": "AB",
  "type": "segment",
  "pointA": "A",
  "pointB": "B"
}
```

只绘制 A 到 B 之间的部分。

### 10.2 `ray`：射线

```json
{
  "id": "ray_AB",
  "type": "ray",
  "pointA": "A",
  "pointB": "B"
}
```

从 A 出发，经过 B，延伸到画布边界。

### 10.3 `infiniteLine` / `lineThroughPoints`：两点直线

```json
{
  "id": "line_AB",
  "type": "lineThroughPoints",
  "pointA": "A",
  "pointB": "B"
}
```

两种类型为同义写法，均向两侧延伸到画布边界。

对所有由两点定义的线对象，A、B 不能重合。

### 10.4 `horizontalLine`：水平线

```json
{
  "id": "line_y4",
  "type": "horizontalLine",
  "y": "4"
}
```

必需字段：`y`。

### 10.5 `verticalLine`：竖直线

```json
{
  "id": "line_x2",
  "type": "verticalLine",
  "x": "2"
}
```

必需字段：`x`。

### 10.6 `perpendicularLine`：过点作已知直线的垂线

```json
{
  "id": "perpendicular1",
  "name": "垂线 1",
  "type": "perpendicularLine",
  "pointA": "P",
  "objectA": "AB"
}
```

`pointA` 是垂线经过的点。基准线有两种写法：

- `objectA` 引用任意直线、线段或射线对象。
- 或使用 `gridAxis` 与 `gridValue` 直接引用背景格线，例如 `"gridAxis": "x", "gridValue": "2"`。

### 10.7 `parallelLine`：过点作已知直线的平行线

```json
{
  "id": "parallel1",
  "name": "平行线 1",
  "type": "parallelLine",
  "pointA": "P",
  "objectA": "AB"
}
```

`pointA` 是平行线经过的点。基准线可以使用 `objectA` 引用几何线，也可以使用 `gridAxis` 与 `gridValue` 引用背景格线。

## 11. 圆与圆弧对象

### 11.1 `circleByCenterRadius`：圆心与半径

```json
{
  "id": "circle_O",
  "type": "circleByCenterRadius",
  "center": "O",
  "radius": "sqrt(5)"
}
```

`center` 必须引用点，`radius` 必须大于 0。

### 11.2 `circleByCenterPoint`：圆心与圆上一点

```json
{
  "id": "circle_OA",
  "type": "circleByCenterPoint",
  "center": "O",
  "pointA": "A"
}
```

半径自动取 O、A 两点的距离。以已有线段为半径作圆时，可把线段的一个端点写入 `center`，另一个端点写入 `pointA`。

### 11.3 `circleByCenterDistance`：圆心与两点距离

```json
{
  "id": "circle_O_AB",
  "type": "circleByCenterDistance",
  "center": "O",
  "pointA": "A",
  "pointB": "B"
}
```

圆心为 O，半径自动取 A、B 两点之间的距离。A、B 可以是普通点、网格交点或其他受约束点；两点不能重合。

### 11.4 `circleByDiameter`：直径圆

```json
{
  "id": "circle_AB",
  "type": "circleByDiameter",
  "pointA": "A",
  "pointB": "B"
}
```

A、B 为直径端点，建议确保两点不重合。

### 11.5 `circleByThreePoints`：过三个点的外接圆

```json
{
  "id": "circle_ABC",
  "type": "circleByThreePoints",
  "pointA": "A",
  "pointB": "B",
  "pointC": "C"
}
```

三个点必须互不重合且不共线。

### 11.6 `arc`：圆心、半径与角度定义的弧

```json
{
  "id": "arc_O",
  "type": "arc",
  "center": "O",
  "radius": "3",
  "startAngle": "0",
  "endAngle": "120"
}
```

角度单位为度：

- 0°：圆心正右方。
- 90°：圆心正上方。
- 正方向：数学坐标系中的逆时针。
- 程序将 `endAngle - startAngle` 归一化为 0°–360° 的正向扫角。

### 11.7 `semicircleByDiameter`：直径半圆

```json
{
  "id": "arc_AB",
  "type": "semicircleByDiameter",
  "pointA": "A",
  "pointB": "B",
  "orientation": "upper"
}
```

`orientation` 可选：

- `upper`：上侧半圆
- `lower`：下侧半圆
- `left`：左侧半圆
- `right`：右侧半圆

默认值为 `upper`。

## 12. `label`：画布显示名称

```json
"label": {
  "text": "A",
  "visible": true,
  "fontSize": 45,
  "offsetX": 8,
  "offsetY": -30
}
```

| 字段 | 类型 | 默认值 | 说明 |
|---|---:|---:|---|
| `text` | 字符串 | `""` | 标签内容 |
| `visible` | 布尔 | `true` | 是否显示标签 |
| `fontSize` | 数值 | `45` | 基准字号，界面编辑时最小为 6 |
| `offsetX` | 数值 | `8` | 相对点中心向右的偏移 |
| `offsetY` | 数值 | `-30` | 相对点中心向下的偏移；负数表示向上 |

v.1.1 起，点、线、射线、直线、圆和圆弧均可显示标签。`name` 是对象的显示名称，`label.text` 是画布上实际绘制的文字；在软件右侧修改“显示名称”时，两者会自动同步。

v.1.2 起，软件内新建的对象默认不写入 `name` 和 `label`。内部 `id` 仍会自动生成以维护依赖，但不会在画布上显示。只有用户在属性面板中填写“显示名称”后，程序才创建并显示名称标签。

其他注意事项：

- 标签使用 Times New Roman。
- 拉丁变量字母显示为斜体；数字、括号、运算符和 `sqrt` 保持正体。
- 标签颜色和不透明度来自对象最终样式。
- 若对象 `style.visible` 为 `false`，对象及其标签会一起隐藏。
- 标签可在画布中直接拖动，也可用方向键微调；保存后会写回 `offsetX/offsetY`。

## 13. `draggable`：可拖动点

`draggable: true` 可用于两类参数点。

第一类是同时具有 `support` 和 `parameter` 的单参数约束点，通常为：

- `pointOnSegment`
- `pointOnLine`
- `pointOnCircle`
- `pointOnArc`

示例：

```json
"parameters": [
  {
    "id": "t",
    "value": "1/3",
    "min": "0",
    "max": "1"
  }
],
"objects": [
  {
    "id": "M",
    "type": "pointOnSegment",
    "support": "AB",
    "parameter": "t",
    "draggable": true
  }
]
```

拖动时程序将鼠标位置投影到支撑线、圆或弧，并更新对应的 `parameter.value`。多个对象引用同一参数时，它们会同步变化。

第二类是 v.2.96 新增的二维自由参数点。它使用 `parametricPoint`，并同时声明 `parameterX` 与 `parameterY`：

```json
"parameters": [
  { "id": "bX", "value": "0.3", "min": "0.2", "max": "0.4" },
  { "id": "bY", "value": "1.3", "min": "1.2", "max": "1.4" }
],
"objects": [
  {
    "id": "B",
    "type": "parametricPoint",
    "x": "bX",
    "y": "bY",
    "parameterX": "bX",
    "parameterY": "bY",
    "draggable": true
  }
]
```

拖动 B 时，鼠标的横、纵坐标会分别夹取到 `bX`、`bY` 的 `min/max` 矩形范围。B、C 都采用这种写法时，就是四参数“超级不定图”。联合验解会使用多维低差异采样，不会只让所有参数沿同一条对角线一起变化。

不要只给 `fixedPoint` 设置 `draggable: true`；需要受参数控制的二维自由点应改用上面的 `parametricPoint + parameterX + parameterY`。

## 14. `solver`：最少步构造求解设置

```json
"solver": {
  "targets": [
    {
      "name": "P",
      "x": "7/8",
      "y": "5/2"
    },
    {
      "name": "Q",
      "x": "3 + t/2",
      "y": "sqrt(5)"
    }
  ],
  "minSteps": 1,
  "maxSteps": 5,
  "maxSolutionsPerTarget": 3,
  "maxStates": 200000,
  "useStraightedge": true,
  "useCompass": false,
  "useGrid": true,
  "searchStrategy": 0,
  "validateParameters": false,
  "parameterSampleCount": 5,
  "cpuThreads": 0
}
```

| 字段 | 类型 | 说明 |
|---|---:|---|
| `targets` | 数组 | 所有待求点；每项包含唯一 `name` 和表达式字符串 `x/y` |
| `minSteps` | 整数 | 搜索起始作图笔数，范围 0–8；已确认较浅层无解时可直接跳过 |
| `maxSteps` | 整数 | 搜索最大作图笔数，范围 1–8；一笔指新画一条直线或一个圆，已知对象、格线和交点不另计 |
| `maxSolutionsPerTarget` | 整数 | 保留的联合最少笔数解数量，范围 1–20；字段名为兼容旧版而保留 |
| `maxStates` | 整数 | 联合目标在每个作图笔数层最多检查的搜索状态，不小于 100 |
| `useStraightedge` | 布尔 | 是否允许过两个已知点作直线 |
| `useCompass` | 布尔 | 是否允许以已知点为圆心、已知两点距离为半径作圆 |
| `useGrid` | 布尔 | 是否把可见背景格线及其交点作为已知条件 |
| `searchStrategy` | 整数 | 搜索策略：0 自动、1 共线反推、2 启发式搜索、3 纯穷举 |
| `validateParameters` | 布尔 | 是否启用不定图联合验解；启用后同一构造必须在多个参数位置全部成立 |
| `parameterSampleCount` | 整数 | 联合检查总位置数（包含当前图），范围 3–11，推荐 5 或 7 |
| `cpuThreads` | 整数 | CPU 并行线程数；0 表示自动使用全部逻辑处理器，也可填写 1–256 |

### 14.1 几何约束目标

当 M、N 是由角度、共线等条件唯一决定，而四参数坐标公式过长时，目标可以不写 `x/y`，改写 `constraint`。v.2.96 首个约束类型为 `angleBisectorPair`：

```json
{
  "name": "M",
  "constraint": {
    "type": "angleBisectorPair",
    "role": "M",
    "pointA": "A",
    "pointB": "B",
    "pointC": "C",
    "pointE": "E",
    "pointP": "P",
    "selector": "first"
  }
}
```

它求解满足以下全部条件的一对点：

- M 在线段 AB 上；
- N 在线段 AC 上；
- M、P、N 共线；
- EA 平分 ∠MEN。

同一约束再写一项并将 `role` 改为 `N`，即可得到 N。`selector` 默认 `first`；若几何位置确实产生两个合格根，可用 `last` 或 `second` 选择沿 AB 参数较大的根。

约束目标会在每个参数场景重新计算，因此适合 B、C 都自由运动的题。它是数值约束求根，不是把某个截图位置的坐标固定下来。

v.2.1 的默认组合是 `useStraightedge: true`、`useCompass: false`、`useGrid: true`，即“直尺 + 网格作为已知”。直尺和圆规不能同时关闭。

v.2.11 起，求解页按括号层级拆分 X/Y 坐标。`"x": "(21+35*sqrt(17))/26"` 这类以括号开头的表达式可安全打开和保存；手动输入时既可写 `P = x, y`，也可写 `P = (x, y)`。不要省略表达式内部必须成对出现的括号。

v.2.2 起，多个目标若能通过同一条直线分别与已有圆、直线或格线相交得到，求解器会主动执行“共线闭包反推”。因此应把全部待求点共同放入 `solver.targets`，不要拆成多个 JSON；共享载线可能把原本分别求解的总笔数显著降低。

v.2.3 起，求解器保留四套策略并由 `searchStrategy` 选择。默认值 0 会依次使用速度更快的专用策略并在需要时退回逐层穷举；若要比较不同思路，可在软件求解页拖动策略滑块。结果播放前还会检查已有线段是否已经覆盖后续所需交点，避免重复作同一条 AC 等载线。

v.2.5 起，不定图应将独立运动参数绑定到 `draggable: true` 的约束点，并设置 `"validateParameters": true`。求解器会在参数的 `min/max` 开区间内选取多个位置，按对象 ID、格点键以及“哪两条对象的第几个交点”等来源关系重新执行每一笔。只有所有位置都能得到对应目标表达式的候选才会保留。`parameterSampleCount` 越大，过滤偶然巧合越严格，但验证耗时也会增加。

v.2.96 起，多参数位置采用 Halton 低差异序列分布。它比旧版的错位等距采样更适合二维或四维自由点，但仍属于有限抽样验证，不等价于符号几何证明。程序找到的方案应继续通过拖动 B、C 和更多参数位置复核；未找到方案也只表示当前策略与预算未命中，不代表题目无解。

v.2.97 起，`parameterSampleCount` 明确表示包含当前图在内的总位置数。搜索状态只在当前图计算；完整候选命中目标以后，才按距离当前参数较远的位置优先复验，并在第一处失败时停止。设置为 7 即“当前图 1 次 + 额外位置最多 6 次”，不是每个状态重复跑 7 次。

v.2.97 的完整逐层穷举会按第一笔分支使用多核 CPU 并行搜索。`searchStrategy: 0` 还会并发运行共线反推、启发式和穷举策略；`cpuThreads: 0` 自动使用本机全部逻辑处理器。发布包中的 `GeometryGrid.Solver.exe` 可在不启动桌面软件时读取同一 JSON，并导出 `.ggsteps.json` 作图步骤文件。

v.2.98 起，`minSteps` 与 `maxSteps` 共同指定搜索笔数范围。例如 `"minSteps": 4, "maxSteps": 6` 会直接搜索 4、5、6 笔层，不再重复 1–3 笔。`minSteps` 不能大于 `maxSteps`；旧 JSON 缺少 `minSteps` 时默认值为 1。若目标本身已知并希望允许 0 笔结果，可设为 0。

`minSteps/maxSteps` 对应界面中的“起始/最大作图笔数”，不是交点数量、对象总数或递归层数。例如先画一条直线，它与多条格线自动产生十几个交点，仍然只计 1 笔。每个笔数层分别使用一份 `maxStates` 预算；若较浅层达到上限，程序仍会继续尝试更深层，但会明确说明该候选解是否还能证明为最少笔数。

v.2.0 起，`targets` 中的多个点默认作为联合目标处理：一份解必须用同一套构造同时得到全部目标。题目已知对象之间，以及已知对象与启用背景格线之间位于画布范围内的交点，会在搜索开始前自动加入零笔数点池。例如 AC 已知且网格启用时，`AC∩x=2` 可以直接作为作线端点，不需要在 `objects` 中另建 E。

v.2.1 起，多目标且 `maxSteps >= 4` 时会先执行目标反推快速搜索。程序优先完成已有载线上的目标，再反推最后一笔所需的辅助交点。快速搜索返回的是有效短方案，但若尚未穷尽全部浅层分支，界面会标为“快速联合解”而不是“最优解”。因此，复杂题建议把 `maxSteps` 设置为你愿意接受的最大笔数，而不是从 1 或 2 反复手动增加。

打开 JSON 后，以上内容会自动进入右侧“求解”页；保存或另存为时，界面中的当前求解设置也会写回 `solver`。旧版 JSON 若没有该区块，程序会自动补用默认配置。

### 14.2 `.ggsteps.json` 作图步骤交换格式

`.ggsteps.json` 不是题目文件，而是求解结果文件。它由软件或命令行暴算器生成，顶层结构为：

```json
{
  "format": "GeometryGrid.ConstructionSteps",
  "version": 1,
  "createdUtc": "2026-08-03T08:00:00+00:00",
  "sourceProject": "C:\\题目\\example.json",
  "strategy": "parallel:逐层穷举",
  "optimalityProven": true,
  "solutions": []
}
```

`solutions[].steps[]` 会完整保存工具、两端点以及端点的来源关系。交点不是只保存截图坐标，而会保存 `parentElementA`、`parentElementB` 和 `selector`，因此导入时可以针对当前参数位置重新计算。这个格式应优先由程序导出；若人工编辑，必须保持步骤键和父对象引用一致。

## 15. `exports`：导出设置

```json
"exports": {
  "width": 1600,
  "height": 1600,
  "dpi": 144,
  "jpegQuality": 92,
  "transparentPng": false,
  "background": "#FFFFFF",
  "emfTextMode": "text"
}
```

| 字段 | 类型 | 默认值 | 说明 |
|---|---:|---:|---|
| `width` | 整数 | `1600` | 输出宽度，像素 |
| `height` | 整数 | `1600` | 输出高度，像素 |
| `dpi` | 数值 | `144` | PNG/JPG 的 DPI 元数据与位图渲染 DPI |
| `jpegQuality` | 整数 | `92` | JPG 质量，程序夹取到 1–100 |
| `transparentPng` | 布尔 | `false` | PNG 是否跳过画布背景 |
| `background` | 字符串 | `"#FFFFFF"` | JPG 背景色 |
| `emfTextMode` | 字符串 | `"text"` | EMF 标签方式：`text` 或 `outline` |

格式差异：

- PNG：`transparentPng: false` 时使用 `canvas.background`；为 `true` 时背景透明。
- JPG：不支持透明，使用 `exports.background`。
- EMF：使用 `canvas.background`，宽高采用 `width/height`。
- EMF `text`：保留文字，文件更小，目标电脑应有 Times New Roman。
- EMF `outline`：将文字转成矢量轮廓，跨电脑外观更稳定，但文件可能更大。
- GIF：先在求解页播放构造解，可继续调整构造对象的 `style`、`label`、名称与可拖动点位置，再用顶部 GIF 按钮导出；动画会继承当前画布格式。画面沿用 `exports.width/height` 的宽高比，长边自动限制为 960 像素，并写入循环播放与逐帧延时。

程序界面、PNG、JPG、GIF 和 EMF 都从同一几何场景重新绘制。输出尺寸变化时，点大小、线宽、网格粗细、字号和标签偏移会按同一视觉比例缩放。

## 16. 完整对象类型速查

| `type` | 必需字段 | 结果 |
|---|---|---|
| `fixedPoint` | `x`, `y` | 点 |
| `parametricPoint` | `x`, `y` | 点 |
| `midpoint` | `pointA`, `pointB` | 点 |
| `ratioPoint` | `pointA`, `pointB`, `ratio` | 点 |
| `intersectionPoint` | `objectA`, `objectB` | 点 |
| `gridIntersectionPoint` | `objectA`, `gridAxis`, `gridValue` | 点 |
| `pointOnGridLine` | `gridAxis`, `gridValue`, `parameter` | 点 |
| `pointOnSegment` | `support`, `parameter` | 点 |
| `pointOnLine` | `support`, `parameter` | 点 |
| `pointOnCircle` | `support`, `parameter` | 点 |
| `pointOnArc` | `support`, `parameter` | 点 |
| `segment` | `pointA`, `pointB` | 线段 |
| `ray` | `pointA`, `pointB` | 射线 |
| `infiniteLine` | `pointA`, `pointB` | 直线 |
| `lineThroughPoints` | `pointA`, `pointB` | 直线 |
| `perpendicularLine` | `pointA` + `objectA`，或 `pointA` + `gridAxis/gridValue` | 垂线 |
| `parallelLine` | `pointA` + `objectA`，或 `pointA` + `gridAxis/gridValue` | 平行线 |
| `horizontalLine` | `y` | 水平直线 |
| `verticalLine` | `x` | 竖直直线 |
| `circleByCenterRadius` | `center`, `radius` | 圆 |
| `circleByCenterPoint` | `center`, `pointA` | 圆 |
| `circleByCenterDistance` | `center`, `pointA`, `pointB` | 圆 |
| `circleByDiameter` | `pointA`, `pointB` | 圆 |
| `circleByThreePoints` | `pointA`, `pointB`, `pointC` | 圆 |
| `arc` | `center`, `radius`, `startAngle`, `endAngle` | 圆弧 |
| `semicircleByDiameter` | `pointA`, `pointB` | 半圆 |

## 17. 推荐创建流程

1. 复制一个已能打开的 JSON，或使用本文第 1 节模板。
2. 先设置 `canvas` 和 `grid`。
3. 在 `parameters` 中定义参数。
4. 先建立基础点，再建立线、圆和弧，最后建立交点与受约束点。
5. 先让所有对象可见，确认几何关系正确。
6. 将辅助线的对象级 `style.visible` 改为 `false`。
7. 添加点标签并在画布中拖到合适位置。
8. 在右侧“全部对象”标签页检查每个对象的有效状态。
9. 用“另存为”保存一份副本，再导出 PNG、JPG 或 EMF。

### v.1.31 软件内绘图工具

- 点：同一个工具同时负责空白点、对象上的受约束点和交点。
  - 点击空白位置创建可自由拖动的独立点。
  - 只靠近一条格线时创建可沿该格线拖动的 `pointOnGridLine`。
  - 同时靠近横、纵两条格线的交会处时创建不可拖动的固定交点，不错误降级为单格线动点。
  - 点击单条线、圆或圆弧，创建可沿该对象拖动的受约束点。
  - 点击两个对象的交会处，直接创建它们的交点。
  - 点击对象与背景网格的交会处，直接创建 `gridIntersectionPoint`。
  - 若该位置已有点，则选中已有点，不重复创建。
- 线段、射线、直线：依次点击两个点；点击空白时会连同所需固定点一起创建。
- 中点：依次点击两个点。
- 垂直：选择一条已知直线和一个点，选择顺序不限。
- 圆：支持线段/两点为直径、线段为半径、圆心加手动数值半径、圆心加两点距离半径、过三个点作圆。
- Delete 删除所选对象及依赖对象；Ctrl+Z 撤销，Ctrl+Y 重做。
- 右侧“画布”标签页可设置宽度和高度；保持 `minX/minY` 不变，只修改 `maxX/maxY`，即向右、向上调整。
- 调整 `canvas` 数学范围只改变几何对象在画布中的坐标映射，不改变 `strokeWidth`、`pointRadius`、`fontSize`、标签偏移或网格粗细。
- 程序崩溃时在 EXE 同目录创建 `crash` 文件夹并写入 `.log`；分发和备份时可将该日志交给开发者排查。
- 不再提供单独的“取交点”按钮；交点属于点工具的智能判定。
- Esc：取消当前工具并回到选择模式。
- 所有绘图操作均进入撤销/重做历史，并可保存到 JSON。

## 18. 常见错误与排查

### JSON 无法解析

检查：

- 是否漏写双引号。
- 是否多写或少写逗号。
- 大括号、方括号是否成对。
- 是否误加注释。
- 表达式是否忘记用字符串形式，例如应写 `"x": "1/3"`。

### 对象引用不存在

检查 `pointA`、`pointB`、`center`、`objectA`、`objectB`、`support`、`closestTo` 的拼写和大小写。对象 ID 区分大小写。

### 对象计算失败

“计算失败”不是程序崩溃，而是该对象当前无法从它引用的父对象得到有效几何结果；依赖它的下游对象也会暂时无效。对于交点，通常表示两对象平行、相离，或交点超出了线段/射线允许的范围。v.2.95 起，求解动画的显示裁剪不会再参与数学交点计算。

常见原因：

- 由两点定义直线时，两点重合。
- 圆半径不大于 0。
- 两个对象没有交点。
- `selector` 或 `orientation` 拼写错误。
- 支撑对象类型不符合要求。
- 表达式引用了不存在的参数。

### 动点不能拖动

检查：

- 对象是否设置 `"draggable": true`。
- 对象是否具有正确的 `support` 和 `parameter`。
- 参数 ID 是否存在。
- 参数 `min` 是否小于或等于 `max`。
- 支撑对象是否为线、圆或圆弧。

### 标签不显示

检查：

- 标签是否加在点对象上。
- `label.visible` 是否为 `true`。
- 对象最终样式的 `visible` 是否为 `true`。
- 标签颜色是否与背景相同。
- `offsetX/offsetY` 是否把文字移出了画布。

### 对象被遮挡

增大对象的 `zIndex`，或降低遮挡对象的 `zIndex`。点通常建议使用 30，线使用 10，圆弧使用 8–9。

## 19. 发布和打包说明

用于分发时，至少保留：

```text
GeometryGrid-v.2.96.exe
JSON创建规范_README.md
```

`GeometryGrid-v.2.96.exe` 是 Windows x64 自包含单文件程序，不要求目标电脑预装 .NET。默认演示、Product Sans 与 FZZhunYuan-M02 界面字体、Liquid Glass 运行层及第三方许可证已经嵌入 EXE，因此即使旁边没有 `examples` 文件夹也可以启动；Liquid Glass 默认关闭，可在设置中启用。

如果 EXE 上级目录中存在：

```text
examples/translated_geometry_8x8.json
```

程序会优先读取这份外部示例；否则读取编译时嵌入 EXE 的内置示例。正式打包时若希望默认演示固定不被外部文件替换，只需分发 EXE 和本 README。
