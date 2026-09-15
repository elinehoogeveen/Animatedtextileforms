# AdaCAD Headless Runner

Runs an AdaCAD `.ada` workspace without the browser and exports the woven draft as a bitmap. Lets you process drafts too large for the AdaCAD web app to render.

## Install

Requires Node 18+.

```bash
npm install adacad-drafting-lib pngjs
```

## Run

```bash
node --max-old-space-size=12288 run_ada.cjs config/4_layer_system.ada images/newemotions.png out.png
```

Arguments: `<workspace.ada> <image.png> <output.png> [ends] [picks]`

- Output defaults to the image's native resolution (no downsampling). Pass `ends picks` only to force a different size.
- `--max-old-space-size` raises Node's heap limit (MB); full-resolution drafts need it. Set it below your available RAM.