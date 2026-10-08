# 文档图片

本目录存放文档引用的图片资源。正文中请使用相对路径引用，例如 `../images/example.png`。

## 控制体积

图片会永久留在 git 历史里，加图前请先压缩。经验值：

| 图片类型 | 建议 | 例 |
| --- | --- | --- |
| 游戏画面、照片类（颜色丰富） | 转 JPEG，质量 80 左右 | `cover.png` 204 KB → `cover.jpg` 21 KB；`gameresolution.png` 2.0 MB → `gameresolution.jpg` 117 KB |
| 界面截图、对话框（大片纯色 + 文字） | 保留 PNG，JPEG 反而更大 | `screen.png`、`display.png` |
| 全屏截图（宽 2560 以上） | **不要**缩到 1600 宽以内，界面文字会糊；保持原分辨率转 JPEG，体积仍只有原 PNG 的十分之一 | `rdm.png` 5.8 MB → `rdm.jpg` 523 KB |

压缩是否过头的判断方法：用系统自带 OCR 再读一遍压缩后的图，还能认出关键文字（分辨率、按钮名）就没问题。
