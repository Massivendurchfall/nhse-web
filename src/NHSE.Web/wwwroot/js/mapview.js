// Island map for the map editor: a small overview of the whole island and a detail view with
// item pictures. .NET owns the data; this module only draws and reports clicked tiles.

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

export function initMap(overview, detail, dotnet, width, height, terrain) {
    s = {
        overview, detail, dotnet, width, height,
        terrain: pixelCanvas(width, height, terrain),
        items: null, view: { x: 0, y: 0, n: 32 }, tiles: [], buildings: [],
        selection: null, cursor: null, hover: null, images: new Map(),
    };
    overview.width = width * 2;
    overview.height = height * 2;
    overview.addEventListener('click', e => {
        const p = tileAt(overview, e, width, height, 0, 0);
        if (p) dotnet.invokeMethodAsync('OnOverviewClick', p.x, p.y);
    });
    detail.addEventListener('click', e => {
        const p = tileAt(detail, e, s.view.n, s.view.n, s.view.x, s.view.y);
        if (p) dotnet.invokeMethodAsync('OnDetailClick', p.x, p.y);
    });
    detail.addEventListener('mousemove', e => {
        s.hover = tileAt(detail, e, s.view.n, s.view.n, s.view.x, s.view.y);
        if (s.cursor) redraw();
    });
    detail.addEventListener('mouseleave', () => { s.hover = null; redraw(); });
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

export function setView(x, y, n, tiles, selection, cursor) {
    s.view = { x, y, n };
    s.tiles = tiles;
    s.selection = selection;
    s.cursor = cursor;
    redraw();
}

export function disposeMap() {
    s?.resize.disconnect();
    s = null;
}

function drawBuildings(ctx, scale, originX, originY) {
    ctx.fillStyle = 'rgba(242, 193, 78, 0.85)';
    ctx.strokeStyle = 'rgba(74, 66, 55, 0.7)';
    ctx.lineWidth = Math.max(1, scale / 8);
    for (const [x, y] of s.buildings) {
        // buildings are drawn as 4 x 4 tile squares around their position, like NHSE
        const left = (x - 2 - originX) * scale;
        const top = (y - 2 - originY) * scale;
        ctx.fillRect(left, top, 4 * scale, 4 * scale);
        ctx.strokeRect(left, top, 4 * scale, 4 * scale);
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
        ctx.lineWidth = Math.max(2, cell / 8);
        ctx.strokeStyle = '#2fa9a1';
        ctx.strokeRect((x - v.x) * cell, (y - v.y) * cell, w * cell, h * cell);
    }
    if (s.cursor && s.hover) {
        const [w, h, snap, url] = s.cursor;
        let { x, y } = s.hover;
        if (snap) { x -= x % 2; y -= y % 2; }
        ctx.fillStyle = 'rgba(79, 174, 107, 0.25)';
        ctx.fillRect((x - v.x) * cell, (y - v.y) * cell, w * cell, h * cell);
        drawPicture(ctx, url, x - v.x, y - v.y, w, h, cell, 0.6);
        ctx.lineWidth = Math.max(2, cell / 8);
        ctx.strokeStyle = '#4fae6b';
        ctx.strokeRect((x - v.x) * cell, (y - v.y) * cell, w * cell, h * cell);
    }
}
