# Agreement — annotating

**v0.** Depends on nothing. Everything else depends on this.

**How two parties mark up each other's files without either of them having to
diff anything.** Deliberately minimal — this is the most-depended-on thing
published here, so a revision costs everyone downstream an afternoon.

---

## The protocol

**Whoever wrote a file does not annotate it. Whoever annotates a file does not
edit it.** That is the whole of it, and it exists so that neither party has to
reconcile two versions of the same document.

## Where annotations go

**For any file `plan.md`, annotations go in `plan.notes.md`, beside it.**

The source file is never modified. **A notes file is never annotated** — there is
no `plan.notes.notes.md`.

**A file stays editable until it has annotations.** Until then nobody has
responded and revising it is just continuing to write. **Once notes exist the
source is fixed**, because the quotes in those notes would come loose. **Deleting
the notes releases it.**

## The format

**A heading giving the location and author, a blockquote of the exact source
text, then the comment.**

```markdown
# Annotations for plan.md

## line 13, col 24–48 — jeff
> with a cap of thirty seconds

Where did thirty come from?

## lines 40–47 — jeff
> ## Venue
> This application will be run locally. It will be browser based, but
> …
> to be in the position of rewriting this portion when that happens.

Tauri is the likely landing spot, not Deno.
```

**Rules:**

- **Quote the source markdown, not the rendered text.** The quote is how a reader
  finds the span, and **it is the only part that must be exact.**
- **Quote exactly what was annotated.** A phrase selection quotes the phrase; a
  block selection quotes the block.
- **Elide long quotes** with `…` on its own line — first line, ellipsis, last
  line.
- **Coordinates are optional.** If you can quote accurately but are unsure of
  line numbers, write `## — jeff` and leave them off.
- **An entry with no quote at all** is a comment on the whole document. It sorts
  first.

Entries appear in document order.

## Asking for a specific kind of answer

**A file may ask for structured responses using heading keywords.** Any heading
level works; the prompt block runs until the next heading of the same or higher
level.

```markdown
## Question: Which storage adapter for v1?
Freeform. Anything the reader wants to say.

## Select: Which of these should land this sprint?
- Block-level anchoring
- Prompt widgets
- Notes file format

## Affirm: Ship behind a flag?
```

`Select` uses the first bulleted list in its block as the options; **any number
may be chosen.** `Affirm` is yes or no. **Only the first word of the heading is
matched, case-insensitively** — whatever follows is free text.

**Answers land in the notes file like everything else, restating the question so
the notes stand alone if the source is discarded:**

```markdown
## Question: Which storage adapter for v1?
**Answer:** File System Access API. No server process this early.

## Select: Which of these should land this sprint?
- [x] Block-level anchoring
- [ ] Prompt widgets
- [x] Notes file format
**Note:** prompt widgets can wait until the format settles.

## Affirm: Ship behind a flag?
**Answer:** No.
```

**Every response type may carry an additional freeform note**, so there is never
a need for an "other" option.

### A rule for whoever writes the prompt

> **Never enumerate an escape hatch.** No *"other"*, no *"it depends"*, no *"leave
> it to the developer."*

**Not because those answers are wrong — because they are always available
anyway**, and enumerating one changes what it costs.

**An escape hatch on the list can be selected and you are done.** Nothing to
write, no reason given, nothing for anyone to see later. **An answer that is
*not* on the list has to be written down**, which means it arrives with a reason
attached.

> **Enumerating "other" converts a recorded deviation into an unrecorded
> compliance.**

**Put only specific, castable options on the list.** If none of them fits, the
person will say so in their own words, and **their own words are the thing you
wanted.**

**A prompt may be skipped on purpose, and that is recorded**, so *considered and
passed* survives as something different from *never got to it*:

```markdown
## Question: Should the shelf survive a restart?
**Skipped.**
```

## What this does not constrain

**Prompts do not limit where else annotations may go.** A file with five prompts
may also carry thirty freeform annotations anywhere in it, and a file with no
prompts is annotated exactly the same way.

**The keyword set is open.** An agreement that uses this one may declare
additional keywords with meaning particular to it. **A reader that does not
recognise a keyword treats the heading as an ordinary annotation** — it must not
drop it.

---

## Roles you must cast

> **Nothing.** This agreement casts all of its own roles. It is the base, and a
> base with local variation in its mechanism would break everything resting on
> it.

**You may still vary it** — in a notes file beside your copy, with a reason, like
anything else. **But understand what it costs:** every agreement you adopt that
depends on this one is now being read through a mechanism the publisher does not
know about.
