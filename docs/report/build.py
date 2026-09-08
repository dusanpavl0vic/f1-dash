"""Builds the architecture report as one HTML file, ready for print-to-PDF."""
import datetime, pathlib
from figures import FIGURES

HERE = pathlib.Path(__file__).parent


def fig(name):
    make, num, caption = FIGURES[name]
    return (f'<figure>{make()}'
            f'<figcaption><b>{num}</b> &nbsp; {caption}</figcaption></figure>')


SECTIONS = [
    ("Overview", "What Apex is, and the five things it must get right", """
<p><u>Apex</u> is a self-hosted Formula 1 live timing, replay, telemetry and analysis
application. It follows a session as it happens, replays any session back to 2018 from a local
archive, and derives per-lap analysis without a human watching it.</p>

<p>Everything in this report follows from five constraints that were fixed before any code
was written.</p>

<table>
<thead><tr><th style="width:26%">Constraint</th><th>What it forces</th></tr></thead>
<tbody>
<tr><td><b>The feed sends deltas</b></td><td>State is never transmitted whole. Both server and browser
must reconstruct it by merging, using <em>identical</em> rules, or they diverge silently.</td></tr>
<tr><td><b>F1 runs ~24 weekends a year</b></td><td>The live path cannot be a separate code path.
If it were, the replay path would be the tested one and live would break on race day.</td></tr>
<tr><td><b>Silent staleness is the worst failure</b></td><td>A frozen dashboard that still looks live
is undetectable by the user. Every connection state is stated in words.</td></tr>
<tr><td><b>One person maintains this</b></td><td>Every moving part must earn its place. A container
that has to be running, backed up and version-matched is a real cost.</td></tr>
<tr><td><b>It is unofficial</b></td><td>No F1 branding anywhere; the legal notice appears on every page.</td></tr>
</tbody></table>

<div class="stats">
<div class="stat"><div class="v">2</div><div class="l">containers</div></div>
<div class="stat"><div class="v">0</div><div class="l">databases</div></div>
<div class="stat"><div class="v">0</div><div class="l">backend packages</div></div>
<div class="stat"><div class="v">90</div><div class="l">backend tests</div></div>
</div>

<p>The backend has <u>no NuGet dependencies at all</u> — only the .NET base class library. That is
not minimalism for its own sake: the SignalR protocol, the archive format and the merge rules are
all things this project had to implement exactly, and a library that almost does it would have been
harder to correct than writing it.</p>
""", None),

    ("System context", "Which component talks to which, and in which direction", """
<p>Four external services, two containers, one volume. Nothing else.</p>
FIG:system
<h3>The external dependencies</h3>
<table>
<thead><tr><th style="width:24%">Service</th><th style="width:22%">Provides</th><th>If it is unavailable</th></tr></thead>
<tbody>
<tr><td><b>F1 live timing</b></td><td>The session feed and the session archive</td>
<td>Live stops; replay of already-downloaded sessions is unaffected.</td></tr>
<tr><td><b>Jolpica</b> <em>(Ergast successor)</em></td><td>Schedule, results, standings</td>
<td>Those pages degrade; timing and telemetry are untouched.</td></tr>
<tr><td><b>MultiViewer</b></td><td>Circuit geometry for the track map</td>
<td>Cached on disk after the first fetch, so only a new circuit is affected.</td></tr>
</tbody></table>

<div class="note blue"><span class="lbl">Design rule</span>
No external service sits in the live path except F1 itself. A standings API being slow must never
be able to delay a position update.</div>
""", None),

    ("The path of one update", "From the wire to the screen", """
<p>This is the hot path. Roughly ten deltas a second arrive during a session, each patching a
deeply nested object, and every decision below exists to keep that cheap.</p>
FIG:ingest

<h3>Two details that are easy to get wrong</h3>

<h4>Raw DEFLATE, not zlib</h4>
<p>Compressed topics arrive as <u>raw DEFLATE</u> with no zlib header. Using <code>ZLibStream</code>
fails on the first byte; <code>DeflateStream</code> is correct. The symptom is a total failure rather
than a subtle one, which is fortunate.</p>

<h4>Serialise once, write many</h4>
<p>A delta is serialised <u>a single time</u> and the same byte array is handed to every connected
client. This is what keeps fan-out nearly free: the number of viewers affects only the number of
socket writes, never the amount of JSON work.</p>

<div class="note"><span class="lbl">Cost avoided</span>
The naive design rebuilds and sends the whole state on every delta. At a 1–2 MB state and ten deltas
a second that is <b>10–20 MB/s of pure waste</b> — the single largest cost in the obvious
implementation, and the reason the snapshot is cached and rebuilt only on demand.</div>

<h3>Backpressure</h3>
<p>Each client has a bounded queue. When it fills, the client is <u>dropped and must reconnect</u> —
it is never accommodated, because one stalled browser tab must not be able to back up the shared
reader for everyone else.</p>
<p>The queue refuses new frames rather than evicting old ones. Dropping the oldest delta would leave
the client receiving patches for a state it never received: still rendering, quietly wrong. Refusing
lets the socket close and forces a clean resync.</p>
""", None),

    ("The delta merge", "The single most important algorithm here", """
<p>If one rule in this section is wrong, every number on the screen is wrong in a way that looks
plausible. It is the most carefully tested code in the project.</p>
FIG:merge

<h3>The five rules</h3>
<table>
<thead><tr><th style="width:34%">Case</th><th>Behaviour</th></tr></thead>
<tbody>
<tr><td>Key absent from the delta</td><td>Left completely untouched. <u>Absent never means null.</u></td></tr>
<tr><td>Both sides are objects</td><td>Merge recursively.</td></tr>
<tr><td>Scalar, or type change</td><td>Replace outright.</td></tr>
<tr><td>Array-like object <em>(numeric keys)</em></td><td>Patch by index — the feed sends
<code>{"2": {...}}</code> to mean "the third element".</td></tr>
<tr><td>Explicit <code>null</code></td><td>Remove the key.</td></tr>
</tbody></table>

<div class="note"><span class="lbl">The trap that cost the most time</span>
A <code>JsonNode</code> may have <b>only one parent</b>. Assigning a node that already lives
elsewhere in the tree silently reparents it, corrupting the state it is being merged into. Every
assignment therefore deep-clones. This is invisible in small tests and catastrophic in a real
session.</div>

<h3>Parity between the two implementations</h3>
<p>The server merges in C#; the browser merges the same deltas in TypeScript. Two implementations of
one algorithm is exactly the situation where drift is inevitable — so <u>both are tested against the
same fixture files</u> on disk. A change to one that is not made to the other fails the suite.</p>

<h3>What the accumulator adds on top</h3>
<p>Not every topic merges. Three are <u>replaced wholesale</u> — <code>Position</code>,
<code>CarData</code> and <code>SessionInfo</code>, because each publish is a complete new value and
merging them would accumulate stale entries. Two are <u>append-only</u> —
<code>RaceControlMessages</code> and <code>TeamRadio</code>, which are logs, not state.</p>
""", None),

    ("Getting live data", "Three sources behind one interface", """
<p>Live data is the only genuinely fragile part of this system, because it is the only part that
depends on someone else's server being willing to talk to yours. So there are three ways in, they
are interchangeable, and the backend moves between them without being told to.</p>
FIG:sources

<h3>What was measured</h3>
<p>From a development machine, on the same day:</p>
<pre>legacy signalr/negotiate   → HTTP 401
signalrcore/negotiate      → HTTP 200
static/SessionInfo.json    → HTTP 200
                             server: AmazonS3 · via: CloudFront</pre>

<p>This matters more than it first appears. The static archive is served from <u>S3 behind
CloudFront</u> — an ordinary content delivery network — while the SignalR origin is the component
that rejects requests. A deployment blocked at the origin is therefore <u>not necessarily blocked at
the CDN</u>, and the polling source exists to exploit exactly that.</p>

<p>The static files support HTTP <code>Range</code> requests, confirmed with a <code>206 Partial
Content</code> response, so a poller re-reads only the bytes appended since its last check — a few
kilobytes a second rather than a 5.6 MB file. The format is identical to the archive the replay
source already parses, <u>including the UTF-8 byte order mark</u>, so no new parser is involved.</p>

<div class="note green"><span class="lbl">Why one interface matters</span>
<code>ISessionSource</code> yields <code>TopicUpdate</code> values and nothing downstream knows which
implementation produced them. Replay is therefore not a testing convenience — it is the same code
path live uses, exercised every single day rather than on 24 weekends a year.</div>
""", None),

    ("Talking to the browser", "Snapshot, deltas, resumption and delay", """
FIG:realtime

<h3>The contract</h3>
<p>A connecting client receives a <u>snapshot</u> carrying the full state and a sequence number, then
every subsequent <u>delta</u> in order. The client merges deltas into the snapshot with the same five
rules the server used.</p>
<p>A snapshot <u>replaces</u> state; it never merges into it. This is not a detail: on a session
change the server sends a deliberately <em>empty</em> snapshot, and a client that merged it would
keep the previous session's drivers — which once rendered a 26-car field with two cars sharing
first place.</p>

<h3>Reconnection</h3>
<p>The client reconnects with exponential backoff and ±20% jitter, so a room full of tabs does not
reconnect in lockstep. It sends the last sequence it applied; the server replies with the missing
deltas when its backlog reaches that far, and with a full snapshot when it does not.</p>

<div class="note"><span class="lbl">Concurrency</span>
Working out what a client missed and registering it for future deltas happen <b>under one lock</b>.
A delta broadcast between those two steps would be lost, and a lost delta is unrecoverable: client
state is delta-accumulated, so it would stay silently wrong for the rest of the session.</div>

<h3>Heartbeat</h3>
<p>A socket that is open but dead is common on mobile networks and behind corporate proxies. Without
an explicit liveness check the dashboard freezes on stale data while still looking live. Silence for
longer than two heartbeat intervals closes the socket and triggers a reconnect.</p>

<h3>Broadcast delay</h3>
<p>Television is typically 5–60 seconds behind the timing feed, so an undelayed dashboard spoils an
overtake before it is shown. Messages are held in a buffer and released against the <u>server's
clock corrected for skew</u> — timing against arrival would drift with every network hiccup, and the
drift only ever accumulates.</p>
<p>Lowering the delay drains at a bounded multiple of real time, so going from 60 seconds to zero
animates rather than teleporting the field. Snapshots bypass the buffer entirely: a snapshot is not
an event to be shown later, it is the state the queued deltas patch.</p>
""", None),

    ("Storage", "Why the archive is the database", """
FIG:storage

<h3>The question, asked properly</h3>
<p>InfluxDB and MongoDB were both offered as free choices. Rather than reason about which shape of
database fits telemetry in the abstract, the actual access patterns were measured.</p>

<table>
<thead><tr><th>Artefact</th><th class="num">Size</th><th>Access pattern</th><th class="num">Cost</th></tr></thead>
<tbody>
<tr><td><code>stream.jsonl</code></td><td class="num">104 MB</td><td>Streamed, never held whole</td><td class="num">—</td></tr>
<tr><td><code>analysis/*.json</code></td><td class="num">~190 KB</td><td>Written once, read whole</td><td class="num">&lt; 5 ms</td></tr>
<tr><td><code>telemetry/*.jsonl</code></td><td class="num">10 MB</td><td>One driver, one session</td><td class="num">11 ms</td></tr>
</tbody></table>

<p><u>Eleven milliseconds</u> to read a driver's entire race telemetry from disk. A database would
replace that with a network round-trip to a container that must be running, backed up and
version-matched — and win nothing.</p>

<div class="note green"><span class="lbl">The trigger, written down in advance</span>
Both answers are about <b>per-session</b> access. The moment a screen asks a question spanning
sessions — <em>"every lap over 330 km/h this season"</em>, <em>"Monza sector 2 across three
years"</em> — the file layout stops working, because answering means opening every file in the
archive. At that point InfluxDB goes in for telemetry and the analysis documents get indexed.
The decision is recorded as <b>D-011</b>, and the migration is designed as <b>Phase K</b>, so it is
planned rather than improvised.</div>

<h3>Two rules that hold regardless</h3>
<ul>
<li><b>The archive stays authoritative.</b> Any store added later is a derived index that can be
dropped and rebuilt, so a corrupt database is an inconvenience rather than data loss.</li>
<li><b>No database in the ingest path.</b> Adding a network write per delta would trade the one thing
this application cannot afford to lose — latency during a live session — for a convenience it does
not need.</li>
</ul>
""", None),

    ("Analysis and telemetry", "Derived once, read many times", """
<p>Analysis is <u>not produced by replaying a session in real time</u>. A finished race is read from
<code>stream.jsonl</code> at full speed and the results are written to disk; a two-hour race is
processed in about a second and a half.</p>

<h3>What is derived</h3>
<table>
<thead><tr><th style="width:26%">Artefact</th><th>Contents</th></tr></thead>
<tbody>
<tr><td><b>Lap times</b></td><td>Every lap for every driver, with sector times and track status.</td></tr>
<tr><td><b>Stints</b></td><td>Compound, age, and where each stop fell.</td></tr>
<tr><td><b>Position history</b></td><td>Per-lap classification, for the progression chart.</td></tr>
<tr><td><b>Telemetry</b></td><td>Speed, throttle, brake, gear, RPM and DRS, per lap, per driver.</td></tr>
</tbody></table>

<div class="note"><span class="lbl">Two bugs worth recording</span>
<b>Telemetry was capped at 2,000 samples per lap</b> after lap 1 of one race recorded 12,922 —
the sampling rate is not constant, and an uncapped buffer grows without bound.<br><br>
<b>Lap charts reject outliers around the median.</b> A 1,958-second lap under a red flag destroyed
the scale of an entire chart, and the track status had already cleared by the time the lap
completed, so the flag could not be used to filter it.</div>

<h3>The 2026 feed change</h3>
<p>DRS was removed from the regulations and from the feed: <code>CarData</code> channel 45 is simply
absent. The replacement aids are not published at all, so nothing can be shown for them; what 2026
added instead is an overtake counter.</p>
<p>The era is detected <u>from the payload, not from the season</u> — if channel 45 is present it is
a DRS era, otherwise the overtake series is used. A mid-season feed change therefore degrades
gracefully instead of breaking.</p>
""", None),

    ("The frontend", "Why the live state is not in a store", """
<p>React 19 and Vite, TypeScript in strict mode with <code>noUncheckedIndexedAccess</code> and
<code>exactOptionalPropertyTypes</code>. Routing is explicit: <code>/live</code> and
<code>/replay</code> are separate destinations, never tabs over one shared session, because a user
cannot tell a replay from live by looking at the numbers.</p>

<h3>The one documented exception</h3>
<p>Live session state lives <u>outside React entirely</u>. Ten deltas a second dispatched through a
normal store would re-render the tree and drop frames. Instead deltas are merged into a module-level
object, and subscribers are notified <u>at most once per animation frame</u> through
<code>useSyncExternalStore</code>. The projection from raw state to view models runs once per frame,
not once per delta.</p>

<div class="note blue"><span class="lbl">What the feed does not keep</span>
The feed publishes the <b>present</b>: a driver's current last-lap time, the track status right now.
It has no history at all. Anything drawn over time — a pace chart, a timeline of safety car periods —
is accumulated client-side by watching the stream. This is deliberately lossy: joining a session
halfway means the history starts halfway, which is stated in the interface rather than disguised.
The complete version of the same data is what the backend writes to disk.</div>

<h3>Responsive layout</h3>
<p>Three layouts at 768 px and 1280 px. The breakpoints are subscribed to through
<code>matchMedia</code> rather than a resize listener — a resize handler fires on every pixel of a
drag, while a media query fires only when the answer changes. It also means <u>the JavaScript and the
stylesheet can never disagree</u> about which layout is active.</p>

<h3>Charts are SVG, deliberately</h3>
<p>PDF export is a browser print. An SVG chart prints as vectors; a canvas chart prints as a
screenshot. Lines also <u>break at missing data</u> rather than bridging it — joining across a lap a
driver did not set would draw a segment through data that does not exist, and it would look exactly
like a real lap.</p>
""", None),

    ("Deployment", "Two containers, one volume", """
FIG:deploy

<p>The host needs <u>only Docker</u>. Even the .NET SDK runs in a container for development, so no
toolchain is installed on the machine.</p>

<h3>Resource use, measured</h3>
<div class="stats">
<div class="stat"><div class="v">0.52%</div><div class="l">CPU, one session</div></div>
<div class="stat"><div class="v">58 MB</div><div class="l">resident</div></div>
<div class="stat"><div class="v">80–122 MB</div><div class="l">per session</div></div>
<div class="stat"><div class="v">2 / 2</div><div class="l">recommended vCPU / GB</div></div>
</div>

<p>The real constraint is <u>not compute</u>. It is whether F1 accepts the server's IP address — which
is why §5 exists, and why the relay above is designed rather than improvised.</p>

<div class="note"><span class="lbl">One detail that cost an afternoon</span>
The container health check must use <code>127.0.0.1</code>, not <code>localhost</code>.
<code>localhost</code> resolves to the IPv6 loopback, and Kestrel binds IPv4 — so the check fails
while the service is perfectly healthy.</div>

<h3>The relay, if it is needed</h3>
<p>A collector process runs where F1 accepts connections — a home connection is the most reliable,
being a residential address — and <u>dials outward</u> to the server. That direction is the whole
design: a server-initiated connection would require a static IP, port forwarding and a firewall rule
at the user's home, and would break the first time their provider rotated the address.</p>
<p>It forwards raw topic updates, never merged state. Sending merged state would mean shipping
1–2 MB per update instead of a few kilobytes, and would put a second copy of the merge algorithm in
production where it could drift from the first.</p>
""", None),

    ("Decisions", "What was chosen, and what would change it", """
<p>Every departure from the original design is recorded with its reasoning in
<code>DECISIONS.md</code>. The ones that shape the architecture:</p>

<table>
<thead><tr><th style="width:8%">ID</th><th style="width:30%">Decision</th><th>Reasoning</th></tr></thead>
<tbody>
<tr><td><b>D-001</b></td><td>.NET 9 rather than Python</td>
<td>Throughput was never the constraint; the deciding factor was a typed model and channel-based
fan-out mapping cleanly onto the bounded-queue design.</td></tr>
<tr><td><b>D-003</b></td><td>Vite SPA rather than Next.js</td>
<td>There is nothing to server-render. The application is one long-lived session driven by a socket.</td></tr>
<tr><td><b>D-004</b></td><td>Live state outside React</td>
<td>Ten deltas a second against a deep object. The one documented exception to the state rules.</td></tr>
<tr><td><b>D-009</b></td><td>One backend; Redis deferred</td>
<td>Redis existed to bridge a split between ingest and fan-out. Without the split there is nothing
to bridge. The seam is preserved — replacing it touches one file.</td></tr>
<tr><td><b>D-010</b></td><td>Bounded channels use <code>Wait</code></td>
<td><code>DropWrite</code> discards silently <em>and returns success</em>, which is indistinguishable
from delivery. Caught by a test, not by observation.</td></tr>
<tr><td><b>D-012</b></td><td>Three live sources</td>
<td>Failover is judged on <em>data delivered</em>, not on connection — the observed
failure is a handshake that completes and then goes silent.</td></tr>
<tr><td><b>D-011</b></td><td>No database yet</td>
<td>Measured, not assumed: 11 ms to read a race of telemetry. The trigger for revisiting is written
down rather than left to judgement.</td></tr>
</tbody></table>

<h3>Verification</h3>
<p>The strongest evidence that the ingest layer is correct is a single assertion: replaying the 2024
Italian Grand Prix through the accumulator produces a classification that matches the official top
ten <u>exactly</u>. Everything upstream of that — inflate, parse, merge, accumulate — has to be right
for it to hold.</p>

<div class="note green"><span class="lbl">Status</span>
110 backend tests and 55 in the browser suite, with a typechecked and linted frontend. All three
ingest sources are built and each is verified against something real: the poller against F1's live
archive, the relay end to end over websockets. Live SignalR subscription remains the one open item —
the handshake completes and the initial state arrives in a Node probe, while the identical
byte-for-byte payload is rejected in C#. It is no longer a blocker, which is why §5 is ordered the
way it is.</div>
""", None),
]


def build():
    body = []

    # ---- cover
    today = datetime.date.today().strftime("%d %B %Y")
    body.append(f"""
<div class="cover">
  <div class="coverTop">
    <div class="coverMark"></div>
    <h1 class="coverTitle">Apex<br/>Architecture</h1>
    <p class="coverSub">How a self-hosted Formula 1 live timing, replay and telemetry
    application is put together — every component, every connection, and the reasoning
    behind each one.</p>
  </div>
  <div class="coverMeta">
    <div><div class="l">Document</div><div class="v">Technical report</div></div>
    <div><div class="l">Project</div><div class="v">Apex</div></div>
    <div><div class="l">Owner</div><div class="v">CloudSheep</div></div>
    <div><div class="l">Date</div><div class="v">{today}</div></div>
  </div>
</div>""")

    # ---- contents
    rows = "".join(
        f'<div class="tocRow"><span class="tocNum">{i+1}</span>'
        f'<span class="tocName">{t}</span>'
        f'<span class="tocDesc">{s}</span></div>'
        for i, (t, s, _, _) in enumerate(SECTIONS))
    body.append(f'<section><h2><span class="idx">—</span>Contents</h2><div class="h2rule"></div>'
                f'<div class="toc">{rows}</div>'
                f'<h3>Figures</h3>' +
                "".join(f'<div class="tocRow"><span class="tocNum">{n.split()[1]}</span>'
                        f'<span class="tocName">{c}</span></div>'
                        for _, (_, n, c) in enumerate(FIGURES.values()))
                + '</section>')

    # ---- sections
    for i, (title, _, html, _) in enumerate(SECTIONS):
        for key in FIGURES:
            html = html.replace(f"FIG:{key}", fig(key))
        body.append(f'<section><h2><span class="idx">{i+1:02d}</span>{title}</h2>'
                    f'<div class="h2rule"></div>{html}</section>')

    css = (HERE / "report.css").read_text()
    out = f"""<!doctype html><html lang="en"><head><meta charset="utf-8">
<title>Apex — Architecture</title><style>{css}</style></head>
<body>{"".join(body)}</body></html>"""

    (HERE / "report.html").write_text(out)
    print(f"report.html  {len(out)/1024:.0f} KB  ·  {len(SECTIONS)} sections  ·  {len(FIGURES)} figures")


if __name__ == "__main__":
    build()
