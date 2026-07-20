"""
One-off: transcode the WC2 Remastered music WAVs to Ogg Vorbis.

Source is the player's own install (44.1 kHz stereo 16-bit PCM, ~580 MB total).
Output goes to Assets/GameData/Extracted/Music, which .gitignore already covers
(/Assets/GameData/Extracted/) -- so this never enters git history and never
ships in a build.

Run:  python convert_music.py [--quality 0.4] [--only-redbook]
"""

import argparse
import os
import sys
import time

import soundfile as sf

SRC = r"C:\Program Files (x86)\Warcraft II Remastered\x86\Data\Music"
DST = r"C:\Users\mattc\UnityProjects\Craftwar\Assets\GameData\Extracted\Music"

BLOCK = 1 << 16  # frames per read; keeps peak memory flat regardless of track length


def convert(src_path, dst_path, quality):
    with sf.SoundFile(src_path) as fin:
        with sf.SoundFile(
            dst_path,
            mode="w",
            samplerate=fin.samplerate,
            channels=fin.channels,
            format="OGG",
            subtype="VORBIS",
            compression_level=quality,
        ) as fout:
            for block in fin.blocks(blocksize=BLOCK, dtype="float32"):
                fout.write(block)


def main():
    ap = argparse.ArgumentParser()
    # libsndfile maps compression_level 0.0 (best quality) .. 1.0 (smallest).
    ap.add_argument("--quality", type=float, default=0.4)
    ap.add_argument("--only-redbook", action="store_true",
                    help="skip the _opl OPL-synth alternates")
    args = ap.parse_args()

    if not os.path.isdir(SRC):
        sys.exit(f"source not found: {SRC}")
    os.makedirs(DST, exist_ok=True)

    names = sorted(n for n in os.listdir(SRC) if n.lower().endswith(".wav"))
    if args.only_redbook:
        names = [n for n in names if not n.lower().endswith("_opl.wav")]
    if not names:
        sys.exit("no .wav files found")

    total_in = total_out = 0
    start = time.time()
    for i, name in enumerate(names, 1):
        src_path = os.path.join(SRC, name)
        dst_path = os.path.join(DST, os.path.splitext(name)[0] + ".ogg")
        size_in = os.path.getsize(src_path)
        try:
            convert(src_path, dst_path, args.quality)
        except Exception as exc:                      # keep going; report at the end
            print(f"  [{i:2}/{len(names)}] FAILED {name}: {exc}")
            continue
        size_out = os.path.getsize(dst_path)
        total_in += size_in
        total_out += size_out
        print(f"  [{i:2}/{len(names)}] {name:<20} "
              f"{size_in/1e6:7.1f} MB -> {size_out/1e6:5.1f} MB")

    print(f"\n{len(names)} tracks in {time.time()-start:.0f}s")
    print(f"total {total_in/1e6:.0f} MB -> {total_out/1e6:.0f} MB "
          f"({100*total_out/max(total_in,1):.1f}%)")
    print(f"output: {DST}")


if __name__ == "__main__":
    main()
