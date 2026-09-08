# Architecture report

`docs/Apex-Architecture.pdf` is generated, not hand-edited.

```
python3 build.py                     # writes report.html
"/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" \
  --headless --disable-gpu --no-pdf-header-footer \
  --print-to-pdf=../Apex-Architecture.pdf "file://$PWD/report.html"
```

| File | Contains |
|---|---|
| `build.py` | The prose. One entry per section; `FIG:name` inlines a figure. |
| `figures.py` | The seven diagrams, as hand-placed SVG. |
| `diagrams.py` | Box, arrow, zone and label primitives. |
| `report.css` | Print styles — A4, page breaks, the light palette. |

The diagrams are hand-placed rather than laid out by a graph library. Every box
here is positioned to make one specific relationship readable, and auto-layout
produced something technically correct and visually useless.

Chrome is the renderer because it is already on the machine and it supports the
CSS fragmentation properties (`break-before`, `break-inside`) the layout relies
on. Any engine with the same support would do.

`F1-Dash-Architecture-Report.pdf` is the earlier report, kept for reference. It
describes the superseded stack; `Apex-Architecture.pdf` supersedes it.
