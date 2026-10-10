"""AutoCommand README infographics — dark "terminal-neon" redesign.

Design rules (why these images read at a glance):
  ONE headline (<= 7 words), ONE subline (<= 12 words),
  exactly 3 steps: big number + 2-4 word label + ONE line of <= 9 words.
Dark-mode canvas with per-image neon accent pairs (cyan/violet/lime/pink)
and soft glow blobs — trendy for Gen Z / Alpha, high-contrast for Gen X.

Usage:  python Assets/regenerate_infographics.py
Outputs: corp_banner, corp_why, corp_debloat, corp_visibility,
         corp_control, corp_hardening  (.png, 2250x1410)
Requires: matplotlib
"""
import os
import textwrap

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import Circle, FancyBboxPatch

matplotlib.rcParams["font.family"] = "DejaVu Sans"

# ── dark palette ─────────────────────────────────────────────────────────
BG = "#0B1121"        # deep space navy
PANEL = "#141E33"     # card surface
BORDER = "#24345C"
TEXT = "#F2F6FF"
MUTED = "#9FB0D0"

CYAN = "#22D3EE"
VIOLET = "#A78BFA"
PINK = "#F472B6"
LIME = "#A3E635"
ORANGE = "#FB923C"
TEAL = "#2DD4BF"

W, H = 2250, 1410
MARGIN = 130
OUT = os.path.dirname(os.path.abspath(__file__))


def canvas(theme):
    a1, a2 = theme[0], theme[1]
    fig = plt.figure(figsize=(W / 100, H / 100), dpi=100)
    ax = fig.add_axes([0, 0, 1, 1])
    ax.set_xlim(0, W)
    ax.set_ylim(0, H)
    ax.invert_yaxis()
    ax.axis("off")
    fig.patch.set_facecolor(BG)

    # top accent bar: two-tone
    ax.add_patch(FancyBboxPatch((0, 0), W / 2, 10, boxstyle="round,pad=0,rounding_size=0",
                                linewidth=0, facecolor=a1))
    ax.add_patch(FancyBboxPatch((W / 2, 0), W / 2, 10, boxstyle="round,pad=0,rounding_size=0",
                                linewidth=0, facecolor=a2))
    # soft glow blobs (depth, not clutter)
    for (gx, gy, r, color, alpha) in [
            (W - 260, 180, 420, a1, 0.10),
            (180, H - 200, 380, a2, 0.08),
            (W * 0.55, H * 0.45, 500, a1, 0.045)]:
        ax.add_patch(Circle((gx, gy), r, facecolor=color, edgecolor="none", alpha=alpha))
    return fig, ax


def header(ax, fig, theme, kicker, title, subline):
    a1, a2 = theme[0], theme[1]
    ax.text(MARGIN, 130, kicker.upper(), fontsize=20, color=a1,
            fontweight="bold", va="center")
    # two-tone headline: last word in the second accent
    parts = title.rsplit(" ", 1)
    if len(parts) == 2:
        left, right = parts[0] + " ", parts[1]
        t1 = ax.text(MARGIN, 215, left, fontsize=46, color=TEXT, fontweight="bold", va="center")
        w1 = t1.get_window_extent(fig.canvas.get_renderer()).transformed(ax.transData.inverted()).x1
        ax.text(w1 + 14, 215, right, fontsize=46, color=a2, fontweight="bold", va="center")
    else:
        ax.text(MARGIN, 215, title, fontsize=46, color=TEXT, fontweight="bold", va="center")
    ax.text(MARGIN, 300, subline, fontsize=25, color=MUTED, va="center")


def step_card(ax, x, y, w, h, num, label, line, accent):
    ax.add_patch(FancyBboxPatch((x, y), w, h, boxstyle="round,pad=0,rounding_size=28",
                                linewidth=2, edgecolor=BORDER, facecolor=PANEL))
    cx = x + 110
    cy = y + h / 2
    # neon number: glow halo + solid disc
    ax.add_patch(Circle((cx, cy), 74, facecolor=accent, edgecolor="none", alpha=0.18))
    ax.add_patch(Circle((cx, cy), 56, facecolor=accent, edgecolor="none"))
    ax.text(cx, cy, str(num), fontsize=40, color=BG, fontweight="bold",
            ha="center", va="center")
    x_text = x + 205
    usable = w - 205 - 70
    wrap_at = max(14, int(usable / 12.2))
    lines = textwrap.wrap(line, wrap_at)[:3]
    ax.text(x_text, cy - 58, label, fontsize=34, color=TEXT, fontweight="bold", va="center")
    for i, ln in enumerate(lines):
        ax.text(x_text, cy + 14 + i * 46, ln, fontsize=22, color=MUTED, va="center")


def three_steps(ax, fig, steps, theme):
    gap = 60
    cw = (W - 2 * MARGIN - 2 * gap) / 3
    y, h = 480, 620
    for i, (label, line) in enumerate(steps):
        step_card(ax, MARGIN + i * (cw + gap), y, cw, h, i + 1, label, line,
                  theme[i % len(theme)])


def pill_row(ax, fig, items, y, theme, fontsize=23):
    renderer = fig.canvas.get_renderer()
    px = MARGIN
    for i, p in enumerate(items):
        accent = theme[i % len(theme)]
        t = ax.text(px, y, p, fontsize=fontsize, color=TEXT, va="center", fontweight="bold",
                    bbox=dict(boxstyle="round,pad=0.65", facecolor=PANEL,
                              edgecolor=accent, linewidth=2))
        bb = t.get_window_extent(renderer=renderer).transformed(ax.transData.inverted())
        px = bb.x1 + 55


def finish(fig, name):
    ax = fig.axes[0]
    ax.text(W - MARGIN, H - 60, "AutoCommand · Apache-2.0", fontsize=15,
            color=MUTED, ha="right", va="center")
    fig.savefig(os.path.join(OUT, name), dpi=100, facecolor=BG)
    plt.close(fig)
    print("wrote", name)


# ── 1. banner — full neon spectrum ───────────────────────────────────────
def banner():
    theme = [CYAN, VIOLET, PINK, LIME]
    fig, ax = canvas(theme)
    ax.text(MARGIN, 520, "Auto", fontsize=104, color=TEXT, fontweight="bold", va="center")
    w_auto = ax.texts[-1].get_window_extent(fig.canvas.get_renderer()).transformed(ax.transData.inverted()).x1
    ax.text(w_auto + 12, 520, "Command", fontsize=104, color=CYAN, fontweight="bold", va="center")
    ax.text(MARGIN, 690, "See what your Windows PC is really doing — then take it back, one click at a time.",
            fontsize=30, color=MUTED, va="center")
    pill_row(ax, fig, ["See every connection", "Know who launched it",
                       "Block in one click", "Undo just as fast"], 900, theme)
    finish(fig, "corp_banner.png")


# ── 2. why — violet / pink ───────────────────────────────────────────────
def why():
    theme = [VIOLET, PINK, VIOLET]
    fig, ax = canvas(theme)
    header(ax, fig, theme, "Why AutoCommand", "Twelve tools. One dashboard.",
           "The questions that cost an afternoon become a row, a color, a click.")
    three_steps(ax, fig, [
        ("Spot it", "The process, the IP, the owner — one row."),
        ("Understand it", "Who launched it, and what it does."),
        ("Take it back", "Block, kill, disable — one click each."),
    ], theme)
    finish(fig, "corp_why.png")


# ── 3. debloat — lime / teal ─────────────────────────────────────────────
def debloat():
    theme = [LIME, TEAL, LIME]
    fig, ax = canvas(theme)
    header(ax, fig, theme, "Quick Scan", "A fresh Windows, clean in one pass.",
           "Debloat, firewall, privacy and hardening — one guided checklist.")
    three_steps(ax, fig, [
        ("Sweep", "23 preinstalled apps gone in one confirmation."),
        ("Lock down", "Firewall, privacy and hardening presets applied."),
        ("Verify", "Every step re-checks itself when the pass ends."),
    ], theme)
    finish(fig, "corp_debloat.png")


# ── 4. visibility — cyan / violet ────────────────────────────────────────
def visibility():
    theme = [CYAN, VIOLET, CYAN]
    fig, ax = canvas(theme)
    header(ax, fig, theme, "Process Monitor + Sysmon Audit", "See every connection — know who launched it.",
           "A live, attributed view that survives process exit.")
    three_steps(ax, fig, [
        ("Watch", "Live grid: process, destination, hostname."),
        ("Attribute", "The parent survives process exit — no mystery PIDs."),
        ("Act", "Block the IP or app; kill the process."),
    ], theme)
    finish(fig, "corp_visibility.png")


# ── 5. control — teal / lime ─────────────────────────────────────────────
def control():
    theme = [TEAL, LIME, TEAL]
    fig, ax = canvas(theme)
    header(ax, fig, theme, "Update Control + Telemetry", "Pin the version that works. Silence the beacons.",
           "Apps run normally; their self-updaters fail politely.")
    three_steps(ax, fig, [
        ("Freeze", "The app runs; its updater is locked out."),
        ("Opt out", "The vendor's own documented switch — nothing hacked."),
        ("Verify quiet", "The DNS cache proves the beacons stopped."),
    ], theme)
    finish(fig, "corp_control.png")


# ── 6. hardening — orange / pink ─────────────────────────────────────────
def hardening():
    theme = [ORANGE, PINK, ORANGE]
    fig, ax = canvas(theme)
    header(ax, fig, theme, "OS Hardening + DBX Safety", "Lock it down — and keep it locked.",
           "Guards that re-assert themselves when something drifts back.")
    three_steps(ax, fig, [
        ("Guard", "SSTP and kernel-debug surfaces stay off."),
        ("Watch", "The Secure Boot chain is hashed and checked."),
        ("Confirm first", "DBX updates apply only with your yes."),
    ], theme)
    finish(fig, "corp_hardening.png")


if __name__ == "__main__":
    banner(); why(); debloat(); visibility(); control(); hardening()
