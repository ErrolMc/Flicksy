# Working in this repo

## Read this first

[ARCHITECTURE.md](docs/ARCHITECTURE.md) is the **index** — the always-loaded structural map: solution shape, the four processes, conventions, and a router to the rest. Per-editor detail lives in three leaf maps under [docs/architecture/](docs/architecture/): [drawing.md](docs/architecture/drawing.md) (the shared rendering library), [snip-editor.md](docs/architecture/snip-editor.md) (PostSnip), [video-editor.md](docs/architecture/video-editor.md) (VideoEditor). **Read the index before exploring the codebase, then open the leaf the change touches.** Use the tables and links to jump straight to the file — do not glob/grep the tree to rediscover structure that's already documented.

Together these are written to be self-sufficient: they name every project, every namespace folder, every drawing tool, every undo command, and the file behind each. If the answer to "where does X live?" is in there, do not search.

## Coding standards

C# style rules for all code in this repo:

- **A control-flow keyword never shares its line with the body.** `if`, `else`, `for`, `foreach`, `while`, `do`, etc. always put the body on the next line, even when it's a single statement.
  ```csharp
  if (blah)          // not: if (blah) doBlah();
      doBlah();
  ```
- **Use `var` only when the type name already appears on that line** — i.e. the right-hand side is a `new` or a cast. If the right-hand side is a method or property call whose type isn't written on the line, use the explicit type.
  ```csharp
  var a = new Foo();   // ok — Foo is on the line
  var b = (Foo)obj;    // ok — Foo is on the line
  int c = Blah();      // ok — explicit type
  var c = Blah();      // not allowed — type not on the line
  ```

## Keeping the architecture docs current

Whenever you make a change that alters the structural picture, update the architecture docs in the **same change** — the relevant leaf ([drawing.md](docs/architecture/drawing.md) / [snip-editor.md](docs/architecture/snip-editor.md) / [video-editor.md](docs/architecture/video-editor.md)) for editor-specific structure, the [index](docs/ARCHITECTURE.md) for the solution shape, the processes, cross-cutting conventions, or its router. Structural means: anything a future session would need the map to discover.

Update when you:
- Add / remove / rename a project, folder, namespace, or top-level concept.
- Add / remove / rename a `DrawingItem` subclass, `IDrawingTool`, `IUndoableCommand`, ViewModel, UserControl, or major service (`IVideoPlayer`, `FfmpegLocator`, etc).
- Change the inter-process contract (Agent ↔ Snipper ↔ Editor args, temp-file conventions, hotkey).
- Change a load-bearing convention (e.g. how undo commands snapshot state, how tools register, how text editing is hosted).
- Change the save flow, capture pipeline, or media-playback model.
- Add a new NuGet dependency or external-binary requirement.

Do NOT update for:
- Refactors that don't change names or relationships (rename a private field, extract a private method).
- Bug fixes that don't change the public shape.
- Cosmetic XAML changes.
- One-line behavior tweaks (timing constants, default colors, etc).

## Style of edits to the architecture docs

These docs are optimized for **input-token efficiency** — the index loads into context every session, and a leaf loads whenever its editor is touched. Keep them that way.

Rules:
1. **Use tables and bullet lists, not prose.** A row in a table is cheaper to load and easier to scan than a paragraph.
2. **Link to files; don't duplicate their content.** Use `[name](path/to/file.cs)` and `[name](path:line)`. Future Claude will Read the file when it needs the body — don't paste it into the doc.
3. **One sentence per fact.** If a fact needs two sentences, the second is usually load-bearing nuance — keep it. If it's a restatement, cut it.
4. **No screenshots, no diagrams in ASCII art** unless they're materially clearer than text. The per-doc layout tree (e.g. [drawing.md](docs/architecture/drawing.md) §1) is the budget.
5. **No history.** Don't write "previously this was X, now it's Y." The doc describes the current build only. Git history is the changelog.
6. **No emojis.**
7. **Headings stay stable.** Sections are referenced by future edits. If you add a new section, add it; don't reorder existing ones unless the structure genuinely changed.
8. **The "Where to look for common changes" table** in each doc is a router. When you add a meaningfully new feature area, add a row — to the index (cross-cutting) or the relevant leaf (editor-specific).
9. **The index "Conventions" section** is for invariants that future sessions would otherwise re-derive. Add a bullet if you introduce a new convention; remove one if it's no longer true.

If an edit would push the doc significantly longer, consider whether the new material is structural (keep, terse) or detail that belongs in the source file's own comment / a follow-up doc.

## Domain glossary

[CONTEXT.md](docs/CONTEXT.md) is the project's domain glossary — the canonical names for the concepts this codebase deals with (snip editor vs. video editor, `Project`/`Track`/`Clip` shape, etc.) and which alternative terms to avoid. Read it before any design conversation. Update it inline (during the conversation, not after) whenever a term is sharpened or a new one emerges. It is a glossary only — no implementation details, no decisions, no plans.

Architectural decisions live in [docs/adr/](docs/adr/) following the standard ADR format. Add a new one only when a decision is hard to reverse, surprising without context, and the result of a real trade-off.

## What goes in this CLAUDE.md vs ARCHITECTURE.md vs CONTEXT.md

- **CLAUDE.md** = instructions to the model (this file). Keep it short.
- **ARCHITECTURE.md + docs/architecture/*.md** = the map of the code (index + per-editor leaves). Update when the map changes.
- **CONTEXT.md** = the glossary. Update when terms change.

Don't move build/run instructions, project descriptions, or file pointers into CLAUDE.md — they belong in the architecture docs so they aren't loaded into every session's context unconditionally.
