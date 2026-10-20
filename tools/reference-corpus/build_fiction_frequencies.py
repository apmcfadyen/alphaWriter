"""
Builds alphaWriter's "typical fiction" word-frequency table from public-domain novels.

The editor compares an author's word use against this table to find their signature
words ("you use 'glance' 11x more than typical fiction"). Only word counts are kept:
the downloaded books are deleted when the script finishes.

Source: Project Gutenberg texts (public domain), fetched from the UK Mirror Service
mirror rather than gutenberg.org itself, as Gutenberg asks of automated tools.
One request at a time with a short pause between files.

Output:
  alphaWriter/Resources/Raw/lexicons/fiction-frequencies.tsv   word<TAB>count, most common first
  tools/reference-corpus/SOURCES.md                            the books actually used

Usage:  python build_fiction_frequencies.py
"""
import re
import shutil
import sys
import tempfile
import time
import urllib.request
from collections import Counter
from pathlib import Path

MIRROR = "https://www.mirrorservice.org/sites/ftp.ibiblio.org/pub/docs/books/gutenberg"
USER_AGENT = "alphaWriter-reference-corpus/1.0 (one-time build of a word-frequency table)"
PAUSE_SECONDS = 0.5
MAX_WORDS = 60000   # keep the most common words only
MIN_COUNT = 3

# Well-known novels and story collections, 1780s–1920s: Project Gutenberg ebook number -> title.
# The title is used when a file's header doesn't state one in a recognisable form.
BOOKS = {
    1342: 'Pride and Prejudice',
    84: 'Frankenstein',
    11: "Alice's Adventures in Wonderland",
    98: 'A Tale of Two Cities',
    1661: 'The Adventures of Sherlock Holmes',
    2701: 'Moby-Dick',
    1400: 'Great Expectations',
    174: 'The Picture of Dorian Gray',
    345: 'Dracula',
    768: 'Wuthering Heights',
    1260: 'Jane Eyre',
    76: 'Adventures of Huckleberry Finn',
    74: 'The Adventures of Tom Sawyer',
    2554: 'Crime and Punishment',
    1184: 'The Count of Monte Cristo',
    5200: 'Metamorphosis',
    64317: 'The Great Gatsby',
    120: 'Treasure Island',
    43: 'The Strange Case of Dr. Jekyll and Mr. Hyde',
    36: 'The War of the Worlds',
    35: 'The Time Machine',
    158: 'Emma',
    161: 'Sense and Sensibility',
    105: 'Persuasion',
    141: 'Mansfield Park',
    121: 'Northanger Abbey',
    730: 'Oliver Twist',
    766: 'David Copperfield',
    1023: 'Bleak House',
    46: 'A Christmas Carol',
    2852: 'The Hound of the Baskervilles',
    244: 'A Study in Scarlet',
    2097: 'The Sign of the Four',
    219: 'Heart of Darkness',
    2814: 'Dubliners',
    160: 'The Awakening',
    514: 'Little Women',
    2641: 'A Room with a View',
    2891: 'Howards End',
    145: 'Middlemarch',
    550: 'Silas Marner',
    6688: 'The Mill on the Floss',
    110: "Tess of the d'Urbervilles",
    153: 'Jude the Obscure',
    107: 'Far from the Madding Crowd',
    209: 'The Turn of the Screw',
    432: 'The Ambassadors',
    1257: 'The Three Musketeers',
    2600: 'War and Peace',
    28054: 'The Brothers Karamazov',
    1399: 'Anna Karenina',
    135: 'Les Misérables',
    996: 'Don Quixote',
    215: 'The Call of the Wild',
    910: 'White Fang',
    103: 'Around the World in Eighty Days',
    164: 'Twenty Thousand Leagues under the Sea',
    829: "Gulliver's Travels",
    521: 'Robinson Crusoe',
    45: 'Anne of Green Gables',
    113: 'The Secret Garden',
    236: 'The Jungle Book',
    4217: 'A Portrait of the Artist as a Young Man',
    5230: 'The Invisible Man',
    159: 'The Island of Doctor Moreau',
    155: 'The Moonstone',
    583: 'The Woman in White',
    1155: 'The Secret Adversary',
    863: 'The Mysterious Affair at Styles',
    41: 'The Legend of Sleepy Hollow',
    33: 'The Scarlet Letter',
    60: 'The Scarlet Pimpernel',
    1695: 'The Man Who Was Thursday',
    108: 'The Return of Sherlock Holmes',
    834: 'The Memoirs of Sherlock Holmes',
    55: 'The Wonderful Wizard of Oz',
    16: 'Peter Pan',
    1837: 'The Prince and the Pauper',
    86: "A Connecticut Yankee in King Arthur's Court",
    2610: 'Notre-Dame de Paris',
    541: 'The Age of Innocence',
    284: 'The House of Mirth',
    8492: 'The King in Yellow',
    10007: 'Carmilla',
}

WORD_RX = re.compile(r"[a-z]+(?:'[a-z]+)?")
START_RX = re.compile(r"\*\*\*\s*START OF (THE|THIS) PROJECT GUTENBERG.*?\*\*\*", re.IGNORECASE)
END_RX = re.compile(r"\*\*\*\s*END OF (THE|THIS) PROJECT GUTENBERG", re.IGNORECASE)
TITLE_RX = re.compile(r"^(?:Title:\s*|The Project Gutenberg e[Bb]ook of\s+)(.+?)(?:,\s*by\s.*)?$", re.MULTILINE)


def mirror_urls(ebook_id: int):
    digits = str(ebook_id)
    folders = "/".join(digits[:-1]) if len(digits) > 1 else "0"
    base = f"{MIRROR}/{folders}/{ebook_id}/{ebook_id}"
    return [(base + "-0.txt", "utf-8"), (base + ".txt", "utf-8"), (base + "-8.txt", "latin-1")]


def fetch(ebook_id: int, cache: Path):
    for url, encoding in mirror_urls(ebook_id):
        try:
            req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
            with urllib.request.urlopen(req, timeout=60) as resp:
                raw = resp.read()
            path = cache / f"{ebook_id}.txt"
            path.write_bytes(raw)
            return raw.decode(encoding, errors="replace")
        except Exception:
            continue
        finally:
            time.sleep(PAUSE_SECONDS)
    return None


def body_of(text: str) -> str:
    start = START_RX.search(text)
    end = END_RX.search(text)
    return text[start.end() if start else 0: end.start() if end else len(text)]


def main():
    repo = Path(__file__).resolve().parents[2]
    out_table = repo / "alphaWriter" / "Resources" / "Raw" / "lexicons" / "fiction-frequencies.tsv"
    out_sources = Path(__file__).resolve().parent / "SOURCES.md"
    cache = Path(tempfile.mkdtemp(prefix="aw-gutenberg-"))

    counts = Counter()
    used = []
    try:
        for i, (ebook_id, expected_title) in enumerate(BOOKS.items(), 1):
            text = fetch(ebook_id, cache)
            if text is None:
                print(f"[{i}/{len(BOOKS)}] {ebook_id}: not found, skipped", flush=True)
                continue
            title_match = TITLE_RX.search(text[:5000])
            title = title_match.group(1).strip() if title_match else expected_title
            words = WORD_RX.findall(body_of(text).lower().replace("’", "'"))
            counts.update(words)
            used.append((ebook_id, title, len(words)))
            print(f"[{i}/{len(BOOKS)}] {ebook_id}: {title[:60]} ({len(words):,} words)", flush=True)
    finally:
        shutil.rmtree(cache, ignore_errors=True)   # keep counts only, never the texts

    if not used:
        sys.exit("No books downloaded; nothing written.")

    total = sum(counts.values())
    out_table.parent.mkdir(parents=True, exist_ok=True)
    with out_table.open("w", encoding="utf-8", newline="\n") as f:
        f.write(f"# Word frequencies from {len(used)} public-domain novels (Project Gutenberg). "
                f"Built by tools/reference-corpus/build_fiction_frequencies.py\n")
        f.write(f"#total\t{total}\n")
        for word, n in counts.most_common(MAX_WORDS):
            if n < MIN_COUNT:
                break
            f.write(f"{word}\t{n}\n")

    with out_sources.open("w", encoding="utf-8", newline="\n") as f:
        f.write("# Reference corpus sources\n\n")
        f.write("`fiction-frequencies.tsv` holds word counts only, computed from these public-domain\n")
        f.write("Project Gutenberg ebooks (downloaded from a mirror, counted, then deleted).\n")
        f.write("Because the corpus is 1780s–1920s fiction, words that did not exist then are\n")
        f.write("not treated as “signature words” by the editor.\n\n")
        f.write(f"Total words counted: {total:,}\n\n| Ebook | Title | Words |\n|---|---|---|\n")
        for ebook_id, title, n in used:
            f.write(f"| {ebook_id} | {title} | {n:,} |\n")

    print(f"\n{len(used)} books, {total:,} words -> {out_table} ({out_table.stat().st_size // 1024} KB)")


if __name__ == "__main__":
    main()
