"""
Writes a sample alphaWriter library (books.json) for UI snapshot runs.

The content is chosen to stress the layout, not to be good fiction:
  * a long book title and long chapter / scene titles (truncation, tooltips)
  * double-digit chapter numbers (number column width)
  * an Outline scene with words in it (word-count rules), an empty chapter
  * prose that triggers editorial notes (adverbs, named emotions, echoes, a long sentence)
  * characters with aliases, locations and items (Story Bible, entity highlighting)

Usage:  python make_sample_library.py <output-dir>
"""
import json
import sys
import uuid
from pathlib import Path

OUTLINE, DRAFT, FIRST_EDIT, SECOND_EDIT, DONE = range(5)


def uid() -> str:
    return str(uuid.uuid4())


def paragraphs(*ps: str) -> str:
    return "".join(f"<p>{p}</p>" for p in ps)


FOG = paragraphs(
    "Maya walked quickly and quietly along the harbour wall. The fog rolled in from the water, "
    "and the fog swallowed the lamps one by one until the fog was all she could see.",
    "“You shouldn’t be out here,” Tomas said softly. He was leaning against the old "
    "customs house, the brass key turning in his fingers.",
    "Maya felt angry. She was tired of being told where she could and could not go, tired of the "
    "careful voices and the careful faces, tired of a town that had decided long before she was born "
    "what a harbourmaster’s daughter was for and had never once thought to ask her whether she "
    "agreed with any of it, or whether she had plans of her own.",
    "“Then walk with me,” she said. // check Tomas's voice here",
    "The door was opened by someone inside. Light spilled across the wet stones of the quay.",
)

STATION = paragraphs(
    "The station clock had stopped at a quarter past four, and nobody had thought to wind it.",
    "Tomas waited on the bench with his collar up. The train was late. The train was always late.",
    "When it finally came, it came without a whistle, sliding out of the dark like something "
    "that did not want to be noticed.",
)

# Typed with a plain keyboard: straight quotes and apostrophes. These used to break
# loading a scene into the editor (see JsArg in WriterPage.xaml.cs).
LEDGER = paragraphs(
    "\"Where did you find it?\" Tomas asked. He didn't look up from the ledger.",
    "\"In the customs house,\" Maya said. \"Behind the stove. Somebody wanted it found.\"",
    "He turned the page. The handwriting changed halfway down, as if someone else had taken over the "
    "lie. She was nervous, and she hated that he could see it.",
    # Phase 1 triggers: capitalised tag, action tag, head-hop, redundancy, misspelt name
    '"Really?" She asked.',
    "\"It's nothing,\" Tomas smiled. Tomas knew she didn't believe him.",
    "Maya nodded her head and made a decision. She would show Thomas the ledger tomorrow.",
    # Batch 2 triggers: ambiguous pronoun, Eleonora with no pronouns set (hint in Story Bible)
    "Tomas handed Erik the ledger. He read it twice.",
    "Eleonora watched from the stairs. Eleonora gathered her shawl. Eleonora pressed her lips together. "
    "Eleonora lifted her chin.",
)

STORM_OUTLINE = paragraphs(
    "Outline: the storm hits the harbour. Maya and Tomas shelter in the customs house. "
    "She finds the ledger. He lies about the key.",
)

BOOK = {
    "Id": uid(),
    "Title": "The Long Winter of the Northern Reaches: A Harbour Mystery",
    "Author": "A. N. Author",
    "Description": "A harbourmaster's daughter, a missing ledger, and a town that keeps its secrets.",
    "AuthorBio": "",
    "WordTarget": 90000,
    "Chapters": [],
    "Characters": [
        {"Id": uid(), "Name": "Maya", "FullName": "Maya Lindqvist", "Aka": "the harbourmaster's daughter",
         "Description": "Restless, observant, nineteen.", "Bio": "", "Notes": "", "ImagePath": "", "Pronouns": "she/her"},
        {"Id": uid(), "Name": "Tomas", "FullName": "Tomas Ekberg", "Aka": "Tom",
         "Description": "Customs clerk with a key he shouldn't have.", "Bio": "", "Notes": "", "ImagePath": "", "Pronouns": "he/him"},
        {"Id": uid(), "Name": "Erik", "FullName": "Erik Lund", "Aka": "",
         "Description": "The harbourmaster's clerk.", "Bio": "", "Notes": "", "ImagePath": "", "Pronouns": "he/him"},
        {"Id": uid(), "Name": "Eleonora", "FullName": "Eleonora Vass-Holmqvist", "Aka": "",
         "Description": "Owns half the harbour and wants the other half.", "Bio": "", "Notes": "", "ImagePath": ""},
    ],
    "Locations": [
        {"Id": uid(), "Name": "Customs House", "Aka": "the old customs house",
         "Description": "Shuttered since the war.", "Notes": "", "ImagePath": ""},
        {"Id": uid(), "Name": "Harbour Wall", "Aka": "", "Description": "", "Notes": "", "ImagePath": ""},
    ],
    "Items": [
        {"Id": uid(), "Name": "Brass Key", "Aka": "the key", "Description": "Fits no door in town.", "Notes": ""},
        {"Id": uid(), "Name": "Ledger", "Aka": "", "Description": "", "Notes": ""},
    ],
    "DailyWordSnapshots": {"2026-10-14": 180, "2026-10-15": 260, "2026-10-17": 410},
}

chars = {c["Name"]: c["Id"] for c in BOOK["Characters"]}


def scene(title, status, content="", pov=None, cast=()):
    return {
        "Id": uid(), "Title": title, "Status": status, "Content": content,
        "ViewpointCharacterIds": [chars[pov]] if pov else [],
        "CharacterIds": [chars[c] for c in cast], "LocationIds": [], "ItemIds": [],
        "Goal": "", "Conflict": "", "Outcome": "", "Notes": "",
    }


def chapter(title, *scenes):
    return {"Id": uid(), "Title": title, "Scenes": list(scenes)}


BOOK["Chapters"] = [
    chapter("Arrival", scene("The Station at a Quarter Past Four", DRAFT, STATION, "Tomas", ["Tomas"])),
    chapter("The Docks, and Everything the Fog Was Hiding From the Town",
            scene("The Fog Rolls In", FIRST_EDIT, FOG, "Maya", ["Maya", "Tomas"]),
            scene("Storm", OUTLINE, STORM_OUTLINE)),
    chapter("Ledger",
            scene("Straight Quotes in the Ledger", DRAFT, LEDGER, "Maya", ["Maya", "Tomas"]),
            scene("Untitled scene", DRAFT, "")),
] + [chapter(f"Chapter {n}") for n in range(4, 12)]

out = Path(sys.argv[1] if len(sys.argv) > 1 else "sample-library")
out.mkdir(parents=True, exist_ok=True)
(out / "books.json").write_text(json.dumps([BOOK], indent=2, ensure_ascii=False), encoding="utf-8")
print(out / "books.json")
