from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageChops
import numpy as np, os

OUT = r"E:\CodeProje\MyVideo\docs\logos"
SIZE = 1024

INK1 = (22, 48, 94, 255)
INK2 = (6, 14, 29, 255)
BLUE = (79, 143, 255, 255)
CYAN = (52, 213, 232, 255)
WHITE = (243, 249, 255, 255)
SHADOW = (0, 0, 0, 90)


def lerp(c1, c2, t):
    return tuple(int(c1[i] * (1 - t) + c2[i] * t) for i in range(3)) + (255,)


def linear_gradient(size, c1, c2, angle=(1, 1)):
    w, h = size
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    vx, vy = angle
    t = (xx * vx + yy * vy) / max(1.0, (w - 1) * abs(vx) + (h - 1) * abs(vy))
    t = np.clip(t, 0, 1)[..., None]
    c1a = np.array(c1[:3], dtype=np.float32)
    c2a = np.array(c2[:3], dtype=np.float32)
    arr = c1a * (1 - t) + c2a * t
    rgba = np.zeros((h, w, 4), dtype=np.uint8)
    rgba[..., :3] = arr.astype(np.uint8)
    rgba[..., 3] = 255
    return Image.fromarray(rgba, "RGBA")


def add_glow(canvas, center, radius, color, alpha=60):
    glow = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(glow)
    x, y = center
    d.ellipse((x - radius, y - radius, x + radius, y + radius), fill=color[:3] + (alpha,))
    glow = glow.filter(ImageFilter.GaussianBlur(radius // 3))
    canvas.alpha_composite(glow)


def make_tile(canvas_size=1024, margin=42, radius=230):
    img = Image.new("RGBA", (canvas_size, canvas_size), (0, 0, 0, 0))
    tile_size = canvas_size - margin * 2
    grad = linear_gradient((tile_size, tile_size), INK1, INK2, angle=(0.65, 1.0))
    mask = Image.new("L", (tile_size, tile_size), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, tile_size - 1, tile_size - 1), radius=radius, fill=255)
    img.paste(grad, (margin, margin), mask)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((margin, margin, canvas_size - margin - 1, canvas_size - margin - 1),
                        radius=radius, outline=(130, 196, 255, 70), width=max(2, canvas_size // 300))
    # top sheen
    sheen = Image.new("RGBA", (tile_size, tile_size), (0, 0, 0, 0))
    sd = ImageDraw.Draw(sheen)
    sd.ellipse((-tile_size * 0.25, -tile_size * 0.45, tile_size * 0.95, tile_size * 0.35),
               fill=(255, 255, 255, 18))
    sheen = sheen.filter(ImageFilter.GaussianBlur(tile_size // 18))
    sheen.putalpha(ImageChops.multiply(sheen.getchannel("A"), mask))
    img.alpha_composite(sheen, (margin, margin))
    return img


def gradient_shape(mask, c1, c2, angle=(1, 0.8)):
    w, h = mask.size
    grad = linear_gradient((w, h), c1, c2, angle=angle)
    layer = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    layer.paste(grad, (0, 0), mask)
    return layer


def render_encryptor(size=1024, margin=42):
    canvas = make_tile(size, margin)
    add_glow(canvas, (size // 2, size // 2 - 20), size // 4, (67, 177, 255, 255), 45)
    scale = size / 1024.0
    pts = [(512, 225), (740, 315), (740, 505), (690, 700), (512, 820), (334, 700), (284, 505), (284, 315)]
    pts = [(x * scale, y * scale) for x, y in pts]
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).polygon(pts, fill=255)
    shield = gradient_shape(mask, (111, 190, 255, 255), (39, 95, 226, 255), angle=(0.8, 1.0))
    canvas.alpha_composite(shield)
    d = ImageDraw.Draw(canvas)
    d.line(pts + [pts[0]], fill=(218, 240, 255, 225), width=max(4, int(13 * scale)), joint="curve")
    # keyhole
    cx, cy = 512 * scale, 470 * scale
    d.ellipse((cx - 57 * scale, cy - 57 * scale, cx + 57 * scale, cy + 57 * scale), fill=(7, 17, 34, 255))
    d.polygon([(cx - 27 * scale, cy + 30 * scale), (cx + 27 * scale, cy + 30 * scale),
               (cx + 42 * scale, cy + 165 * scale), (cx - 42 * scale, cy + 165 * scale)], fill=(7, 17, 34, 255))
    # small cyan signature dot
    d.ellipse((748 * scale, 748 * scale, 790 * scale, 790 * scale), fill=CYAN)
    return canvas


def render_player(size=1024, margin=42):
    canvas = make_tile(size, margin)
    add_glow(canvas, (size // 2, size // 2), size // 3, (79, 143, 255, 255), 55)
    scale = size / 1024.0
    d = ImageDraw.Draw(canvas)
    ring = (512 - 302, 512 - 302, 512 + 302, 512 + 302)
    d.ellipse(tuple(v * scale for v in ring), outline=(225, 246, 255, 235), width=max(5, int(34 * scale)))
    # play triangle with a subtle cyan edge
    triangle = [(423, 342), (423, 682), (735, 512)]
    tri = [(x * scale, y * scale) for x, y in triangle]
    d.polygon(tri, fill=WHITE)
    d.line(tri + [tri[0]], fill=(174, 235, 255, 210), width=max(2, int(8 * scale)), joint="curve")
    d.ellipse((748 * scale, 748 * scale, 790 * scale, 790 * scale), fill=CYAN)
    return canvas


def render_mobile(size=1024, margin=0):
    canvas = make_tile(size, margin, radius=230 if margin else 230)
    add_glow(canvas, (size // 2, size // 2), size // 3, (52, 213, 232, 255), 48)
    scale = size / 1024.0
    d = ImageDraw.Draw(canvas)
    # photo frame
    frame = (250, 250, 774, 774)
    d.rounded_rectangle(tuple(v * scale for v in frame), radius=int(108 * scale),
                        outline=(242, 249, 255, 245), width=max(5, int(34 * scale)))
    # play triangle tinted gradient
    tm = Image.new("L", (size, size), 0)
    tri = [(445, 360), (445, 664), (710, 512)]
    ImageDraw.Draw(tm).polygon([(x * scale, y * scale) for x, y in tri], fill=255)
    canvas.alpha_composite(gradient_shape(tm, (255, 255, 255, 255), (107, 221, 247, 255), angle=(0.8, 0.7)))
    # small photo aperture dot
    d.ellipse((650 * scale, 318 * scale, 706 * scale, 374 * scale), fill=(52, 213, 232, 255))
    return canvas


def render_mobile_foreground(size=432):
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    scale = size / 1024.0
    d = ImageDraw.Draw(canvas)
    frame = (250, 250, 774, 774)
    d.rounded_rectangle(tuple(v * scale for v in frame), radius=int(108 * scale),
                        outline=(242, 249, 255, 245), width=max(4, int(34 * scale)))
    tm = Image.new("L", (size, size), 0)
    tri = [(445, 360), (445, 664), (710, 512)]
    ImageDraw.Draw(tm).polygon([(x * scale, y * scale) for x, y in tri], fill=255)
    canvas.alpha_composite(gradient_shape(tm, (255, 255, 255, 255), (107, 221, 247, 255), angle=(0.8, 0.7)))
    d.ellipse((650 * scale, 318 * scale, 706 * scale, 374 * scale), fill=(52, 213, 232, 255))
    return canvas
def save_icon_set(name, image, sizes=(16, 24, 32, 48, 64, 128, 256)):
    path = os.path.join(OUT, f"{name}-1024.png")
    image.save(path)
    image.resize((512, 512), Image.LANCZOS).save(os.path.join(OUT, f"{name}-512.png"))
    ico_path = os.path.join(OUT, f"{name}.ico")
    image.save(ico_path, format="ICO", sizes=[(s, s) for s in sizes])
    return path, ico_path


enc = render_encryptor()
player = render_player()
mobile = render_mobile()
save_icon_set("encryptor", enc)
save_icon_set("player", player)
save_icon_set("mobile", mobile)

# preview sheet
sheet = Image.new("RGBA", (1500, 640), (12, 17, 28, 255))
d = ImageDraw.Draw(sheet)
font_path = r"C:\Windows\Fonts\msyh.ttc"
font_big = ImageFont.truetype(font_path, 34)
font_small = ImageFont.truetype(font_path, 24)
items = [(enc, "MiYing Encryptor", "加密工具 · 盾牌钥匙孔"), (player, "MiYing Player", "桌面播放器 · 播放环"), (mobile, "MiYing Mobile", "安卓综合查看器 · 照片框内播放")]
x0 = 90
for i, (im, title, subtitle) in enumerate(items):
    x = x0 + i * 470
    thumb = im.resize((300, 300), Image.LANCZOS)
    sheet.alpha_composite(thumb, (x, 105))
    tw = d.textlength(title, font=font_big)
    d.text((x + (300 - tw) / 2, 435), title, font=font_big, fill=(240, 246, 255, 255))
    sw = d.textlength(subtitle, font=font_small)
    d.text((x + (300 - sw) / 2, 490), subtitle, font=font_small, fill=(148, 166, 195, 255))
sheet.save(os.path.join(OUT, "family-preview.png"))

# Android launcher assets
android_res = r"D:\Android\MiYingPlayer\app\src\main\res"
densities = {"mdpi": 1.0, "hdpi": 1.5, "xhdpi": 2.0, "xxhdpi": 3.0, "xxxhdpi": 4.0}
for density, factor in densities.items():
    folder = os.path.join(android_res, f"mipmap-{density}")
    os.makedirs(folder, exist_ok=True)
    mobile.resize((int(48 * factor), int(48 * factor)), Image.LANCZOS).save(os.path.join(folder, "ic_launcher.png"))
    fg = render_mobile_foreground(432)
    fg.resize((int(108 * factor), int(108 * factor)), Image.LANCZOS).save(os.path.join(folder, "ic_launcher_foreground.png"))
drawable_nodpi = os.path.join(android_res, "drawable-nodpi")
os.makedirs(drawable_nodpi, exist_ok=True)
mobile.resize((512, 512), Image.LANCZOS).save(os.path.join(drawable_nodpi, "logo_mobile.png"))
print("android assets", android_res)
print("generated", OUT)
