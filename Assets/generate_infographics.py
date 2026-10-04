"""Generate AutoCommand README infographics (dark cyber theme, matplotlib).

Usage:  py Assets/generate_infographics.py
Outputs: Assets/firewall_profiles_infographic.png
         Assets/security_stack_infographic.png
"""
import os

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np
from matplotlib.patches import FancyBboxPatch, FancyArrowPatch, Circle

matplotlib.rcParams["font.family"] = "DejaVu Sans"

# ---- palette -------------------------------------------------------------
BG_TOP = "#0A1526"
BG_BOT = "#0C2136"
PANEL = "#0F1E33"
PANEL_EDGE = "#2A4A73"
CYAN = "#00E5FF"
CYAN_DIM = "#0E6E8C"
TXT = "#E8F4FF"
SUB = "#8FAECB"
ORANGE = "#FFAE42"
RED = "#FF5A5A"
GREEN = "#3DDC84"
BLUE = "#4FC3F7"
PURPLE = "#B388FF"

W, H = 1600, 1000


def new_canvas():
    fig = plt.figure(figsize=(W / 100, H / 100), dpi=150)
    ax = fig.add_axes([0, 0, 1, 1])
    ax.set_xlim(0, W)
    ax.set_ylim(0, H)
    ax.invert_yaxis()
    ax.axis("off")
    # vertical gradient background
    grad = np.linspace(0, 1, 256).reshape(-1, 1)
    ax.imshow(grad, extent=[0, W, H, 0], aspect="auto",
              cmap=matplotlib.colors.LinearSegmentedColormap.from_list("bg", [BG_TOP, BG_BOT]), zorder=-2)
    rng = np.random.default_rng(42)
    dots_x, dots_y = rng.uniform(0, W, 90), rng.uniform(0, H, 90)
    ax.scatter(dots_x, dots_y, s=rng.uniform(2, 14, 90), color=CYAN, alpha=rng.uniform(0.04, 0.16, 90), zorder=-1)
    return fig, ax


def panel(ax, x, y, w, h, edge=PANEL_EDGE, lw=1.6, face=PANEL, alpha=0.92):
    p = FancyBboxPatch((x, y), w, h, boxstyle="round,pad=0,rounding_size=14",
                       linewidth=lw, edgecolor=edge, facecolor=face, alpha=alpha, zorder=1)
    ax.add_patch(p)
    return p


def header(ax, title, subtitle, version_chip="v3.7.0"):
    panel(ax, 40, 34, W - 80, 108, edge=CYAN_DIM, lw=2.2, alpha=0.95)
    ax.add_patch(FancyBboxPatch((40, 34), 14, 108, boxstyle="round,pad=0,rounding_size=7",
                                linewidth=0, facecolor=CYAN, zorder=2))
    ax.text(84, 72, title, color=TXT, fontsize=27, fontweight="bold", va="center")
    ax.text(84, 112, subtitle, color=SUB, fontsize=15, va="center")
    ax.text(W - 72, 88, version_chip, color=BG_TOP, fontsize=19, fontweight="bold",
            ha="right", va="center",
            bbox=dict(boxstyle="round,pad=0.5", facecolor=CYAN, edgecolor="none"))


def footer(ax, text, accent=CYAN):
    panel(ax, 40, H - 72, W - 80, 46, edge=PANEL_EDGE, alpha=0.95)
    ax.text(W / 2, H - 49, text, color=accent, fontsize=14.5, ha="center", va="center", fontweight="bold")


# ============================================================ infographic 1
def firewall_infographic():
    fig, ax = new_canvas()
    header(ax, "AUTOCOMMAND  —  ADVANCED FIREWALL MANAGEMENT",
           "Five one-click profiles  •  native INetFwPolicy2 COM  •  live progress & completion reporting")

    profiles = [
        ("SHIELD UP", ORANGE, "Paranoid lockdown",
         "Disables every grouped rule,\nre-enables whitelists only:\nmDNS + Core Networking"),
        ("PUBLIC STRICT", RED, "Public Wi-Fi / travel",
         "Disables File & Printer\nSharing, Network Discovery,\nRemote Desktop"),
        ("GAMING", GREEN, "Play & stream",
         "Enables Network Discovery,\nCast to Device functionality\n(stream / cast targets)"),
        ("OFFICE", CYAN, "Workstation on a LAN",
         "Enables File & Printer\nSharing, Network Discovery,\nRemote Desktop"),
        ("HOME", BLUE, "Trusted home network",
         "Enables File & Printer\nSharing, Network Discovery"),
    ]

    cw, ch, gap = 292, 330, 15
    x0 = (W - (cw * 5 + gap * 4)) / 2
    y0 = 192
    ax.text(x0, y0 - 16, "QUICK PROFILE PRESETS", color=CYAN, fontsize=17, fontweight="bold")
    for i, (name, accent, tag, body) in enumerate(profiles):
        x = x0 + i * (cw + gap)
        panel(ax, x, y0, cw, ch)
        ax.add_patch(FancyBboxPatch((x, y0), cw, 8, boxstyle="round,pad=0,rounding_size=4",
                                    linewidth=0, facecolor=accent, zorder=2))
        ax.add_patch(Circle((x + 38, y0 + 54), 21, linewidth=2.4, edgecolor=accent, facecolor="none", zorder=3))
        ax.text(x + 38, y0 + 54, name[0], color=accent, fontsize=21, fontweight="bold",
                ha="center", va="center", zorder=3)
        ax.text(x + 68, y0 + 47, name, color=TXT, fontsize=16, fontweight="bold", va="center")
        ax.text(x + 68, y0 + 72, tag, color=accent, fontsize=11.5, va="center", style="italic")
        ax.text(x + cw / 2, y0 + 165, body, color=SUB, fontsize=12.2, ha="center", va="center", linespacing=1.7)

    ax.text(x0, y0 + ch + 44, "ENGINE PIPELINE  —  WHAT HAPPENS WHEN YOU PICK A PROFILE", color=CYAN,
            fontsize=17, fontweight="bold")

    steps = [
        ("1. ENUMERATE", "Read every rule via\nINetFwPolicy2 COM —\nzero PowerShell", CYAN),
        ("2. RESOLVE", "Translate indirect names:\n@FirewallAPI.dll,-32752\n→ \u201cNetwork Discovery\u201d", CYAN),
        ("3. MATCH", "Case-insensitive match on\nraw + resolved group names\n(never misses a rule)", CYAN),
        ("4. APPLY", "Live per-group progress,\ncontrols locked during\nthe transaction", GREEN),
        ("5. REPORT", "Completion dialog: matched /\nchanged / failed + errors —\nauto-refresh, enabled first", ORANGE),
    ]
    sw, sh, sy = 282, 200, y0 + ch + 66
    sx0 = (W - (sw * 5 + gap * 4)) / 2
    for i, (title, body, accent) in enumerate(steps):
        x = sx0 + i * (sw + gap)
        panel(ax, x, sy, sw, sh, edge=accent if i >= 3 else PANEL_EDGE)
        ax.text(x + 18, sy + 36, title, color=accent, fontsize=14.5, fontweight="bold", va="center")
        ax.text(x + 18, sy + 68, body, color=SUB, fontsize=11.8, va="top", linespacing=1.55)
        if i < 4:
            ax.annotate("", xy=(x + sw + gap - 6, sy + sh / 2), xytext=(x + sw + 6, sy + sh / 2),
                        arrowprops=dict(arrowstyle="-|>", color=CYAN_DIM, lw=2.4))

    ax.text(W / 2, sy + sh + 46,
            "Typical run — applying \u201cHome\u201d touches ~84 rules; every matched / changed / failed rule is counted and reported.",
            color=SUB, fontsize=13, ha="center", va="center", style="italic")

    footer(ax, "ZERO-POWERSHELL CORE   •   NATIVE COM (HNetCfg.FwPolicy2)   •   WINDOWS 10 / 11   •   RUN AS ADMINISTRATOR")

    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "firewall_profiles_infographic.png")
    fig.savefig(out, dpi=150)
    plt.close(fig)
    return out


# ============================================================ infographic 2
def stack_infographic():
    fig, ax = new_canvas()
    header(ax, "AUTOCOMMAND  —  WINDOWS SECURITY & HARDENING SUITE",
           "Zero-PowerShell monitoring & mitigation  •  WPF dashboard  •  integrated AI co-pilot")

    panels = [
        ("EVENT-DRIVEN ENFORCER", CYAN, [
            "WMI watcher: instant SSTP / kernel-debug",
            "adapter detection (zero idle CPU)",
            "State reconciliation sweep: SSTP service",
            "and BCD kernel-debug state re-asserted",
            "Toast alerts + auto-mitigate toggle",
        ]),
        ("ADVANCED FIREWALL", ORANGE, [
            "5 one-click profiles (Shield Up → Home)",
            "Group enable / disable via native COM",
            "1-click inbound+outbound process block",
            "Live apply progress + per-group report",
            "Config overrides with drift detection",
        ]),
        ("AI SECURITY CO-PILOT", PURPLE, [
            "Dedicated AI Assistant tab",
            "Gemini cloud assistant",
            "Local GGUF models via LLamaSharp",
            "Review-before-run command execution",
            "AI-auditable pages (GetAuditContext)",
        ]),
        ("NETWORK ANALYTICS", GREEN, [
            "Sysmon operational-log EventLogWatcher",
            "Raw-socket packet sniffer",
            "Live connections + Geo-DNS reverse map",
            "SVCHOST service monitor",
            "DNS resolution service",
        ]),
        ("HARDENING & UEFI", BLUE, [
            "UEFI DBX download / apply + revocation",
            "EFI integrity baseline drift checks",
            "Signature-verified sigcheck auto-download",
            "LSA protection / UAC / telemetry toggles",
            "SetupAPI P/Invoke device takedown",
        ]),
        ("APP & BLOATWARE CONTROL", RED, [
            "Default Apps: Windows Settings parity",
            "list with versions & publishers",
            "Authenticode-validated signature status",
            "1-click bloatware removal:",
            "Outlook / Xbox / Family / Phone",
        ]),
    ]

    cw, ch, gap = 489, 300, 22
    x0, y0 = 44, 180
    for i, (title, accent, bullets) in enumerate(panels):
        col, row = i % 3, i // 3
        x = x0 + col * (cw + gap)
        y = y0 + row * (ch + 26)
        panel(ax, x, y, cw, ch)
        ax.add_patch(FancyBboxPatch((x, y), cw, 8, boxstyle="round,pad=0,rounding_size=4",
                                    linewidth=0, facecolor=accent, zorder=2))
        ax.text(x + 24, y + 44, title, color=accent, fontsize=17, fontweight="bold", va="center")
        body = "\n".join("•  " + b for b in bullets)
        ax.text(x + 24, y + 78, body, color=SUB, fontsize=12.4, va="top", linespacing=1.75)

    footer(ax, "WINDOWS 10 / 11   •   .NET 10 (WPF)   •   ADMINISTRATOR PRIVILEGES   •   APACHE-2.0   •   github.com/dparksports/AutoCommand")

    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "security_stack_infographic.png")
    fig.savefig(out, dpi=150)
    plt.close(fig)
    return out


if __name__ == "__main__":
    print(firewall_infographic())
    print(stack_infographic())
