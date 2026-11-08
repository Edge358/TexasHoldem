"""Measure whether the hole cards are vertically centred in their slot.

Why this exists: three separate "fixes" for the card alignment were reported as
working, from looking at screenshots, and none of them were. Android will not
vertically centre text in a fixed-height box, and the error is small enough to
argue about and large enough to look wrong. Measure it instead.

The script finds the highlighted card slot by its near-white 2px border (the
human seat's highlight, present whenever it is that player's turn), then reports
the gap in physical pixels above and below the actual glyph ink.

Usage, from the repo root in PowerShell, with a screenshot pulled off the
emulator. Always name the device with -s: with a phone on USB, a bare adb
command fails or picks the wrong device.

    $adb = "$env:LOCALAPPDATA/Android/Sdk/platform-tools/adb.exe"
    & $adb -s emulator-5554 shell screencap -p /sdcard/s.png
    & $adb -s emulator-5554 pull /sdcard/s.png s.png
    $env:PYTHONIOENCODING = "utf-8"; python Tools/measure_card_slot.py s.png

It currently reads 10 above / 9 below: the cards sit 1 px low, which is inside
tolerance and not worth re-tuning, since one physical pixel is about half a design
unit. (Before its trim was made symmetric the script read 10 / 10, which was the
script being 1 px off, not the cards being exact.) Anything past a few pixels of
imbalance means the TranslationY on CardSlotLabelStyle needs revisiting.

Limitation, and it matters: this measures the extent of the *ink*, which equals
visual centring only for text with no descenders. Card ranks and suit glyphs have
none, so the reading is valid for the card slots. It is not valid for anything
containing parentheses or letters like g, p or y: the bet labels read about 8px
"low" purely because "(6)" descends below the baseline, and they are in fact
centred correctly. Do not point this at them and then "fix" a non-problem.

Requires PIL and numpy, both present in the machine's Anaconda install.
"""

import sys

import numpy as np
from PIL import Image


def longest_run(xs):
    """Longest contiguous run in a sorted array of column indices."""
    best = cur = 1
    start = best_start = xs[0]
    for i in range(1, xs.size):
        if xs[i] == xs[i - 1] + 1:
            cur += 1
        else:
            if cur > best:
                best, best_start = cur, start
            cur, start = 1, xs[i]
    if cur > best:
        best, best_start = cur, start
    return int(best_start), int(best)


def find_highlighted_slot(a):
    """Locate the card slot drawn with the near-white highlight border."""
    height = a.shape[0]
    near_white = (a[:, :, 0] > 205) & (a[:, :, 1] > 205) & (a[:, :, 2] > 205)

    edges = []
    for y in range(height):
        xs = np.flatnonzero(near_white[y])
        if xs.size < 150:
            continue
        x0, run = longest_run(xs)
        if run > 150:
            edges.append((y, x0, run))

    groups = []
    for edge in edges:
        if groups and edge[0] - groups[-1][-1][0] <= 2:
            groups[-1].append(edge)
        else:
            groups.append([edge])

    # The card slot is the highlighted box roughly 80px tall. The seat info box
    # carries the same border but is much taller.
    for i in range(len(groups) - 1):
        top, bottom = groups[i], groups[i + 1]
        box_height = bottom[0][0] - top[-1][0]
        if 60 < box_height < 100 and abs(top[0][1] - bottom[0][1]) < 20:
            return top[-1][0], bottom[0][0], top[0][1], top[0][2]
    return None


def main():
    if len(sys.argv) != 2:
        print(__doc__)
        return 2

    a = np.asarray(Image.open(sys.argv[1]).convert('RGB')).astype(int)
    slot = find_highlighted_slot(a)
    if slot is None:
        print('Could not find the highlighted card slot.')
        print('It only appears while it is the human seat\'s turn to act.')
        return 1

    y_top, y_bottom, x0, width = slot
    # Step inside the border so its anti-aliasing is not counted as glyph ink.
    # y_top is the last row of the top border and y_bottom the first row of the
    # bottom border, so skip the same two rows at each end. The first version
    # skipped two at the top but one at the bottom, which read 1 px low: its
    # "10 above / 10 below" was really 12 and 11 from the border.
    inner = a[y_top + 3:y_bottom - 2, x0 + 5:x0 + width - 5]
    rows = inner.shape[0]

    background = np.median(inner.reshape(-1, 3), axis=0)
    distance = np.sqrt(((inner - background) ** 2).sum(axis=2))
    ink_per_row = (distance > 60).sum(axis=1)

    inked = np.flatnonzero(ink_per_row > 3)
    if inked.size == 0:
        print('Slot found but empty. Deal a hand first.')
        return 1

    above = int(inked[0])
    below = int(rows - 1 - inked[-1])
    off = above - below

    print('slot interior : %d px' % rows)
    print('glyph ink     : rows %d..%d (%d px tall)' % (inked[0], inked[-1], inked[-1] - inked[0] + 1))
    print('gap above     : %d px' % above)
    print('gap below     : %d px' % below)
    print('off centre    : %+d px  ->  %s' % (off, 'PASS' if abs(off) <= 3 else 'FAIL'))
    return 0 if abs(off) <= 3 else 1


if __name__ == '__main__':
    sys.exit(main())
