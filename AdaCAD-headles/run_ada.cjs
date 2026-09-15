
/*
 * Headless AdaCAD workspace runner
 * --------------------------------
 *
 * Loads a .ada workspace, rebuilds the operation graph and executes
 * operations via adacad-drafting-lib.
 *
 * Terminal imagemap is rendered directly to PNG row-by-row.
 *
 * IMPORTANT:
 *   - No JPG conversion
 *   - No huge imagemap Draft
 *   - No huge output PNG buffer
 *   - Input image is read once
 *   - Output is written progressively, one row at a time
 *
 * Usage:
 *
 *   node run_ada.cjs <workspace.ada> <image.png> <out.png> <width> <height>
 *
 * Example:
 *
 *   node run_ada.cjs \
 *     config/4_layer_HM.ada \
 *     images/4layer_120ppcm.png \
 *     out.png \
 *     8840 \
 *     28320
 */

const fs = require("fs");
const zlib = require("zlib");
const { PNG } = require("pngjs");
const ada = require("adacad-drafting-lib");

const { getOp, call } = ada;
const { warps, wefts, isUp } = ada;


// ============================================================================
// 0. ARGUMENTS
// ============================================================================

const [
  ,
  ,
  ADA_PATH,
  IMG_PATH,
  OUT_PATH,
  RES_W,
  RES_H
] = process.argv;


if (!ADA_PATH || !IMG_PATH || !OUT_PATH) {

  console.error(
    "Usage: node run_ada.cjs <workspace.ada> <image.png> <out.png> <width> <height>"
  );

  process.exit(1);
}


const ovrW = RES_W
  ? parseInt(RES_W, 10)
  : null;

const ovrH = RES_H
  ? parseInt(RES_H, 10)
  : null;


function log(...args) {
  console.log(...args);
}


// ============================================================================
// 1. LOAD WORKSPACE
// ============================================================================

const ws = JSON.parse(
  fs.readFileSync(
    ADA_PATH,
    "utf8"
  )
);


const opMap = new Map(
  ws.ops.map(
    o => [o.node_id, o]
  )
);


const treeMap = new Map(
  ws.tree.map(
    t => [t.node, t]
  )
);


// draft node id -> computed Draft
const computed = new Map();


// ============================================================================
// 2. PRELOAD STORED SEED DRAFTS
// ============================================================================

const {
  unpackDrawdownFromArray,
  initDraftFromDrawdown
} = ada;


const producedBy = new Set();


for (const id of opMap.keys()) {

  const t = treeMap.get(id);

  if (!t) continue;

  for (const o of t.outputs || []) {

    const cxn = treeMap.get(o.tn);

    const dst =
      cxn &&
      (cxn.outputs || [])[0];

    if (dst) {
      producedBy.add(dst.tn);
    }
  }
}


let seeds = 0;


for (const dn of ws.draft_nodes || []) {

  if (producedBy.has(dn.node_id)) {
    continue;
  }

  const cd = dn.compressed_draft;

  if (
    !cd ||
    !cd.compressed_drawdown
  ) {
    continue;
  }


  try {

    const dd =
      unpackDrawdownFromArray(
        cd.compressed_drawdown,
        cd.warps,
        cd.wefts
      );


    const draft =
      initDraftFromDrawdown(dd);


    draft.id = cd.id;


    for (
      const m of [
        "rowSystemMapping",
        "colSystemMapping",
        "rowShuttleMapping",
        "colShuttleMapping"
      ]
    ) {

      if (cd[m]) {
        draft[m] = cd[m];
      }
    }


    computed.set(
      dn.node_id,
      draft
    );


    seeds++;

  } catch (e) {

    // Ignore unreadable seed draft.
  }
}


// ============================================================================
// 3. INPUT PNG
// ============================================================================

let _srcPng = null;


function srcPng() {

  if (!_srcPng) {

    console.log(
      "Reading source PNG..."
    );


    _srcPng =
      PNG.sync.read(
        fs.readFileSync(
          IMG_PATH
        )
      );


    console.log(
      "Source image: " +
      _srcPng.width +
      " x " +
      _srcPng.height
    );


    console.log(
      "RGBA memory: " +
      (
        _srcPng.data.length /
        1024 /
        1024
      ).toFixed(1) +
      " MB"
    );
  }


  return _srcPng;
}


// ============================================================================
// 4. COLOUR PALETTE
// ============================================================================

const storedImageData =
  ws.indexed_image_data &&
  ws.indexed_image_data[0];


if (!storedImageData) {

  throw new Error(
    "Workspace has no indexed_image_data."
  );
}


const colors =
  storedImageData.colors;


const colorMapping =
  storedImageData.color_mapping;


const palette =
  colors.map(
    c => [
      c.r,
      c.g,
      c.b
    ]
  );


// Cache nearest-colour results.
//
// This is useful because large textile images often contain large areas
// of the same RGB values.

const nearestCache =
  new Map();


function nearestPaletteColour(
  r,
  g,
  b
) {

  const key =
    (
      r << 16
    ) |
    (
      g << 8
    ) |
    b;


  const cached =
    nearestCache.get(key);


  if (cached !== undefined) {
    return cached;
  }


  let best = 0;
  let bestDistance = Infinity;


  for (
    let k = 0;
    k < palette.length;
    k++
  ) {

    const dr =
      r - palette[k][0];

    const dg =
      g - palette[k][1];

    const db =
      b - palette[k][2];


    const distance =
      dr * dr +
      dg * dg +
      db * db;


    if (
      distance <
      bestDistance
    ) {

      bestDistance =
        distance;

      best = k;
    }
  }


  nearestCache.set(
    key,
    best
  );


  return best;
}


// ============================================================================
// 5. GRAPH HELPERS
// ============================================================================

function inputDraftNodes(opId) {

  const t =
    treeMap.get(opId);


  const groups =
    new Map();


  if (!t) {
    return groups;
  }


  for (
    const inp of t.inputs || []
  ) {

    const cxn =
      treeMap.get(inp.tn);


    if (!cxn) {
      continue;
    }


    const src =
      (cxn.inputs || [])[0];


    if (!src) {
      continue;
    }


    if (!groups.has(inp.ndx)) {

      groups.set(
        inp.ndx,
        []
      );
    }


    groups
      .get(inp.ndx)
      .push(src.tn);
  }


  return groups;
}


function outputDraftNodes(opId) {

  const t =
    treeMap.get(opId);


  const outs = [];


  if (!t) {
    return outs;
  }


  for (
    const o of t.outputs || []
  ) {

    const cxn =
      treeMap.get(o.tn);


    if (!cxn) {
      continue;
    }


    const dst =
      (cxn.outputs || [])[0];


    if (dst) {
      outs.push(dst.tn);
    }
  }


  return outs;
}


// ============================================================================
// 6. DOWNSTREAM CHECK
// ============================================================================

function hasDownstreamOperation(
  opId
) {

  const t =
    treeMap.get(opId);


  if (!t) {
    return false;
  }


  for (
    const o of t.outputs || []
  ) {

    const cxn =
      treeMap.get(o.tn);


    if (!cxn) {
      continue;
    }


    for (
      const dst of
      cxn.outputs || []
    ) {

      if (
        opMap.has(dst.tn)
      ) {

        return true;
      }
    }
  }


  return false;
}


// ============================================================================
// 7. TOPOLOGICAL ORDER
// ============================================================================

function topoOps() {

  const opIds =
    [...opMap.keys()];


  const deps =
    new Map();


  for (
    const id of opIds
  ) {

    const set =
      new Set();


    for (
      const [, dns] of
      inputDraftNodes(id)
    ) {

      for (
        const dn of dns
      ) {

        const parent =
          (
            treeMap.get(dn) ||
            {}
          ).parent;


        if (
          parent != null &&
          parent !== -1 &&
          opMap.has(parent)
        ) {

          set.add(parent);
        }
      }
    }


    deps.set(
      id,
      set
    );
  }


  const order = [];
  const done = new Set();


  let guard = 0;


  while (
    order.length <
      opIds.length &&
    guard++ <
      opIds.length + 5
  ) {

    for (
      const id of opIds
    ) {

      if (
        done.has(id)
      ) {
        continue;
      }


      if (
        [...deps.get(id)]
          .every(
            d => done.has(d)
          )
      ) {

        order.push(id);
        done.add(id);
      }
    }
  }


  return {
    order,
    complete:
      order.length ===
      opIds.length
  };
}


// ============================================================================
// 8. FIND STRUCTURE FOR COLOUR
// ============================================================================

function findDraftForColour(
  opId,
  colourIndex
) {

  const groups =
    inputDraftNodes(opId);


  const target =
    colors[colourIndex];


  if (!target) {
    return null;
  }


  /*
   * IMPORTANT:
   *
   * We preserve the AdaCAD colour -> draft relationship.
   *
   * The colour index from the image is mapped through
   * indexed_image_data.color_mapping and then matched to the
   * appropriate input draft.
   */

  for (
    const [ndx, dns] of
    [...groups.entries()]
      .sort(
        (a, b) =>
          a[0] - b[0]
      )
  ) {

    const node =
      opMap.get(opId);


    const inlet =
      node &&
      node.inlets
        ? node.inlets[ndx]
        : null;


    let matches = false;


    if (
      typeof inlet ===
      "string"
    ) {

      matches =
        inlet === target.hex ||
        inlet === target.name;
    }


    if (
      inlet &&
      typeof inlet ===
      "object"
    ) {

      if (
        inlet.hex &&
        inlet.hex === target.hex
      ) {

        matches = true;
      }


      if (
        inlet.r === target.r &&
        inlet.g === target.g &&
        inlet.b === target.b
      ) {

        matches = true;
      }
    }


    if (matches) {

      const draft =
        dns
          .map(
            d =>
              computed.get(d)
          )
          .find(Boolean);


      if (draft) {
        return draft;
      }
    }
  }


  return null;
}


// ============================================================================
// 9. COLOUR MAPPING
// ============================================================================

function mappedColourIndex(
  sourceIndex
) {

  if (!colorMapping) {
    return sourceIndex;
  }


  /*
   * AdaCAD's mapping can be represented in a few slightly different
   * ways depending on the workspace version.
   *
   * Handle both object-style and array-style mappings.
   */

  if (
    Array.isArray(colorMapping)
  ) {

    const mapping =
      colorMapping.find(
        m =>
          m &&
          (
            m.from === sourceIndex ||
            m.source === sourceIndex ||
            m.index === sourceIndex
          )
      );


    if (mapping) {

      if (
        mapping.to !== undefined
      ) {

        return mapping.to;
      }


      if (
        mapping.target !== undefined
      ) {

        return mapping.target;
      }
    }
  }


  if (
    typeof colorMapping ===
    "object"
  ) {

    const mapped =
      colorMapping[sourceIndex];


    if (
      mapped !== undefined
    ) {

      return mapped;
    }
  }


  return sourceIndex;
}


// ============================================================================
// 10. PNG CHUNK
// ============================================================================

function pngChunk(
  type,
  data
) {

  const typeBuffer =
    Buffer.from(type);


  const lengthBuffer =
    Buffer.alloc(4);


  lengthBuffer.writeUInt32BE(
    data.length,
    0
  );


  /*
   * PNG CRC32 implementation.
   *
   * Avoids requiring another npm package.
   */

  let crc = 0xffffffff;


  const crcData =
    Buffer.concat([
      typeBuffer,
      data
    ]);


  for (
    let i = 0;
    i < crcData.length;
    i++
  ) {

    crc ^= crcData[i];


    for (
      let k = 0;
      k < 8;
      k++
    ) {

      crc =
        (
          crc >>> 1
        ) ^
        (
          0xedb88320 &
          -(
            crc & 1
          )
        );
    }
  }


  crc =
    (
      crc ^
      0xffffffff
    ) >>> 0;


  const crcBuffer =
    Buffer.alloc(4);


  crcBuffer.writeUInt32BE(
    crc,
    0
  );


  return Buffer.concat([
    lengthBuffer,
    typeBuffer,
    data,
    crcBuffer
  ]);
}


// ============================================================================
// 11. STREAM PNG
// ============================================================================

function writePNGRows(
  outPath,
  width,
  height,
  makeRow
) {

  return new Promise(
    (resolve, reject) => {

      const stream =
        fs.createWriteStream(
          outPath
        );


      stream.on(
        "error",
        reject
      );


      // PNG signature

      stream.write(
        Buffer.from([
          137, 80, 78, 71,
          13, 10, 26, 10
        ])
      );


      // IHDR

      const ihdr =
        Buffer.alloc(13);


      ihdr.writeUInt32BE(
        width,
        0
      );


      ihdr.writeUInt32BE(
        height,
        4
      );


      // 8-bit RGBA

      ihdr[8] = 8;
      ihdr[9] = 6;
      ihdr[10] = 0;
      ihdr[11] = 0;
      ihdr[12] = 0;


      stream.write(
        pngChunk(
          "IHDR",
          ihdr
        )
      );


      const deflate =
        zlib.createDeflate({
          level: 6
        });


      deflate.on(
        "error",
        reject
      );


      deflate.on(
        "data",
        chunk => {

          stream.write(
            pngChunk(
              "IDAT",
              chunk
            )
          );
        }
      );


      deflate.on(
        "end",
        () => {

          stream.write(
            pngChunk(
              "IEND",
              Buffer.alloc(0)
            )
          );


          stream.end(
            () => resolve()
          );
        }
      );


      try {

        for (
          let y = 0;
          y < height;
          y++
        ) {

          const row =
            makeRow(y);


          /*
           * One scanline:
           *
           *   filter byte + RGBA pixels
           */

          const scanline =
            Buffer.alloc(
              1 +
              width * 4
            );


          scanline[0] = 0;


          row.copy(
            scanline,
            1
          );


          deflate.write(
            scanline
          );


          if (
            y === 0 ||
            y === height - 1 ||
            y %
              Math.max(
                1,
                Math.floor(
                  height / 20
                )
              ) === 0
          ) {

            console.log(
              "  row " +
              (y + 1) +
              "/" +
              height
            );
          }
        }


        deflate.end();

      } catch (err) {

        reject(err);

        deflate.destroy();
        stream.destroy();
      }
    }
  );
}


// ============================================================================
// 12. RENDER TERMINAL IMAGEMAP
// ============================================================================

async function renderImagemap(
  opId,
  width,
  height
) {

  const png =
    srcPng();


  console.log(
    "----------------------------------------"
  );


  console.log(
    "Streaming terminal imagemap"
  );


  console.log(
    "Output: " +
    width +
    " x " +
    height
  );


  console.log(
    "----------------------------------------"
  );


  /*
   * Cache the actual AdaCAD structure associated with each colour.
   *
   * We only store one Draft reference per colour.
   *
   * We DO NOT construct an 8840 x 28320 drawdown.
   */

  const draftCache =
    new Map();


  await writePNGRows(
    OUT_PATH,
    width,
    height,
    (y) => {

      /*
       * Only ONE output row exists in memory here.
       */

      const row =
        Buffer.alloc(
          width * 4
        );


      /*
       * Map output row -> source image row.
       */

      const sourceY =
        Math.floor(
          y *
          png.height /
          height
        );


      for (
        let x = 0;
        x < width;
        x++
      ) {

        /*
         * Map output column -> source image column.
         */

        const sourceX =
          Math.floor(
            x *
            png.width /
            width
          );


        const sourceIndex =
          (
            sourceY *
            png.width +
            sourceX
          ) * 4;


        /*
         * Determine which AdaCAD colour this pixel represents.
         */

        const sourceColour =
          nearestPaletteColour(
            png.data[sourceIndex],
            png.data[
              sourceIndex + 1
            ],
            png.data[
              sourceIndex + 2
            ]
          );


        const mappedColour =
          mappedColourIndex(
            sourceColour
          );


        /*
         * Find the AdaCAD structure for this colour.
         *
         * This is cached, so the graph is not searched for every pixel.
         */

        let draft =
          draftCache.get(
            mappedColour
          );


        if (
          draft === undefined
        ) {

          draft =
            findDraftForColour(
              opId,
              mappedColour
            );


          draftCache.set(
            mappedColour,
            draft || null
          );
        }


        let up = false;


        /*
         * Apply the colour's original AdaCAD structure.
         */

        if (
          draft &&
          draft.drawdown
        ) {

          const dw =
            warps(
              draft.drawdown
            );


          const dh =
            wefts(
              draft.drawdown
            );


          if (
            dw > 0 &&
            dh > 0
          ) {

            const localY =
              y % dh;


            const localX =
              x % dw;


            up =
              isUp(
                draft.drawdown,
                localY,
                localX
              );
          }
        }


        const value =
          up
            ? 0
            : 255;


        const k =
          x * 4;


        row[k] =
          value;


        row[k + 1] =
          value;


        row[k + 2] =
          value;


        row[k + 3] =
          255;
      }


      return row;
    }
  );


  console.log(
    "----------------------------------------"
  );


  console.log(
    "Finished:"
  );


  console.log(
    OUT_PATH
  );


  console.log(
    "----------------------------------------"
  );
}


// ============================================================================
// 13. MAIN
// ============================================================================

(async () => {

  const {
    order,
    complete
  } = topoOps();


  log(
    "graph: " +
    ws.ops.length +
    " ops, " +
    ws.draft_nodes.length +
    " drafts, " +
    ws.nodes.filter(
      n => n.type === "cxn"
    ).length +
    " connections"
  );


  log(
    "preloaded " +
    seeds +
    " stored seed draft(s)"
  );


  log(
    "topo order resolved for " +
    order.length +
    "/" +
    ws.ops.length +
    " ops" +
    (
      complete
        ? ""
        : " (cycle or missing dep!)"
    )
  );


  log("---");


  let ran = 0;

  const failed = [];


  // ========================================================================
  // EXECUTE OPERATIONS
  // ========================================================================

  for (
    const opId of order
  ) {

    const node =
      opMap.get(opId);


    const op =
      getOp(node.name);


    if (!op) {

      failed.push([
        node.name,
        "no such op in lib"
      ]);

      continue;
    }


    /*
     * TERMINAL IMAGEMAP
     *
     * Do NOT call imagemap.
     *
     * We need the graph information around it, but we don't want AdaCAD
     * to construct the enormous terminal drawdown.
     */

    if (
      node.name === "imagemap" &&
      !hasDownstreamOperation(opId)
    ) {

      log(
        "SKIP imagemap -> " +
        "will render row-by-row"
      );


      ran++;

      continue;
    }


    // ----------------------------------------------------------------------
    // NORMAL OPERATION
    // ----------------------------------------------------------------------

    const groups =
      inputDraftNodes(opId);


    const inlets = [];


    for (
      const [ndx, dns] of
      [...groups.entries()]
        .sort(
          (a, b) =>
            a[0] - b[0]
        )
    ) {

      const drafts =
        dns
          .map(
            d =>
              computed.get(d)
          )
          .filter(Boolean);


      inlets.push({
        drafts,
        inlet_params: [
          node.inlets[ndx]
        ],
        inlet_id: ndx
      });
    }


    let params =
      node.params.slice();


    /*
     * If imagemap is NOT terminal, retain the original behaviour.
     *
     * This path should normally not be reached for your large final image.
     */

    if (
      node.name === "imagemap"
    ) {

      const png =
        srcPng();


      const scale = 4;


      const sw =
        Math.ceil(
          png.width / scale
        );


      const sh =
        Math.ceil(
          png.height / scale
        );


      const image_map =
        new Array(sh);


      for (
        let y = 0;
        y < sh;
        y++
      ) {

        const row =
          new Array(sw);


        const sourceY =
          Math.min(
            y * scale,
            png.height - 1
          );


        for (
          let x = 0;
          x < sw;
          x++
        ) {

          const sourceX =
            Math.min(
              x * scale,
              png.width - 1
            );


          const idx =
            (
              sourceY *
              png.width +
              sourceX
            ) * 4;


          row[x] =
            nearestPaletteColour(
              png.data[idx],
              png.data[idx + 1],
              png.data[idx + 2]
            );
        }


        image_map[y] =
          row;
      }


      const analyzed = {

        name: "image.png",

        data: null,
        image: null,

        colors,

        colors_mapping:
          colorMapping,

        proximity_map: [],

        image_map,

        width: sw,
        height: sh,

        type: "png",

        warning: ""
      };


      params = [
        {
          id:
            String(
              node.params[0] ||
              "img"
            ),

          data:
            analyzed
        },

        ovrW ||
          sw,

        ovrH ||
          sh
      ];
    }


    try {

      const outputs =
        await call(
          op,
          params,
          inlets
        );


      const outNodes =
        outputDraftNodes(opId);


      outputs.forEach(
        (out, i) => {

          if (
            out.draft &&
            outNodes[i] != null
          ) {

            computed.set(
              outNodes[i],
              out.draft
            );
          }
        }
      );


      const dd =
        outputs[0] &&
        outputs[0].draft &&
        outputs[0].draft.drawdown;


      ran++;


      log(
        "ok  " +
        node.name.padEnd(16) +
        " -> " +
        (
          dd
            ? (
                warps(dd) +
                "x" +
                wefts(dd) +
                " draft"
              )
            : "(no drawdown)"
        )
      );


    } catch (e) {

      failed.push([
        node.name,
        e.message
      ]);


      log(
        "FAIL " +
        node.name.padEnd(15) +
        " " +
        e.message
      );
    }
  }


  // ========================================================================
  // SUMMARY
  // ========================================================================

  log("---");


  log(
    "executed " +
    ran +
    "/" +
    ws.ops.length +
    " ops; " +
    failed.length +
    " failed"
  );


  // ========================================================================
  // FIND TERMINAL IMAGEMAP
  // ========================================================================

  const terminalImagemaps =
    order.filter(
      opId => {

        const node =
          opMap.get(opId);


        return (
          node &&
          node.name === "imagemap" &&
          !hasDownstreamOperation(
            opId
          )
        );
      }
    );


  if (
    terminalImagemaps.length
  ) {

    const opId =
      terminalImagemaps[
        terminalImagemaps.length - 1
      ];


    const png =
      srcPng();


    /*
     * IMPORTANT:
     *
     * Width and height are independently taken from the command line.
     *
     * For your image:
     *
     *   source = 8840 x 28320
     *
     * and:
     *
     *   output = 8840 x 28320
     *
     * use:
     *
     *   ... out.png 8840 28320
     */

    const width =
      ovrW ||
      png.width;


    const height =
      ovrH ||
      png.height;


    await renderImagemap(
      opId,
      width,
      height
    );


    return;
  }


  // ========================================================================
  // NORMAL TERMINAL DRAFT FALLBACK
  // ========================================================================

  const consumed =
    new Set();


  for (
    const id of opMap.keys()
  ) {

    for (
      const [, dns] of
      inputDraftNodes(id)
    ) {

      dns.forEach(
        d =>
          consumed.add(d)
      );
    }
  }


  const terminals =
    [...computed.keys()]
      .filter(
        d =>
          !consumed.has(d)
      );


  let best = null;


  for (
    const d of
    (
      terminals.length
        ? terminals
        : [...computed.keys()]
    )
  ) {

    const dr =
      computed.get(d);


    if (
      dr &&
      dr.drawdown &&
      (
        !best ||
        warps(dr.drawdown) *
        wefts(dr.drawdown) >
        warps(best.drawdown) *
        wefts(best.drawdown)
      )
    ) {

      best = dr;
    }
  }


  if (!best) {

    log(
      "no terminal draft produced"
    );

    return;
  }


  const W =
    warps(
      best.drawdown
    );


  const H =
    wefts(
      best.drawdown
    );


  log(
    "terminal draft: " +
    W +
    " x " +
    H
  );


  const out =
    new PNG({
      width: W,
      height: H
    });


  for (
    let y = 0;
    y < H;
    y++
  ) {

    for (
      let x = 0;
      x < W;
      x++
    ) {

      const value =
        isUp(
          best.drawdown,
          y,
          x
        )
          ? 0
          : 255;


      const k =
        (
          y * W +
          x
        ) * 4;


      out.data[k] =
        value;

      out.data[k + 1] =
        value;

      out.data[k + 2] =
        value;

      out.data[k + 3] =
        255;
    }
  }


  fs.writeFileSync(
    OUT_PATH,
    PNG.sync.write(out)
  );


  log(
    "wrote " +
    OUT_PATH
  );

})();

