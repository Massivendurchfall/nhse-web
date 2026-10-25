// File access for the save editor. Everything stays in the browser.
// Folder mode (Chrome, Edge) keeps the directory handle so edited files can be written back.
// File mode (folder upload input) and ZIP mode can only read; saving then downloads a ZIP.

const state = { mode: null, dir: null, files: new Map() };

export function supportsFolderAccess() {
    return typeof window.showDirectoryPicker === 'function';
}

export function language() {
    return navigator.language || 'en';
}

async function walk(dir, prefix, depth) {
    for await (const [name, handle] of dir.entries()) {
        const path = prefix ? `${prefix}/${name}` : name;
        if (handle.kind === 'file')
            state.files.set(path, handle);
        else if (depth < 6)
            await walk(handle, path, depth + 1);
    }
}

export async function pickFolder() {
    let dir;
    try {
        dir = await window.showDirectoryPicker({ id: 'acnh-save', mode: 'readwrite' });
    } catch (e) {
        if (e && e.name === 'AbortError')
            return null;
        throw e;
    }
    state.mode = 'folder';
    state.dir = dir;
    state.files = new Map();
    await walk(dir, '', 0);
    return [...state.files.keys()];
}

// Hooks a hidden <input type="file" webkitdirectory> and reports the picked paths to .NET.
export function attachFolderInput(input, dotnet) {
    input.addEventListener('change', () => {
        state.mode = 'files';
        state.dir = null;
        state.files = new Map();
        for (const file of input.files)
            state.files.set(file.webkitRelativePath || file.name, file);
        dotnet.invokeMethodAsync('OnFolderInput', [...state.files.keys()]);
        input.value = '';
    });
}

export function openFolderInput(input) {
    input.click();
}

export async function readFile(path) {
    const entry = state.files.get(path);
    if (!entry)
        throw new Error(`not picked: ${path}`);
    const file = typeof entry.getFile === 'function' ? await entry.getFile() : entry;
    return new Uint8Array(await file.arrayBuffer());
}

export function canWriteBack() {
    return state.mode === 'folder' && state.dir !== null;
}

export async function writeFile(path, bytes) {
    const handle = state.files.get(path);
    if (!handle || typeof handle.createWritable !== 'function')
        throw new Error(`cannot write ${path}`);
    const writable = await handle.createWritable();
    await writable.write(bytes);
    await writable.close();
}

export function download(name, bytes, type = 'application/zip') {
    const blob = new Blob([bytes], { type });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = name;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 10000);
}

/** Decodes a picture file and scales it to width x height RGBA pixels (centre square crop). */
export async function decodeImage(bytes, width, height) {
    const bitmap = await createImageBitmap(new Blob([bytes]));
    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    const ctx = canvas.getContext('2d');
    ctx.imageSmoothingEnabled = true;
    ctx.imageSmoothingQuality = 'high';
    const side = Math.min(bitmap.width, bitmap.height);
    ctx.drawImage(bitmap, (bitmap.width - side) / 2, (bitmap.height - side) / 2, side, side, 0, 0, width, height);
    bitmap.close();
    return new Uint8Array(ctx.getImageData(0, 0, width, height).data.buffer);
}
