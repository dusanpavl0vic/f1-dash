"""SVG diagrams for the architecture report.

Hand-built rather than generated from a graph library: every box here is placed
to make one specific relationship readable, and an auto-layout would reflow them
into something technically correct and visually useless.
"""

INK = "#16181d"; SOFT = "#4a5058"; FAINT = "#7b828c"
RULE = "#d8dce2"; PANEL = "#f6f7f9"; PANEL2 = "#eef1f5"
ACCENT = "#d81f26"; BLUE = "#1f5fd8"; GREEN = "#157f3f"; AMBER = "#b06f00"
MONO = "SF Mono, Menlo, Consolas, monospace"


def box(x, y, w, h, title, lines=(), accent=INK, fill="#ffffff", dash=None):
    d = f' stroke-dasharray="{dash}"' if dash else ""
    out = [f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="{fill}" '
           f'stroke="{accent}" stroke-width="1.4"{d}/>']
    out.append(f'<rect x="{x}" y="{y}" width="{w}" height="3" fill="{accent}"/>')
    out.append(f'<text x="{x+10}" y="{y+21}" font-family="{MONO}" font-size="9.5" '
               f'font-weight="700" fill="{INK}">{title}</text>')
    for i, ln in enumerate(lines):
        out.append(f'<text x="{x+10}" y="{y+37+i*13}" font-family="{MONO}" '
                   f'font-size="8" fill="{SOFT}">{ln}</text>')
    return "".join(out)


def arrow(x1, y1, x2, y2, label=None, colour=INK, dash=None, curve=0, lx=None, ly=None):
    d = f' stroke-dasharray="{dash}"' if dash else ""
    mid = f"M{x1} {y1} L{x2} {y2}"
    if curve:
        cx, cy = (x1 + x2) / 2 + curve, (y1 + y2) / 2
        mid = f"M{x1} {y1} Q{cx} {cy} {x2} {y2}"
    out = [f'<path d="{mid}" fill="none" stroke="{colour}" stroke-width="1.4"{d} '
           f'marker-end="url(#a{colour[1:]})"/>']
    if label:
        tx = lx if lx is not None else (x1 + x2) / 2
        ty = ly if ly is not None else (y1 + y2) / 2 - 6
        out.append(f'<rect x="{tx - len(label)*2.6 - 4}" y="{ty - 8}" '
                   f'width="{len(label)*5.2 + 8}" height="12" fill="#ffffff"/>')
        out.append(f'<text x="{tx}" y="{ty}" font-family="{MONO}" font-size="7.4" '
                   f'fill="{colour}" text-anchor="middle">{label}</text>')
    return "".join(out)


def defs():
    m = []
    for c in (INK, ACCENT, BLUE, GREEN, FAINT, AMBER):
        m.append(f'<marker id="a{c[1:]}" viewBox="0 0 10 10" refX="9" refY="5" '
                 f'markerWidth="5" markerHeight="5" orient="auto-start-reverse">'
                 f'<path d="M0 0 L10 5 L0 10 z" fill="{c}"/></marker>')
    return "<defs>" + "".join(m) + "</defs>"


def label(x, y, text, size=7.4, colour=FAINT, weight="400", anchor="start"):
    return (f'<text x="{x}" y="{y}" font-family="{MONO}" font-size="{size}" '
            f'fill="{colour}" font-weight="{weight}" text-anchor="{anchor}">{text}</text>')


def zone(x, y, w, h, name, colour=RULE):
    return (f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="none" '
            f'stroke="{colour}" stroke-width="1" stroke-dasharray="4 3"/>'
            + label(x + 8, y + 15, name, 7.2, FAINT, "700"))


def svg(w, h, body):
    return (f'<svg viewBox="0 0 {w} {h}" xmlns="http://www.w3.org/2000/svg">'
            + defs() + body + "</svg>")
