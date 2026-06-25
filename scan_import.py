"""
Experimental: import handwriting from a photographed template sheet.

Pipeline (classic CV, no paid AI):
  photo -> detect 3 corner fiducials -> affine-warp to canonical sheet ->
  crop each cell -> Otsu threshold (keep dark ink, drop faint printed guides) ->
  skeletonize -> trace centre-lines into ordered strokes -> JSON.

Usage:  python -u scan_import.py <image> <job.json> <out.json>
  job.json = { "cols": 7, "chars": "ABC...abc...0-9..." }
  out.json = { "glyphs": [ { "cp": 65, "w": W, "h": H,
                             "strokes": [ [[x,y],...], ... ] }, ... ] }

Deps:  pip install opencv-python scikit-image numpy
"""
import sys, json
import numpy as np
import cv2
from skimage.morphology import skeletonize

# Canonical sheet geometry — must match MainWindow.ScanImport.cs RenderTemplateSheet.
SHEET_W = 2480
COLS_DEFAULT = 7
FID = 70
MARGIN = 170
CELL_H = 360
GRID_TOP = 360


def geometry(cols, n):
    grid_w = SHEET_W - 2 * MARGIN
    cell_w = grid_w // cols
    grid_left = (SHEET_W - cell_w * cols) // 2
    rows = (n + cols - 1) // cols
    grid_right = grid_left + cell_w * cols
    grid_bottom = GRID_TOP + CELL_H * rows
    sheet_h = grid_bottom + MARGIN
    # fiducial centres (TL, TR, BL)
    tl = (grid_left - FID - 12 + FID / 2, GRID_TOP - FID - 12 + FID / 2)
    tr = (grid_right + 12 + FID / 2,      GRID_TOP - FID - 12 + FID / 2)
    bl = (grid_left - FID - 12 + FID / 2, grid_bottom + 12 + FID / 2)
    return dict(cell_w=cell_w, cell_h=CELL_H, grid_left=grid_left, grid_top=GRID_TOP,
                rows=rows, sheet_h=sheet_h, tl=tl, tr=tr, bl=bl)


def find_fiducials(gray):
    """Return up to 3 square blob centres, or None if not confidently found."""
    th = cv2.threshold(gray, 0, 255, cv2.THRESH_BINARY_INV + cv2.THRESH_OTSU)[1]
    cnts, _ = cv2.findContours(th, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    h, w = gray.shape
    cand = []
    for c in cnts:
        area = cv2.contourArea(c)
        if area < (w * h) * 0.0002 or area > (w * h) * 0.02:
            continue
        x, y, bw, bh = cv2.boundingRect(c)
        ar = bw / float(bh) if bh else 0
        if 0.6 < ar < 1.6 and cv2.contourArea(c) / float(bw * bh) > 0.7:
            cand.append((x + bw / 2, y + bh / 2))
    if len(cand) < 3:
        return None
    # pick the three extreme corners: TL (min x+y), TR (max x-y), BL (min x-y)
    tl = min(cand, key=lambda p: p[0] + p[1])
    tr = max(cand, key=lambda p: p[0] - p[1])
    bl = min(cand, key=lambda p: p[0] - p[1])
    if tl == tr or tl == bl or tr == bl:
        return None
    return tl, tr, bl


def warp(gray, fids, geo):
    src = np.float32([fids[0], fids[1], fids[2]])
    dst = np.float32([geo["tl"], geo["tr"], geo["bl"]])
    M = cv2.getAffineTransform(src, dst)
    return cv2.warpAffine(gray, M, (SHEET_W, geo["sheet_h"]),
                          flags=cv2.INTER_LINEAR, borderValue=255)


def trace_component(mask):
    """Order the skeleton pixels of one component into a polyline."""
    ys, xs = np.nonzero(mask)
    if len(xs) < 2:
        return None
    pts = list(zip(xs.tolist(), ys.tolist()))
    pset = set(pts)

    def neighbours(p):
        x, y = p
        return [(x + dx, y + dy) for dx in (-1, 0, 1) for dy in (-1, 0, 1)
                if (dx or dy) and (x + dx, y + dy) in pset]

    # start at an endpoint (1 neighbour) if any, else arbitrary
    start = next((p for p in pts if len(neighbours(p)) == 1), pts[0])
    order, visited, cur = [], set(), start
    while cur is not None and cur not in visited:
        order.append(cur); visited.add(cur)
        nxt = [q for q in neighbours(cur) if q not in visited]
        cur = min(nxt, key=lambda q: (q[0] - order[-1][0]) ** 2 + (q[1] - order[-1][1]) ** 2) if nxt else None
    if len(order) < 2:
        return None
    # simplify (Douglas-Peucker)
    arr = np.array(order, dtype=np.int32).reshape(-1, 1, 2)
    eps = max(1.5, 0.01 * cv2.arcLength(arr, False))
    simp = cv2.approxPolyDP(arr, eps, False).reshape(-1, 2)
    return [[float(x), float(y)] for x, y in simp]


def extract_cell(cell):
    """Cell grayscale -> list of strokes (cell-local coords)."""
    pad = int(min(cell.shape) * 0.10)
    inner = cell[pad:-pad, pad:-pad]
    if inner.size == 0:
        return None, 0, 0
    h, w = inner.shape
    # dark ink only; faint printed guides drop out under Otsu
    bw = cv2.threshold(inner, 0, 255, cv2.THRESH_BINARY_INV + cv2.THRESH_OTSU)[1]
    bw = cv2.morphologyEx(bw, cv2.MORPH_OPEN, np.ones((3, 3), np.uint8))
    if cv2.countNonZero(bw) < (w * h) * 0.003:
        return None, w, h            # empty cell
    skel = skeletonize(bw > 0)
    skel_u8 = (skel * 255).astype(np.uint8)
    num, labels = cv2.connectedComponents(skel_u8)
    strokes = []
    for lbl in range(1, num):
        poly = trace_component(labels == lbl)
        if poly and len(poly) >= 2:
            strokes.append(poly)
    return strokes, w, h


def main():
    image_path, job_path, out_path = sys.argv[1], sys.argv[2], sys.argv[3]
    job = json.load(open(job_path, encoding="utf-8"))
    cols = int(job.get("cols", COLS_DEFAULT))
    chars = job["chars"]

    img = cv2.imread(image_path, cv2.IMREAD_GRAYSCALE)
    if img is None:
        print("could not read image", file=sys.stderr); sys.exit(1)

    geo = geometry(cols, len(chars))
    fids = find_fiducials(img)
    if fids is not None:
        sheet = warp(img, fids, geo)
    else:
        # fallback: assume the photo is cropped to the page; scale to canonical
        sheet = cv2.resize(img, (SHEET_W, geo["sheet_h"]), interpolation=cv2.INTER_LINEAR)

    glyphs = []
    cw, ch = geo["cell_w"], geo["cell_h"]
    for i, ch_char in enumerate(chars):
        col, row = i % cols, i // cols
        x = geo["grid_left"] + col * cw
        y = geo["grid_top"] + row * ch
        cell = sheet[y:y + ch, x:x + cw]
        if cell.size == 0:
            continue
        try:
            strokes, w, h = extract_cell(cell)
        except Exception as ex:
            print(f"cell {ch_char!r}: {ex}", file=sys.stderr); continue
        if not strokes:
            continue
        glyphs.append({"cp": ord(ch_char), "w": float(w), "h": float(h), "strokes": strokes})

    json.dump({"glyphs": glyphs}, open(out_path, "w", encoding="utf-8"))
    print(f"OK {len(glyphs)} glyphs", flush=True)


if __name__ == "__main__":
    main()
