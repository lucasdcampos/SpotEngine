// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { dotnet } from './_framework/dotnet.js'

// --- Canvas + WebGL2 context (no Emscripten GL emulation involved) ---
const canvas = document.getElementById('canvas');
const gl = canvas.getContext('webgl2', { antialias: true });
if (!gl) {
    document.body.insertAdjacentHTML('beforeend', '<p style="color:red">WebGL2 not available in this browser.</p>');
    throw new Error('WebGL2 not supported');
}
console.log('[spike] WebGL2 context acquired:', gl.getParameter(gl.VERSION));

// JS-side handle table: C# refers to GL objects by integer index.
const objs = [null];
const reg = (o) => { objs.push(o); return objs.length - 1; };

const glImports = {
    viewport: (x, y, w, h) => gl.viewport(x, y, w, h),
    clearColor: (r, g, b, a) => gl.clearColor(r, g, b, a),
    clear: (mask) => gl.clear(mask),
    enable: (cap) => gl.enable(cap),

    createShader: (type) => reg(gl.createShader(type)),
    shaderSource: (s, src) => gl.shaderSource(objs[s], src),
    compileShader: (s) => gl.compileShader(objs[s]),
    getShaderCompileStatus: (s) => gl.getShaderParameter(objs[s], gl.COMPILE_STATUS),
    getShaderInfoLog: (s) => gl.getShaderInfoLog(objs[s]) ?? '',

    createProgram: () => reg(gl.createProgram()),
    attachShader: (p, s) => gl.attachShader(objs[p], objs[s]),
    linkProgram: (p) => gl.linkProgram(objs[p]),
    getProgramLinkStatus: (p) => gl.getProgramParameter(objs[p], gl.LINK_STATUS),
    getProgramInfoLog: (p) => gl.getProgramInfoLog(objs[p]) ?? '',
    useProgram: (p) => gl.useProgram(objs[p]),
    getUniformLocation: (p, name) => reg(gl.getUniformLocation(objs[p], name)),
    uniform1f: (loc, v) => gl.uniform1f(objs[loc], v),
    uniform3f: (loc, r, g, b) => gl.uniform3f(objs[loc], r, g, b),

    createBuffer: () => reg(gl.createBuffer()),
    bindBuffer: (target, b) => gl.bindBuffer(target, objs[b]),
    bufferData: (target, data, usage) => gl.bufferData(target, new Float32Array(data), usage),

    createVertexArray: () => reg(gl.createVertexArray()),
    bindVertexArray: (v) => gl.bindVertexArray(objs[v]),
    enableVertexAttribArray: (i) => gl.enableVertexAttribArray(i),
    vertexAttribPointer: (i, size, type, normalized, stride, offset) =>
        gl.vertexAttribPointer(i, size, type, normalized, stride, offset),

    drawArrays: (mode, first, count) => gl.drawArrays(mode, first, count),
};

const { setModuleImports, getAssemblyExports, getConfig, runMain } = await dotnet.create();

setModuleImports('main.js', { gl: glImports });

const config = getConfig();
const exports = await getAssemblyExports(config.mainAssemblyName);

// Run C# Main() (prints hello + managed-dependency smoke test), then drive rendering.
await runMain();

exports.Spike.Init(canvas.width, canvas.height);

let last = performance.now();
function frame(now) {
    const dt = (now - last) / 1000;
    last = now;
    exports.Spike.Frame(dt);
    requestAnimationFrame(frame);
}
requestAnimationFrame(frame);
