"""The six figures. Each answers one question the prose cannot answer as fast."""
from diagrams import *


def fig_system():
    """Everything, and which way each arrow points."""
    b = [label(14, 22, "WHO TALKS TO WHOM", 9, INK, "700")]

    b.append(zone(14, 40, 216, 306, "EXTERNAL / INTERNET"))
    b.append(zone(254, 40, 250, 306, "APEX HOST — DOCKER"))
    b.append(zone(528, 40, 168, 306, "STORAGE — VOLUME"))

    # --- external, top three are upstream; browser is downstream -----------
    b.append(box(28, 64, 188, 56, "F1 LIVE TIMING",
                 ["SignalR Core  · websocket", "static archive · https + CDN"], ACCENT))
    b.append(box(28, 134, 188, 42, "JOLPICA (ERGAST)",
                 ["schedule, results, standings"], BLUE))
    b.append(box(28, 190, 188, 42, "MULTIVIEWER API",
                 ["circuit geometry"], BLUE))
    b.append(box(28, 272, 188, 56, "BROWSER",
                 ["React 19 SPA", "one long-lived session"], INK))

    # --- host --------------------------------------------------------------
    b.append(box(268, 64, 222, 130, "BACKEND — .NET 9",
                 ["Ingest    ISessionSource", "Merge     StateAccumulator",
                  "Realtime  LiveSessionState", "Catalog   REST endpoints",
                  "Analysis  AnalysisBuilder"], ACCENT, PANEL))
    b.append(box(268, 272, 222, 56, "WEB — nginx",
                 ["the built SPA, served static", "port 3000"], INK, PANEL))

    # --- storage -----------------------------------------------------------
    b.append(box(542, 64, 140, 96, "./data/archive",
                 ["stream.jsonl", "analysis/*.json", "telemetry/*.jsonl",
                  "track-cache/"], GREEN, PANEL))
    b.append(box(542, 178, 140, 44, "NO DATABASE",
                 ["by measurement — §7"], GREEN, "#ffffff", "3 3"))

    # --- upstream in, at three separate heights so nothing crosses ---------
    b.append(arrow(216, 86, 268, 92, "feed", ACCENT, lx=242, ly=80))
    b.append(arrow(216, 152, 268, 132, "REST", BLUE, lx=242, ly=138))
    b.append(arrow(216, 208, 268, 168, "geometry", BLUE, lx=246, ly=196))

    # --- browser: one horizontal line for the page, one diagonal for data --
    b.append(arrow(216, 300, 268, 300, "page", INK, lx=242, ly=294))
    b.append(arrow(216, 284, 268, 186, "ws + REST · 4000", INK, lx=196, ly=250))

    # --- storage -----------------------------------------------------------
    b.append(arrow(490, 110, 542, 104, "read/write", GREEN, lx=516, ly=99))
    return svg(700, 356, "".join(b))


def fig_ingest():
    """One delta, from the wire to the screen."""
    b = [label(14, 22, "THE PATH OF ONE UPDATE", 9, INK, "700")]

    stages = [
        (14,  "SOURCE",       ["ISessionSource", "replay | live | poll"], ACCENT),
        (128, "INFLATE",      ["raw DEFLATE", "not zlib"], INK),
        (242, "PARSE",        ["FrameParser", "topic + payload"], INK),
        (356, "MERGE",        ["StateAccumulator", "returns the DELTA"], ACCENT),
        (470, "FAN OUT",      ["one serialise,", "N socket writes"], INK),
        (584, "BROWSER",      ["merge.ts", "same 5 rules"], BLUE),
    ]
    for x, t, lines, c in stages:
        b.append(box(x, 44, 100, 62, t, lines, c, PANEL if c is ACCENT else "#ffffff"))
        if x < 584:
            b.append(arrow(x + 100, 75, x + 114, 75, None, FAINT))

    # side effects
    b.append(box(242, 140, 214, 46, "ANALYSIS + TELEMETRY",
                 ["observe the accumulator, never a clone"], GREEN, PANEL))
    b.append(arrow(406, 106, 350, 140, None, GREEN))

    b.append(box(470, 140, 214, 46, "DELTA BACKLOG — 600 frames",
                 ["≈ 1 min, so a reconnect resumes"], AMBER, PANEL))
    b.append(arrow(520, 106, 520, 140, None, AMBER))

    b.append(label(14, 210, "The browser runs the SAME merge algorithm the server used to produce the delta.", 8, SOFT))
    b.append(label(14, 224, "Both are tested against one shared fixture set, so they cannot drift apart.", 8, SOFT))
    return svg(700, 236, "".join(b))


def fig_merge():
    """Why the merge is the most important code in the project."""
    b = [label(14, 22, "DELTA MERGE — STATE IS NEVER SENT WHOLE", 9, INK, "700")]

    b.append(label(14, 48, "HELD STATE", 7.6, FAINT, "700"))
    b.append(f'<rect x="14" y="56" width="200" height="96" fill="{PANEL}" stroke="{RULE}"/>')
    for i, ln in enumerate(['{', '  "Lines": {', '    "1": {', '      "Position": "3",',
                            '      "GapToLeader": "+4.1"', '    }', '  }', '}']):
        b.append(f'<text x="24" y="{74+i*11}" font-family="{MONO}" font-size="7.6" fill="{SOFT}">{ln}</text>')

    b.append(label(250, 48, "ARRIVING DELTA", 7.6, ACCENT, "700"))
    b.append(f'<rect x="250" y="56" width="200" height="96" fill="#fff" stroke="{ACCENT}"/>')
    for i, ln in enumerate(['{', '  "Lines": {', '    "1": {', '      "Position": "2"',
                            '    }', '  }', '}']):
        b.append(f'<text x="260" y="{74+i*11}" font-family="{MONO}" font-size="7.6" fill="{ACCENT}">{ln}</text>')

    b.append(label(486, 48, "RESULT", 7.6, GREEN, "700"))
    b.append(f'<rect x="486" y="56" width="200" height="96" fill="{PANEL}" stroke="{GREEN}"/>')
    for i, ln in enumerate(['{', '  "Lines": {', '    "1": {', '      "Position": "2",',
                            '      "GapToLeader": "+4.1"', '    }', '  }', '}']):
        colour = GREEN if i == 3 else SOFT
        b.append(f'<text x="496" y="{74+i*11}" font-family="{MONO}" font-size="7.6" fill="{colour}">{ln}</text>')

    b.append(arrow(214, 104, 250, 104, None, FAINT))
    b.append(arrow(450, 104, 486, 104, None, FAINT))

    b.append(label(14, 178, "GapToLeader survives — it was not mentioned, so it was not changed.", 8, INK, "700"))
    b.append(label(14, 194, "That is the whole contract: absent means untouched, never means null.", 8, SOFT))

    b.append(f'<rect x="14" y="208" width="672" height="34" fill="{PANEL}" stroke="{RULE}"/>')
    b.append(label(24, 222, "TRAP  A JsonNode may have only ONE parent. Every assignment deep-clones,", 7.6, ACCENT, "700"))
    b.append(label(24, 234, "or the merge silently reparents nodes out of the state it is building.", 7.6, SOFT))
    return svg(700, 252, "".join(b))


def fig_sources():
    """Three ways in, one interface."""
    b = [label(14, 22, "THREE INGEST SOURCES, ONE INTERFACE", 9, INK, "700")]

    b.append(box(240, 46, 220, 44, "ISessionSource",
                 ["IAsyncEnumerable<TopicUpdate>"], ACCENT, PANEL))

    opts = [
        (14, "REPLAY", ["stream.jsonl on disk", "paced to session time",
                        "seek = restart at offset"], GREEN, "works always"),
        (250, "LIVE — SIGNALR", ["negotiate + websocket", "lowest latency",
                                 "origin may reject the IP"], AMBER, "≈ 0 s behind"),
        (486, "LIVE — STATIC POLL", ["CDN, HTTP Range reads", "same file format",
                                     "immune to origin auth"], BLUE, "1–3 s behind"),
    ]
    for x, t, lines, c, lat in opts:
        b.append(box(x, 132, 200, 76, t, lines, c))
        b.append(label(x + 100, 224, lat, 7.6, c, "700", "middle"))
        b.append(arrow(x + 100, 132, 350, 92, None, FAINT))

    b.append(f'<rect x="14" y="250" width="672" height="52" fill="{PANEL}" stroke="{RULE}"/>')
    b.append(label(24, 268, "MEASURED FROM THIS HOST", 7.4, INK, "700"))
    b.append(label(24, 284, "legacy signalr → 401     signalrcore → 200     static archive → 200 (S3 via CloudFront)", 8, SOFT))
    b.append(label(24, 296, "The static path is a plain CDN, so it is not subject to the origin's IP filtering.", 7.6, FAINT))
    return svg(700, 312, "".join(b))


def fig_realtime():
    """Snapshot, deltas, and what a reconnect costs."""
    b = [label(14, 22, "REALTIME PROTOCOL AND RECONNECT", 9, INK, "700")]

    for x, name in ((60, "BROWSER"), (400, "BACKEND")):
        b.append(f'<line x1="{x}" y1="52" x2="{x}" y2="330" stroke="{RULE}" stroke-width="1.2"/>')
        b.append(label(x, 44, name, 8, INK, "700", "middle"))

    steps = [
        (76,  "connect  /ws", INK, 1),
        (100, "snapshot  seq=1041   ~1.4 MB", GREEN, 0),
        (128, "delta  seq=1042", FAINT, 0),
        (148, "delta  seq=1043", FAINT, 0),
        (168, "delta  seq=1044", FAINT, 0),
    ]
    for y, text, c, right in steps:
        if right:
            b.append(arrow(64, y, 396, y, text, c))
        else:
            b.append(arrow(396, y, 64, y, text, c))

    b.append(f'<rect x="70" y="184" width="320" height="22" fill="#fff" stroke="{ACCENT}" stroke-dasharray="3 3"/>')
    b.append(label(230, 199, "CONNECTION DROPS — 6 s", 8, ACCENT, "700", "middle"))

    b.append(arrow(64, 226, 396, 226, "reconnect  /ws?since=1044", ACCENT))
    b.append(arrow(396, 250, 64, 250, "delta 1045 … 1061   ~9 KB", GREEN))

    b.append(f'<rect x="14" y="272" width="672" height="58" fill="{PANEL}" stroke="{RULE}"/>')
    b.append(label(24, 290, "WHY THE BACKLOG IS BOUNDED AT 600 FRAMES", 7.4, INK, "700"))
    b.append(label(24, 305, "≈ one minute of live rate. Beyond it the client gets a full snapshot instead —", 7.8, SOFT))
    b.append(label(24, 317, "a gap in a delta stream is unrecoverable, and guessing would corrupt the client silently.", 7.8, SOFT))
    return svg(700, 340, "".join(b))


def fig_storage():
    """What is on disk and why it is not a database."""
    b = [label(14, 22, "STORAGE — THE ARCHIVE IS THE DATABASE", 9, INK, "700")]

    tree = [
        ("data/archive/", 0, INK, ""),
        ("2026/", 1, INK, ""),
        ("italian-grand-prix/", 2, INK, ""),
        ("race/", 3, INK, ""),
        ("stream.jsonl", 4, ACCENT, "104 MB|source of truth, append-only"),
        ("analysis/", 4, GREEN, ""),
        ("meta.json", 5, SOFT, "236 B|"),
        ("laps.json", 5, SOFT, "182 KB|"),
        ("stints.json", 5, SOFT, "6 KB|"),
        ("telemetry/*.jsonl", 5, SOFT, "10 MB|21 drivers, per lap"),
        ("track-cache/", 1, SOFT, "|circuit geometry"),
    ]
    y = 50
    for name, depth, c, note in tree:
        b.append(f'<text x="{20 + depth*16}" y="{y}" font-family="{MONO}" font-size="8.4" '
                 f'fill="{c}" font-weight="{"700" if depth<=4 and c!=SOFT else "400"}">{name}</text>')
        if note:
            # Two columns, placed explicitly. SVG collapses runs of spaces, so
            # padding a single string would not line anything up.
            size, desc = note.split("|")
            if size:
                b.append(label(374, y, size, 7.8, FAINT, anchor="end"))
            if desc:
                b.append(label(396, y, desc, 7.8, FAINT))
        y += 15

    b.append(f'<line x1="14" y1="228" x2="686" y2="228" stroke="{RULE}"/>')
    b.append(label(14, 248, "EVERYTHING UNDER analysis/ IS DERIVED", 7.6, GREEN, "700"))
    b.append(label(14, 262, "It can be deleted and rebuilt from stream.jsonl. That is what makes a database", 7.8, SOFT))
    b.append(label(14, 274, "optional rather than load-bearing here.", 7.8, SOFT))

    cards = [("11 ms", "read one driver's", "whole race telemetry"),
             ("0 pkgs", "NuGet dependencies", "in the backend"),
             ("58 MB", "resident memory", "at steady state"),
             ("0.52 %", "CPU", "one replayed session")]
    for i, (v, l1, l2) in enumerate(cards):
        x = 14 + i * 170
        b.append(f'<rect x="{x}" y="292" width="158" height="56" fill="{PANEL}" stroke="{RULE}"/>')
        b.append(f'<text x="{x+12}" y="{316}" font-family="{MONO}" font-size="15" '
                 f'font-weight="700" fill="{INK}">{v}</text>')
        b.append(label(x + 12, 330, l1, 7, FAINT))
        b.append(label(x + 12, 340, l2, 7, FAINT))
    return svg(700, 358, "".join(b))


def fig_deploy():
    """Deployment, including the relay fallback."""
    b = [label(14, 22, "DEPLOYMENT — AND THE RELAY FALLBACK", 9, INK, "700")]

    b.append(zone(14, 44, 300, 150, "NORMAL — EVERYTHING ON ONE HOST"))
    b.append(box(30, 74, 120, 50, "backend", [":4000"], ACCENT, PANEL))
    b.append(box(174, 74, 120, 50, "web", [":3000"], INK, PANEL))
    b.append(box(30, 140, 264, 40, "./data volume", ["archive + analysis"], GREEN, PANEL))
    b.append(arrow(174, 99, 152, 99, None, FAINT))
    b.append(arrow(90, 124, 90, 140, None, GREEN))

    b.append(zone(336, 44, 350, 150, "IF F1 REJECTS THE SERVER'S IP"))
    b.append(box(352, 74, 130, 50, "collector", ["home / residential"], AMBER))
    b.append(box(556, 74, 114, 50, "backend", ["VPS"], ACCENT, PANEL))
    b.append(arrow(482, 92, 556, 92, "outbound WSS", AMBER))
    b.append(label(352, 148, "The collector DIALS OUT. Server-initiated would need a static IP,", 7.6, SOFT))
    b.append(label(352, 160, "port forwarding and a firewall rule at the user's home.", 7.6, SOFT))
    b.append(label(352, 176, "It sends raw TopicUpdates — never merged state.", 7.6, INK, "700"))

    b.append(f'<rect x="14" y="212" width="672" height="46" fill="{PANEL}" stroke="{RULE}"/>')
    b.append(label(24, 230, "HOST REQUIREMENT", 7.4, INK, "700"))
    b.append(label(24, 246, "2 vCPU / 2 GB is comfortable. The constraint is not compute — it is whether F1 accepts the IP.", 7.8, SOFT))
    return svg(700, 268, "".join(b))


FIGURES = {
    "system": (fig_system, "Figure 1", "System context — every component and the direction of every arrow"),
    "ingest": (fig_ingest, "Figure 2", "One update, from the wire to the screen"),
    "merge": (fig_merge, "Figure 3", "The delta merge — the single most important algorithm in the project"),
    "sources": (fig_sources, "Figure 4", "Three ways to get live data, behind one interface"),
    "realtime": (fig_realtime, "Figure 5", "Snapshot, deltas, and what a reconnect actually costs"),
    "storage": (fig_storage, "Figure 6", "What is on disk, and why there is no database"),
    "deploy": (fig_deploy, "Figure 7", "Deployment topology, with the relay fallback"),
}
