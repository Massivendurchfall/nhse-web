// Island map for the map editor: a small overview of the whole island and a detail view with
// item pictures. .NET owns the data; this module only draws and reports taps, strokes and areas.

let s = null;

function pixelCanvas(width, height, rgba) {
    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    canvas.getContext('2d').putImageData(new ImageData(new Uint8ClampedArray(rgba), width, height), 0, 0);
    return canvas;
}

function tileAt(canvas, event, across, down, originX, originY) {
    const rect = canvas.getBoundingClientRect();
    const x = Math.floor((event.clientX - rect.left) / rect.width * across);
    const y = Math.floor((event.clientY - rect.top) / rect.height * down);
    if (x < 0 || y < 0 || x >= across || y >= down)
        return null;
    return { x: originX + x, y: originY + y };
}

function image(url) {
    let entry = s.images.get(url);
    if (!entry) {
        entry = { img: new Image(), ok: false };
        entry.img.decoding = 'async';
        entry.img.onload = () => { entry.ok = true; redraw(); };
        entry.img.src = url;
        s.images.set(url, entry);
    }
    return entry.ok ? entry.img : null;
}

let pending = false;
function redraw() {
    if (pending)
        return;
    pending = true;
    requestAnimationFrame(() => {
        pending = false;
        drawOverview();
        drawDetail();
    });
}

function detailTile(e) {
    return tileAt(s.detail, e, s.view.n, s.view.n, s.view.x, s.view.y);
}

export function initMap(overview, detail, dotnet, width, height, terrain) {
    s = {
        overview, detail, dotnet, width, height,
        terrain: pixelCanvas(width, height, terrain),
        items: null, view: { x: 0, y: 0, n: 32 }, tiles: [], buildings: [],
        selection: null, cursor: null, hover: null, tool: 'look', drag: null, images: new Map(),
    };
    overview.width = width * 2;
    overview.height = height * 2;
    overview.addEventListener('click', e => {
        const p = tileAt(overview, e, width, height, 0, 0);
        if (p) dotnet.invokeMethodAsync('OnOverviewClick', p.x, p.y);
    });

    // A tap without moving is a click. Dragging paints tile by tile ('paint') or spans an area ('area').
    detail.addEventListener('pointerdown', e => {
        if (e.button !== 0) return;
        const p = detailTile(e);
        if (!p) return;
        s.drag = { start: p, last: p, moved: false };
        detail.setPointerCapture(e.pointerId);
    });
    detail.addEventListener('pointermove', e => {
        const p = detailTile(e);
        s.hover = p;
        const d = s.drag;
        if (d && p && (p.x !== d.last.x || p.y !== d.last.y)) {
            if (!d.moved && s.tool === 'paint')
                dotnet.invokeMethodAsync('OnPaint', d.start.x, d.start.y, true);
            d.moved = true;
            d.last = p;
            if (s.tool === 'paint')
                dotnet.invokeMethodAsync('OnPaint', p.x, p.y, false);
        }
        if (s.cursor || d) redraw();
    });
    const finish = (e, cancelled) => {
        const d = s?.drag;
        if (!d) return;
        s.drag = null;
        if (cancelled) {
            if (d.moved && s.tool === 'paint') dotnet.invokeMethodAsync('OnPaintEnd');
        } else if (s.tool === 'area') {
            dotnet.invokeMethodAsync('OnArea', d.start.x, d.start.y, d.last.x, d.last.y);
        } else if (!d.moved) {
            dotnet.invokeMethodAsync('OnDetailClick', d.start.x, d.start.y);
        } else if (s.tool === 'paint') {
            dotnet.invokeMethodAsync('OnPaintEnd');
        }
        redraw();
    };
    detail.addEventListener('pointerup', e => finish(e, false));
    detail.addEventListener('pointercancel', e => finish(e, true));
    detail.addEventListener('pointerleave', () => { if (!s.drag) { s.hover = null; redraw(); } });

    s.resize = new ResizeObserver(() => {
        const size = Math.round(detail.clientWidth * (window.devicePixelRatio || 1));
        if (size > 0 && detail.width !== size) {
            detail.width = size;
            detail.height = size;
            redraw();
        }
    });
    s.resize.observe(detail);
}

/** Tiles per side of the detail view to start with: one acre on wide screens, half an acre on phones. */
export function tilesAcross(detail) {
    return detail.clientWidth >= 440 ? 32 : 16;
}

export function setItems(rgba, buildings) {
    s.items = pixelCanvas(s.width, s.height, rgba);
    s.buildings = buildings;
    redraw();
}

/** Replaces a block of the ground picture after paths changed (item tile coordinates). */
export function patchTerrain(x, y, w, h, rgba) {
    s.terrain.getContext('2d').putImageData(new ImageData(new Uint8ClampedArray(rgba), w, h), x, y);
    redraw();
}

export function setView(x, y, n, tiles, selection, cursor, tool) {
    s.view = { x, y, n };
    s.tiles = tiles;
    s.selection = selection;
    s.cursor = cursor;
    s.tool = tool;
    // painting and spanning areas need the finger on the map, looking leaves scrolling to the page
    s.detail.style.touchAction = tool === 'look' ? 'manipulation' : 'none';
    redraw();
}

/** Saves the whole island as a PNG, every tile drawn as a 4 x 4 pixel square. */
export function exportMap(fileName) {
    const scale = 4;
    const canvas = document.createElement('canvas');
    canvas.width = s.width * scale;
    canvas.height = s.height * scale;
    const ctx = canvas.getContext('2d');
    ctx.imageSmoothingEnabled = false;
    ctx.drawImage(s.terrain, 0, 0, canvas.width, canvas.height);
    if (s.items) ctx.drawImage(s.items, 0, 0, canvas.width, canvas.height);
    drawBuildings(ctx, scale, 0, 0);
    canvas.toBlob(blob => {
        const a = document.createElement('a');
        a.href = URL.createObjectURL(blob);
        a.download = fileName;
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(() => URL.revokeObjectURL(a.href), 10000);
    }, 'image/png');
}

export function disposeMap() {
    s?.resize.disconnect();
    s = null;
}

// buildings come as [left, top, width, height, colour] in item tiles: bridges at their real size,
// everything else as the 4 x 4 square around its position, like NHSE
function drawBuildings(ctx, scale, originX, originY) {
    ctx.strokeStyle = 'rgba(74, 66, 55, 0.7)';
    ctx.lineWidth = Math.max(1, scale / 8);
    for (const [x, y, w, h, color] of s.buildings) {
        ctx.fillStyle = color;
        ctx.fillRect((x - originX) * scale, (y - originY) * scale, w * scale, h * scale);
        ctx.strokeRect((x - originX) * scale, (y - originY) * scale, w * scale, h * scale);
    }
}

function drawOverview() {
    if (!s) return;
    const ctx = s.overview.getContext('2d');
    ctx.imageSmoothingEnabled = false;
    ctx.drawImage(s.terrain, 0, 0, s.overview.width, s.overview.height);
    if (s.items) ctx.drawImage(s.items, 0, 0, s.overview.width, s.overview.height);
    const scale = s.overview.width / s.width;
    drawBuildings(ctx, scale, 0, 0);
    const v = s.view;
    ctx.lineWidth = 3;
    ctx.strokeStyle = '#d0613a';
    ctx.strokeRect(v.x * scale, v.y * scale, v.n * scale, v.n * scale);
}

function drawPicture(ctx, url, x, y, w, h, cell, alpha) {
    const img = url ? image(url) : null;
    if (!img) return;
    const size = Math.min(w, h) * cell * 0.92;
    ctx.globalAlpha = alpha;
    ctx.drawImage(img, x * cell + (w * cell - size) / 2, y * cell + (h * cell - size) / 2, size, size);
    ctx.globalAlpha = 1;
}

function outline(ctx, x, y, w, h, cell, color, fill) {
    const v = s.view;
    if (fill) {
        ctx.fillStyle = fill;
        ctx.fillRect((x - v.x) * cell, (y - v.y) * cell, w * cell, h * cell);
    }
    ctx.lineWidth = Math.max(2, cell / 8);
    ctx.strokeStyle = color;
    ctx.strokeRect((x - v.x) * cell, (y - v.y) * cell, w * cell, h * cell);
}

function drawDetail() {
    if (!s || s.detail.width === 0) return;
    const ctx = s.detail.getContext('2d');
    const v = s.view;
    const cell = s.detail.width / v.n;
    ctx.imageSmoothingEnabled = false;
    ctx.drawImage(s.terrain, v.x, v.y, v.n, v.n, 0, 0, s.detail.width, s.detail.height);
    if (s.items) {
        // the map colors under the pictures show which tiles a big item covers
        ctx.globalAlpha = 0.35;
        ctx.drawImage(s.items, v.x, v.y, v.n, v.n, 0, 0, s.detail.width, s.detail.height);
        ctx.globalAlpha = 1;
    }
    ctx.imageSmoothingEnabled = true;

    // grid: thin lines per tile, stronger ones per terrain tile (2 x 2)
    ctx.lineWidth = 1;
    for (let i = 0; i <= v.n; i++) {
        ctx.strokeStyle = (v.x + i) % 2 === 0 ? 'rgba(0, 0, 0, 0.16)' : 'rgba(0, 0, 0, 0.06)';
        ctx.beginPath(); ctx.moveTo(i * cell, 0); ctx.lineTo(i * cell, s.detail.height); ctx.stroke();
        ctx.strokeStyle = (v.y + i) % 2 === 0 ? 'rgba(0, 0, 0, 0.16)' : 'rgba(0, 0, 0, 0.06)';
        ctx.beginPath(); ctx.moveTo(0, i * cell); ctx.lineTo(s.detail.width, i * cell); ctx.stroke();
    }

    drawBuildings(ctx, cell, v.x, v.y);
    for (const [x, y, w, h, url] of s.tiles)
        drawPicture(ctx, url, x - v.x, y - v.y, w, h, cell, 1);

    if (s.selection) {
        const [x, y, w, h] = s.selection;
        outline(ctx, x, y, w, h, cell, '#2fa9a1', s.tool === 'area' ? 'rgba(47, 169, 161, 0.18)' : null);
    }
    const d = s.drag;
    if (d && s.tool === 'area') {
        const x = Math.min(d.start.x, d.last.x), y = Math.min(d.start.y, d.last.y);
        outline(ctx, x, y, Math.abs(d.last.x - d.start.x) + 1, Math.abs(d.last.y - d.start.y) + 1, cell, '#2fa9a1', 'rgba(47, 169, 161, 0.18)');
    } else if (s.cursor && s.hover) {
        // [width, height, snap to even tiles, picture, colour, tiles to step back from the snapped tile]
        const [w, h, snap, url, color, back] = s.cursor;
        let { x, y } = s.hover;
        if (snap) { x -= x % 2; y -= y % 2; }
        x -= back; y -= back;
        outline(ctx, x, y, w, h, cell, color, color + '40');
        drawPicture(ctx, url, x - v.x, y - v.y, w, h, cell, 0.6);
    }
}
