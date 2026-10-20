"""
Builds alphaWriter's sensory lexicon from the Lancaster Sensorimotor Norms.

The editor uses it to show which senses a scene draws on (sight, sound, touch, taste,
smell, inner body) and to find abstract passages: words with low perceptual strength in
every sense ("situation", "decided", "aspect") read as telling rather than showing.

Source: Lynott, Connell, Brysbaert, Brand & Carney (2020). The Lancaster Sensorimotor
Norms: multidimensional measures of perceptual and action strength for 40,000 English
words. Behavior Research Methods, 52, 1271-1291. https://doi.org/10.3758/s13428-019-01316-z
Data: https://osf.io/7emr6/ (CC BY 4.0). Only the six perceptual means are kept, rounded.

Output:
  alphaWriter/Resources/Raw/lexicons/sensorimotor.tsv
    word<TAB>auditory<TAB>gustatory<TAB>haptic<TAB>interoceptive<TAB>olfactory<TAB>visual
    (ratings 0-5, one decimal; single words only, lower case)

Usage:  python build_sensorimotor.py [path-to-downloaded-csv]
        Without a path, the CSV (17 MB) is downloaded to a temporary folder and deleted.
"""
import csv
import shutil
import sys
import tempfile
import urllib.request
from pathlib import Path

CSV_URL = "https://osf.io/download/48wsc/"   # Lancaster_sensorimotor_norms_for_39707_words.csv
SENSES = ["Auditory", "Gustatory", "Haptic", "Interoceptive", "Olfactory", "Visual"]
OUT = Path(__file__).resolve().parents[2] / "alphaWriter" / "Resources" / "Raw" / "lexicons" / "sensorimotor.tsv"


def build(csv_path: Path) -> int:
    rows = []
    with open(csv_path, encoding="utf-8-sig", newline="") as f:
        for r in csv.DictReader(f):
            word = r["Word"].strip().lower()
            if not word.isalpha():   # skip multi-word items ("a cappella") and hyphenated forms
                continue
            rows.append((word, [round(float(r[f"{s}.mean"]), 1) for s in SENSES]))
    rows.sort()
    OUT.parent.mkdir(parents=True, exist_ok=True)
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write("# Lancaster Sensorimotor Norms (Lynott et al., 2020), CC BY 4.0, https://osf.io/7emr6/\n")
        f.write("# word\tauditory\tgustatory\thaptic\tinteroceptive\tolfactory\tvisual\n")
        for word, values in rows:
            f.write(word + "\t" + "\t".join(f"{v:g}" for v in values) + "\n")
    return len(rows)


def main() -> None:
    if len(sys.argv) > 1:
        print(build(Path(sys.argv[1])), "words ->", OUT)
        return
    tmp = Path(tempfile.mkdtemp(prefix="lancaster-"))
    try:
        path = tmp / "norms.csv"
        urllib.request.urlretrieve(CSV_URL, path)
        print(build(path), "words ->", OUT)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    main()
