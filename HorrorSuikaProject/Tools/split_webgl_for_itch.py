"""Split oversized Unity WebGL build files so the build fits itch.io's 200 MB per-file limit.

Any file in <build>/Build larger than the chunk size is cut into <name>.part000, .part001, ...
and index.html is patched to download the parts, stitch them into a Blob, and hand Unity the
Blob URL instead of the original file URL. A zip ready for itch upload is written next to the build.

Usage: python Tools/split_webgl_for_itch.py Builds/WebGL [--chunk-mb 190] [--no-zip]
"""

import argparse
import json
import os
import sys
import zipfile

CONFIG_KEYS = ("dataUrl", "frameworkUrl", "codeUrl", "memoryUrl", "symbolsUrl")
LOADER_ANCHOR = "document.body.appendChild(script);"
MARKER = "/* itch-split-loader */"

SPLIT_LOADER_JS = """
      MARKER
      var splitFiles = SPLIT_MANIFEST;
      function loadSplitFiles() {
        var keys = Object.keys(splitFiles);
        if (!keys.length) return Promise.resolve();
        var total = 0, loaded = 0;
        keys.forEach(function (k) { total += splitFiles[k].size; });
        loadingBar.style.display = "block";
        function fetchPart(url) {
          return fetch(url).then(function (response) {
            if (!response.ok) throw new Error("Failed to download " + url + " (" + response.status + ")");
            if (!response.body || !response.body.getReader) {
              return response.arrayBuffer().then(function (buf) { loaded += buf.byteLength; return buf; });
            }
            var reader = response.body.getReader(), chunks = [];
            function pump() {
              return reader.read().then(function (r) {
                if (r.done) return new Blob(chunks);
                chunks.push(r.value);
                loaded += r.value.byteLength;
                // Split downloads fill the first half of the bar; Unity's own load fills the rest.
                progressBarFull.style.width = (50 * loaded / total) + "%";
                return pump();
              });
            }
            return pump();
          });
        }
        return Promise.all(keys.map(function (key) {
          var entry = splitFiles[key];
          return Promise.all(entry.parts.map(function (p) { return fetchPart(buildUrl + "/" + p); }))
            .then(function (parts) {
              var blob = new Blob(parts, { type: entry.type });
              config[key] = URL.createObjectURL(blob);
            });
        }));
      }
"""


def mime_for(filename):
    name = filename.lower()
    if ".wasm" in name:
        return "application/wasm"
    if name.endswith(".js") or ".js." in name:
        return "application/javascript"
    return "application/octet-stream"


def find_config_key(html, filename):
    for key in CONFIG_KEYS:
        if '%s: buildUrl + "/%s"' % (key, filename) in html:
            return key
    return None


def split_file(path, chunk_bytes):
    parts = []
    base = os.path.basename(path)
    with open(path, "rb") as src:
        index = 0
        while True:
            chunk = src.read(chunk_bytes)
            if not chunk:
                break
            part_name = "%s.part%03d" % (base, index)
            with open(os.path.join(os.path.dirname(path), part_name), "wb") as dst:
                dst.write(chunk)
            parts.append(part_name)
            index += 1
    os.remove(path)
    return parts


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("build_dir")
    parser.add_argument("--chunk-mb", type=int, default=190)
    parser.add_argument("--no-zip", action="store_true")
    args = parser.parse_args()

    build_dir = os.path.abspath(args.build_dir)
    index_path = os.path.join(build_dir, "index.html")
    files_dir = os.path.join(build_dir, "Build")
    chunk_bytes = args.chunk_mb * 1024 * 1024

    with open(index_path, "r", encoding="utf-8") as f:
        html = f.read()
    if MARKER in html:
        sys.exit("index.html is already patched; rebuild before splitting again.")
    if LOADER_ANCHOR not in html:
        sys.exit("Could not find '%s' in index.html; template not supported." % LOADER_ANCHOR)

    manifest = {}
    for name in sorted(os.listdir(files_dir)):
        path = os.path.join(files_dir, name)
        size = os.path.getsize(path)
        if size <= chunk_bytes:
            continue
        key = find_config_key(html, name)
        if key is None:
            sys.exit("%s is over the limit but is not referenced in the index.html config." % name)
        parts = split_file(path, chunk_bytes)
        manifest[key] = {"parts": parts, "size": size, "type": mime_for(name)}
        print("Split %s (%.1f MB) into %d parts -> config.%s" % (name, size / 1048576, len(parts), key))

    if not manifest:
        print("No file exceeds %d MB; nothing to split." % args.chunk_mb)
    else:
        loader = SPLIT_LOADER_JS.replace("MARKER", MARKER).replace("SPLIT_MANIFEST", json.dumps(manifest))
        html = html.replace("      var script = document.createElement(\"script\");",
                            loader + "\n      var script = document.createElement(\"script\");", 1)
        html = html.replace(
            LOADER_ANCHOR,
            "loadSplitFiles().then(function () { %s }).catch(function (e) { unityShowBanner(String(e), \"error\"); });"
            % LOADER_ANCHOR, 1)
        with open(index_path, "w", encoding="utf-8") as f:
            f.write(html)
        print("Patched index.html")

    total = 0
    count = 0
    biggest = 0
    for root, _, files in os.walk(build_dir):
        for name in files:
            s = os.path.getsize(os.path.join(root, name))
            total += s
            count += 1
            biggest = max(biggest, s)
    print("Build: %d files, %.1f MB total, largest file %.1f MB" % (count, total / 1048576, biggest / 1048576))

    if not args.no_zip:
        zip_path = build_dir.rstrip("\\/") + "_itch.zip"
        with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_STORED) as z:
            for root, _, files in os.walk(build_dir):
                for name in files:
                    full = os.path.join(root, name)
                    z.write(full, os.path.relpath(full, build_dir))
        print("Wrote %s (%.1f MB)" % (zip_path, os.path.getsize(zip_path) / 1048576))


if __name__ == "__main__":
    main()
