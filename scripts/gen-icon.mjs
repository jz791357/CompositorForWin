#!/usr/bin/env node
// Converts the upstream 1024px app icon PNG into a multi-size Windows .ico
// (16/24/32/48/64/128/256, 32bpp BMP entries). Node-only: decodes the PNG with
// the built-in zlib, downsamples with premultiplied-alpha area averaging, and
// packs the ICO container by hand. Re-run after an upstream icon change:
//
//   node scripts/gen-icon.mjs [source.png] [output.ico]

import { readFileSync, writeFileSync } from 'node:fs';
import { inflateSync } from 'node:zlib';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const source = process.argv[2] ?? join(here, '..', '..', 'Compositor', 'Compositor', 'Assets.xcassets', 'AppIcon.appiconset', 'app-icon-1024.png');
const output = process.argv[3] ?? join(here, '..', 'src', 'Compositor', 'app.ico');
const SIZES = [16, 24, 32, 48, 64, 128, 256];

// --- Decode the PNG (8-bit RGBA, non-interlaced only; Xcode icons are). ---
const png = readFileSync(source);
if (png.readUInt32BE(0) !== 0x89504e47) throw 'not a PNG';
let width = 0, height = 0, bitDepth, colorType, interlace, idat = [];
for (let off = 8; off < png.length;) {
  const len = png.readUInt32BE(off), type = png.toString('ascii', off + 4, off + 8);
  const data = png.subarray(off + 8, off + 8 + len);
  if (type === 'IHDR') {
    width = data.readUInt32BE(0); height = data.readUInt32BE(4);
    bitDepth = data[8]; colorType = data[9]; interlace = data[12];
  } else if (type === 'IDAT') idat.push(data);
  else if (type === 'IEND') break;
  off += 12 + len;
}
if (bitDepth !== 8 || colorType !== 6) throw `unsupported PNG (depth ${bitDepth}, color ${colorType}) — extend the script`;
if (interlace !== 0) throw 'interlaced PNG not supported';
const raw = inflateSync(Buffer.concat(idat));

const bpp = 4, stride = width * bpp;
const rgba = Buffer.alloc(width * height * 4);
const paeth = (a, b, c) => {
  const p = a + b - c, pa = Math.abs(p - a), pb = Math.abs(p - b), pc = Math.abs(p - c);
  return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
};
for (let y = 0; y < height; y++) {
  const filter = raw[y * (stride + 1)];
  const row = raw.subarray(y * (stride + 1) + 1, (y + 1) * (stride + 1));
  const out = rgba.subarray(y * stride, (y + 1) * stride);
  const prev = y ? rgba.subarray((y - 1) * stride, y * stride) : null;
  for (let x = 0; x < stride; x++) {
    const left = x >= bpp ? out[x - bpp] : 0, up = prev ? prev[x] : 0, ul = prev && x >= bpp ? prev[x - bpp] : 0;
    out[x] = row[x]
      + (filter === 1 ? left : filter === 2 ? up : filter === 3 ? (left + up) >> 1
      : filter === 4 ? paeth(left, up, ul) : 0);
  }
}

// --- Area-average downsample (premultiplied, so transparent edges stay clean). ---
function downsample(size) {
  const out = Buffer.alloc(size * size * 4);
  for (let ty = 0; ty < size; ty++) {
    for (let tx = 0; tx < size; tx++) {
      const x0 = tx * width / size, x1 = (tx + 1) * width / size;
      const y0 = ty * height / size, y1 = (ty + 1) * height / size;
      let r = 0, g = 0, b = 0, a = 0, area = 0;
      for (let sy = Math.floor(y0); sy < Math.ceil(y1) && sy < height; sy++) {
        for (let sx = Math.floor(x0); sx < Math.ceil(x1) && sx < width; sx++) {
          const w = (Math.min(x1, sx + 1) - Math.max(x0, sx)) * (Math.min(y1, sy + 1) - Math.max(y0, sy));
          const i = (sy * width + sx) * 4, al = rgba[i + 3] / 255;
          r += rgba[i] * al * w; g += rgba[i + 1] * al * w; b += rgba[i + 2] * al * w;
          a += rgba[i + 3] * w; area += w;
        }
      }
      const al = a / area / 255, o = (ty * size + tx) * 4;
      out[o] = al ? Math.round(r / area / al) : 0;
      out[o + 1] = al ? Math.round(g / area / al) : 0;
      out[o + 2] = al ? Math.round(b / area / al) : 0;
      out[o + 3] = Math.round(a / area);
    }
  }
  return out;
}

// --- Pack the ICO: 32bpp BMP entries (BGRA, bottom-up) with zero AND masks. ---
const images = SIZES.map((size) => {
  const src = downsample(size);
  const maskStride = ((size + 31) >> 5) * 4;
  const dib = Buffer.alloc(40 + size * size * 4 + maskStride * size);
  dib.writeUInt32LE(40, 0);            // biSize
  dib.writeInt32LE(size, 4);           // biWidth
  dib.writeInt32LE(size * 2, 8);       // biHeight: pixel rows + AND mask rows
  dib.writeUInt16LE(1, 12);            // biPlanes
  dib.writeUInt16LE(32, 14);           // biBitCount
  dib.writeUInt32LE(dib.length, 20);   // biSizeImage
  for (let y = 0; y < size; y++) {
    const from = (size - 1 - y) * size * 4;   // bottom-up
    dib.set(src.subarray(from, from + size * 4), 40 + y * size * 4);
  }
  return dib;
});

const header = Buffer.alloc(6 + 16 * images.length);
header.writeUInt16LE(1, 2); header.writeUInt16LE(images.length, 4);
let offset = header.length;
images.forEach((dib, i) => {
  const e = 6 + i * 16, size = SIZES[i];
  header[e] = size === 256 ? 0 : size;
  header[e + 1] = size === 256 ? 0 : size;
  header.writeUInt16LE(1, e + 4); header.writeUInt16LE(32, e + 6);
  header.writeUInt32LE(dib.length, e + 8); header.writeUInt32LE(offset, e + 12);
  offset += dib.length;
});
writeFileSync(output, Buffer.concat([header, ...images]));
console.log(`Wrote ${SIZES.join('/')} icon to ${output} (${(header.length + images.reduce((s, d) => s + d.length, 0))} bytes).`);
