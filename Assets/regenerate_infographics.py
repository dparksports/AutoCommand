"""AutoCommand README infographics — clean redesign.

Design rules (why these images read at a glance):
  ONE headline (<= 7 words), ONE subline (<= 12 words),
  exactly 3 steps: big number + 2-4 word label + ONE line of <= 9 words.
  No jargon, no chips, no dense paragraphs, no stale version footer.

Usage:  python Assets/regenerate_infographics.py
Outputs: corp_banner, corp_why, corp_debloat, corp_visibility,
         corp_control, corp_hardening  (.png, 2250x1410)
Requires: matplotlib
"""
import os

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import Circle, FancyBboxPatch

matplotlib.rcParams["font.family"] = "DejaVu Sans"

PAGE = "#FFFFFF"
NAVY = "#16294C"
MUTED = "#5B6B7E"
ACCENT = "#2563EB"
ACCENT_SOFT = "#EAF1FE"
BORDER = "#D7DFE9"
W, H = 2250, 1410
MARGIN = 120
OUT = os.path.dirname(os.path.abspath(__file__))


def canvas():
    fig = plt.figure(figsize=(W / 100, H / 100), dpi=100)
    ax = fig.add_axes([0, 0, 1, 1])
    ax.set_xlim(0, W)
    ax.set_ylim(0, H)
    ax.invert_yaxis()
    ax.axis("off")
    fig.patch.set_facecolor(PAGE)
    ax.add_patch(FancyBboxPatch((0, 0), W, 10, boxstyle="round,pad=0,rounding_size=0",
                                linewidth=0, facecolor=ACCENT))
    return fig, ax


def header(ax, kicker, title, subline):
    ax.text(MARGIN, 130, kicker.upper(), fontsize=20, color=ACCENT,
            fontweight="bold", va="center")
    ax.text(MARGIN, 215, title, fontsize=46, color=NAVY, fontweight="bold", va="center")
    ax.text(MARGIN, 300, subline, fontsize=25, color=MUTED, va="center")


def step_card(ax, fig, x, y, w, h, num, label, line):
    ax.add_patch(FancyBboxPatch((x, y), w, h, boxstyle="round,pad=0,rounding_size=28",
                                linewidth=2, edgecolor=BORDER, facecolor="#FBFCFE"))
    import textwrap
    cx = x + 110
    cy = y + h / 2
    ax.add_patch(Circle((cx, cy), 58, facecolor=ACCENT_SOFT, edgecolor="none"))
    ax.text(cx, cy, str(num), fontsize=42, color=ACCENT, fontweight="bold",
            ha="center", va="center")
    x_text = x + 205
    usable = w - 205 - 70
    wrap_at = max(14, int(usable / 12.2))          # ~px per char at 22px DejaVu
    lines = textwrap.wrap(line, wrap_at)[:3]
    ax.text(x_text, cy - 58, label, fontsize=34, color=NAVY, fontweight="bold", va="center")
    for i, ln in enumerate(lines):
        ax.text(x_text, cy + 14 + i * 46, ln, fontsize=22, color=MUTED, va="center")


def three_steps(ax, fig, steps):
    gap = 60
    cw = (W - 2 * MARGIN - 2 * gap) / 3
    y, h = 480, 620
    for i, (label, line) in enumerate(steps):
        step_card(ax, fig, MARGIN + i * (cw + gap), y, cw, h, i + 1, label, line)


def pill_row(ax, fig, items, y, fontsize=23):
    renderer = fig.canvas.get_renderer()
    px = MARGIN
    for p in items:
        t = ax.text(px, y, p, fontsize=fontsize, color=NAVY, va="center", fontweight="bold",
                    bbox=dict(boxstyle="round,pad=0.65", facecolor=ACCENT_SOFT, edgecolor="none"))
        bb = t.get_window_extent(renderer=renderer).transformed(ax.transData.inverted())
        px = bb.x1 + 55


def finish(fig, name):
    ax = fig.axes[0]
    ax.text(W - MARGIN, H - 60, "AutoCommand · Apache-2.0", fontsize=15,
            color=MUTED, ha="right", va="center")
    fig.savefig(os.path.join(OUT, name), dpi=100, facecolor=PAGE)
    plt.close(fig)
    print("wrote", name)


# ── 1. banner ────────────────────────────────────────────────────────────
def banner():
    fig, ax = canvas()
    ax.text(MARGIN, 520, "AutoCommand", fontsize=104, color=NAVY, fontweight="bold", va="center")
    ax.text(MARGIN, 690, "See what your Windows PC is really doing — then take it back, one click at a time.",
            fontsize=30, color=MUTED, va="center")
    pill_row(ax, fig, ["See every connection", "Know who launched it",
                       "Block in one click", "Undo just as fast"], 900)
    finish(fig, "corp_banner.png")


# ── 2. why ───────────────────────────────────────────────────────────────
def why():
    fig, ax = canvas()
    header(ax, "Why AutoCommand", "Twelve tools. One dashboard.",
           "The questions that cost an afternoon become a row, a color, a click.")
    three_steps(ax, fig, [
        ("Spot it", "The process, the IP, the owner — one row."),
        ("Understand it", "Who launched it, and what it does."),
        ("Take it back", "Block, kill, disable — one click each."),
    ])
    finish(fig, "corp_why.png")


# ── 3. debloat ───────────────────────────────────────────────────────────
def debloat():
    fig, ax = canvas()
    header(ax, "Quick Scan", "A fresh Windows, clean in one pass.",
           "Debloat, firewall, privacy and hardening — one guided checklist.")
    three_steps(ax, fig, [
        ("Sweep", "23 preinstalled apps gone in one confirmation."),
        ("Lock down", "Firewall, privacy and hardening presets applied."),
        ("Verify", "Every step re-checks itself when the pass ends."),
    ])
    finish(fig, "corp_debloat.png")


# ── 4. visibility ────────────────────────────────────────────────────────
def visibility():
    fig, ax = canvas()
    header(ax, "Process Monitor + Sysmon Audit", "See every connection — know who launched it.",
           "A live, attributed view that survives process exit.")
    three_steps(ax, fig, [
        ("Watch", "Live grid: process, destination, hostname."),
        ("Attribute", "The parent survives process exit — no mystery PIDs."),
        ("Act", "Block the IP or app; kill the process."),
    ])
    finish(fig, "corp_visibility.png")


# ── 5. control ───────────────────────────────────────────────────────────
def control():
    fig, ax = canvas()
    header(ax, "Update Control + Telemetry", "Pin the version that works. Silence the beacons.",
           "Apps run normally; their self-updaters fail politely.")
    three_steps(ax, fig, [
        ("Freeze", "The app runs; its updater is locked out."),
        ("Opt out", "The vendor's own documented switch — nothing hacked."),
        ("Verify quiet", "The DNS cache proves the beacons stopped."),
    ])
    finish(fig, "corp_control.png")


# ── 6. hardening ─────────────────────────────────────────────────────────
def hardening():
    fig, ax = canvas()
    header(ax, "OS Hardening + DBX Safety", "Lock it down — and keep it locked.",
           "Guards that re-assert themselves when something drifts back.")
    three_steps(ax, fig, [
        ("Guard", "SSTP and kernel-debug surfaces stay off."),
        ("Watch", "The Secure Boot chain is hashed and checked."),
        ("Confirm first", "DBX updates apply only with your yes."),
    ])
    finish(fig, "corp_hardening.png")


if __name__ == "__main__":
    banner(); why(); debloat(); visibility(); control(); hardening()
