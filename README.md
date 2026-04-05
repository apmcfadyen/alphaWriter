# Overview

alphaWriter is a novel-writing and novel-editing tool. Mostly a novel-editing tool. 

The goal of alphaWriter is simple: build the absolute best novel-writing companion possible, while respecting the writer's privacy and having no exposure to AI. In other words, it relies heavily on Natural Language Processing (NLP) models, which run locally on the end user's machine and send back no telemetry.

Its structure is inspired by project structures in IDEs--rather than being a conventional word processor, it allows the creation of Books, which have any number of Chapters, which have any number of Scenes. Scenes are where the writing happens; they can contain Characters, Items, and Locations. Each of these have (probably too much) metadata associated with them, and alphaWriter tracks relationships and which objects are in which Chapters/Scenes.

While it supports every aspect of novel writing from prewriting on, its ideal use case is a very specific part of the novel writing process, for which very few (non-AI) tools exist to help: right after Draft 1, and before sending to human alpha readers. The writer has a good understanding of what the book is and their intentions for it. A basic plot exists, biographies and arcs have been sketched out, and the writer has a relative level of comfort with their work's tone and style. 

In fact, I would not recommend this as a tool to write a novel from scratch. alphaWriter contains many reports of "vanity metrics" and little widgets that will actually distract from meaningful progress with Draft 1.

## Core Features:
- Scene, Chapter, and Book aggregated wordcounts
- Ability to set book wordcount goal with dynamic percentage of total
- Ability to add Characters, Locations, and Items (Entities) and note their presence in scenes.

## Text Editor Features:
- Basic bold, italics, underline styling
- In-line "hyperlinking" of known Characters, Locations, and Items (If you've programmed before, think "peeking the definition" of an entity in a book)
- Able to add editing to-dos and "comments" using // text for single paragraph and /* text */ for multiple paragraphs. Comments do not count towards wordcount

## Reporting Features:
- Filterable Entity Relationship Diagram illustrating which Characters, Locations, and/or Items are in scenes together. Line thickness scales with quantity of shared scenes
- Word use frequency histogram
- "Problem word" report showing semantically-relevant words used >0.5% of total
- Wordcount by viewpoint character
- Daily Progress Log showing wordcount delta by date
- Natural Language Processing Assessment of all scenes with downloadable model for deeper analysis
    - Pacing Heatmap showing wordcount by Scene
    - Style Consistency Heatmap based on average sentence length, dialogue percentage, and contraction use
    - Emotion Distribution chart showing "timing" and quantity of certain emotions throughout the scene
    - Analysis notes identifying long sentences, long periods of time without dialogue, and sudden emotional changes.
    - Adverb/adjective density analysis, flagging overuse of adverbs.
 
<img width="1910" height="989" alt="image" src="https://github.com/user-attachments/assets/0cc8ae77-8caf-474f-84be-0c285f857edb" />

<img width="1661" height="827" alt="image" src="https://github.com/user-attachments/assets/773d2f62-c469-4a2b-9cdc-63868a2b21b9" />

<img width="1253" height="703" alt="image" src="https://github.com/user-attachments/assets/5f8603f6-0c07-4bed-974c-770446e800be" />

<img width="1660" height="496" alt="image" src="https://github.com/user-attachments/assets/7ac8001f-db12-4d06-a9a5-1f37e5774da9" />

<img width="1494" height="646" alt="image" src="https://github.com/user-attachments/assets/ecca76b8-b530-4aca-a6c2-ead0dc4d8d46" />

<img width="1473" height="653" alt="image" src="https://github.com/user-attachments/assets/8d360131-a18c-4e80-b391-b376eb389ea5" />
