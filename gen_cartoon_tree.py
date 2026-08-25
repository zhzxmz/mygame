# -*- coding: utf-8 -*-
"""生成卡通可爱风小树 PNG（透明背景 + 带天空预览版），供 Unity 游戏使用。"""
import os
from PIL import Image, ImageDraw, ImageFilter

S = 1024          # 超采样绘制尺寸
FINAL = 512       # 最终输出尺寸
ROOT = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.join(ROOT, "Assets", "Sprites")
os.makedirs(OUT_DIR, exist_ok=True)

# ---------- 调色板 ----------
OUTLINE   = (74, 56, 42, 255)     # 深暖棕（树干描边）
TRUNK_M   = (150, 98, 54, 255)
TRUNK_L   = (188, 134, 82, 255)
TRUNK_D   = (116, 74, 40, 255)
LEAF_OUT  = (56, 94, 40, 255)     # 深绿（树冠描边）
LEAF_D    = (74, 138, 46, 255)
LEAF_M    = (112, 178, 66, 255)
LEAF_L    = (156, 214, 100, 255)
LEAF_HL   = (214, 240, 152, 255)
APPLE     = (228, 78, 68, 255)
APPLE_D   = (166, 46, 42, 255)
APPLE_HL  = (255, 172, 142, 255)
EYE       = (62, 44, 32, 255)
CHEEK     = (250, 140, 130, 130)
WHITE     = (255, 255, 255, 255)

# ---------- 树图层 ----------
tree = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(tree)


def blob(cx, cy, r, fill, leaf_out=LEAF_OUT):
    """画一个带描边的树冠圆团，并叠上左上光源的高光层次。"""
    d.ellipse([cx - r - 15, cy - r - 15, cx + r + 15, cy + r + 15], fill=leaf_out)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=fill)
    # 内层提亮（左上受光）
    ir = r * 0.62
    d.ellipse([cx - 30 - ir, cy - 36 - ir, cx - 30 + ir, cy - 36 + ir], fill=LEAF_L)
    hr = r * 0.32
    d.ellipse([cx - 46 - hr, cy - 60 - hr, cx - 46 + hr, cy - 60 + hr], fill=LEAF_HL)


def apple(cx, cy, r=26):
    d.ellipse([cx - r - 7, cy - r - 7, cx + r + 7, cy + r + 7], fill=APPLE_D)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=APPLE)
    hr = r * 0.38
    d.ellipse([cx - 7 - hr, cy - 9 - hr, cx - 7 + hr, cy - 9 + hr], fill=APPLE_HL)


# 1) 地面阴影（柔化）
sh = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(sh).ellipse([232, 856, 792, 936], fill=(40, 30, 20, 70))
sh = sh.filter(ImageFilter.GaussianBlur(26))
tree.alpha_composite(sh)

# 2) 树干（上窄下宽 + 描边 + 明暗条纹）
trunk_out = [(416, 928), (608, 928), (568, 536), (456, 536)]
trunk_in  = [(428, 896), (596, 896), (556, 560), (468, 560)]
d.polygon(trunk_out, fill=OUTLINE)
d.polygon(trunk_in, fill=TRUNK_M)
d.polygon([(486, 582), (508, 582), (500, 880), (478, 880)], fill=TRUNK_L)
d.polygon([(540, 582), (556, 582), (556, 880), (544, 880)], fill=TRUNK_D)

# 3) 树冠（先下后上，形成层次）
blob(420, 470, 118, LEAF_D)
blob(604, 470, 118, LEAF_D)
blob(356, 420, 152, LEAF_M)
blob(668, 420, 152, LEAF_M)
blob(512, 292, 206, LEAF_M)
blob(512, 168, 128, LEAF_L)

# 4) 小苹果
apple(300, 452)
apple(712, 428)
apple(468, 170)
apple(610, 218)
apple(378, 300)

# 5) 树脸（圆眼 + 腮红 + 微笑）
d.ellipse([452, 684, 488, 720], fill=EYE)
d.ellipse([536, 684, 572, 720], fill=EYE)
d.ellipse([462, 694, 474, 706], fill=WHITE)
d.ellipse([546, 694, 558, 706], fill=WHITE)
d.ellipse([420, 722, 472, 766], fill=CHEEK)
d.ellipse([552, 722, 604, 766], fill=CHEEK)
d.arc([464, 708, 560, 784], start=20, end=160, fill=EYE, width=12)

# ---------- 透明版 ----------
tree_small = tree.resize((FINAL, FINAL), Image.LANCZOS)
tree_small.save(os.path.join(OUT_DIR, "Tree_Cartoon.png"))

# ---------- 预览版（淡蓝天空 + 太阳 + 白云 + 草地） ----------
prev = Image.new("RGBA", (S, S), (0, 0, 0, 0))
pd = ImageDraw.Draw(prev)
for y in range(S):
    t = y / S
    r = int(174 + (238 - 174) * t)
    g = int(221 + (248 - 221) * t)
    b = 255
    pd.line([(0, y), (S, y)], fill=(r, g, b, 255))

# 太阳
glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(glow).ellipse([60, 60, 260, 260], fill=(255, 244, 178, 150))
glow = glow.filter(ImageFilter.GaussianBlur(40))
prev.alpha_composite(glow)
pd.ellipse([108, 108, 212, 212], fill=(255, 240, 160, 255))

# 白云
def cloud(cx, cy, s=1.0):
    for dx, dy, r in [(-70, 0, 44), (0, -22, 56), (66, 2, 42), (0, 12, 60)]:
        pd.ellipse([cx + dx * s - r * s, cy + dy * s - r * s,
                    cx + dx * s + r * s, cy + dy * s + r * s], fill=(255, 255, 255, 235))

cloud(240, 300, 0.9)
cloud(830, 210, 1.1)
cloud(640, 360, 0.7)

# 草地（圆润小坡）
pd.ellipse([-420, 660, 700, 1280], fill=(172, 224, 126, 255))
pd.ellipse([360, 700, 1500, 1290], fill=(156, 214, 114, 255))
pd.ellipse([-120, 760, 1180, 1320], fill=(140, 202, 102, 255))

# 小花
def flower(fx, fy):
    for ox, oy in [(-9, 0), (9, 0), (0, -9), (0, 9)]:
        pd.ellipse([fx + ox - 7, fy + oy - 7, fx + ox + 7, fy + oy + 7], fill=(255, 255, 255, 255))
    pd.ellipse([fx - 8, fy - 8, fx + 8, fy + 8], fill=(255, 208, 90, 255))

for fx, fy in [(200, 840), (880, 806), (120, 952), (960, 934), (360, 984)]:
    flower(fx, fy)

prev.alpha_composite(tree)
prev = prev.resize((FINAL, FINAL), Image.LANCZOS)
prev.save(os.path.join(OUT_DIR, "Tree_Cartoon_Preview.png"))

print("OK ->", os.path.join(OUT_DIR, "Tree_Cartoon.png"))
print("OK ->", os.path.join(OUT_DIR, "Tree_Cartoon_Preview.png"))
