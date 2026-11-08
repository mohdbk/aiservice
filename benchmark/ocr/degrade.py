"""Make 'scanned' variants of the ATS pages: lower resolution, grey, slight tilt, noise, blur, JPEG."""
import pathlib
import random

import numpy as np
from PIL import Image, ImageFilter

DATA = pathlib.Path(__file__).parent / "data"


def degrade(src: pathlib.Path, dst: pathlib.Path, seed: int):
    rng = random.Random(seed)
    im = Image.open(src).convert("L")
    w, h = im.size
    im = im.resize((int(w * 0.75), int(h * 0.75)), Image.BILINEAR)           # about 150 DPI
    im = im.rotate(rng.uniform(-1.5, 1.5), resample=Image.BICUBIC, expand=True, fillcolor=255)
    arr = np.asarray(im).astype(np.float32)
    arr = arr * rng.uniform(0.85, 0.95) + rng.uniform(5, 20)                  # paper tone and contrast
    arr += np.random.default_rng(seed).normal(0, 9, arr.shape)                # sensor noise
    im = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.7))
    im.save(dst, "JPEG", quality=55)


def _perspective_coeffs(src, dst):
    """Coefficients for PIL's PERSPECTIVE transform mapping dst quad -> src quad."""
    m = []
    for (x, y), (u, v) in zip(dst, src):
        m.append([x, y, 1, 0, 0, 0, -u * x, -u * y])
        m.append([0, 0, 0, x, y, 1, -v * x, -v * y])
    a = np.array(m, dtype=np.float64)
    b = np.array([c for p in src for c in p], dtype=np.float64)
    return np.linalg.solve(a, b).tolist()


def photo(src: pathlib.Path, dst: pathlib.Path, seed: int):
    """Phone photo of a printed page: perspective, uneven lighting, low resolution, blur, JPEG."""
    rng = random.Random(seed)
    im = Image.open(src).convert("RGB")
    w, h = im.size
    j = lambda: rng.uniform(0.02, 0.05)
    quad = [(w * j(), h * j()), (w * (1 - j()), h * j() * 0.5), (w * (1 - j() * 0.5), h * (1 - j())), (w * j() * 0.7, h * (1 - j() * 0.6))]
    canvas = Image.new("RGB", (w, h), (90, 85, 80))                       # desk background
    warped = im.transform((w, h), Image.PERSPECTIVE, _perspective_coeffs([(0, 0), (w, 0), (w, h), (0, h)], quad),
                          Image.BICUBIC, fillcolor=(90, 85, 80))
    mask = Image.new("L", (w, h), 0)
    from PIL import ImageDraw
    ImageDraw.Draw(mask).polygon(quad, fill=255)
    canvas.paste(warped, (0, 0), mask)
    arr = np.asarray(canvas).astype(np.float32)
    yy, xx = np.mgrid[0:h, 0:w]
    light = 0.72 + 0.28 * (1 - ((xx - w * rng.uniform(0.2, 0.8)) ** 2 + (yy - h * rng.uniform(0.1, 0.5)) ** 2) / (w * w + h * h))
    arr = arr * light[..., None]
    arr += np.random.default_rng(seed).normal(0, 6, arr.shape)
    im = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8))
    im = im.resize((int(w * 0.55), int(h * 0.55)), Image.BILINEAR).filter(ImageFilter.GaussianBlur(0.8))
    im.save(dst, "JPEG", quality=45)


if __name__ == "__main__":
    for i, src in enumerate(sorted(DATA.glob("*__designed.png"))):
        dst = DATA / src.name.replace("__designed.png", "__photo.jpg")
        photo(src, dst, seed=200 + i)
        print("photo", dst.name)
    for i, src in enumerate(sorted(DATA.glob("*__ats.png"))):
        dst = DATA / src.name.replace("__ats.png", "__scanned.jpg")
        degrade(src, dst, seed=100 + i)
        print("degraded", dst.name)
