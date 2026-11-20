# Metadata service (E07-S01)

Every screen reads an item's descriptive metadata through the queries below and changes it through one command. The rules of precedence (user, then AI, then file; D020, D047) stay in the domain model (`ItemMetadata`, see `docs/domain-model.md`); the service only loads, applies and stores.

Code: `src/MediaFileClassifier.UseCases/Metadata`.

## Reading

| Query | Returns |
|-------|---------|
| `GetItemMetadataQuery(itemId)` | `ItemMetadataView` of one item |
| `GetItemsMetadataQuery(itemIds)` | One view per distinct item, in the order given, loaded with one query per table |

An `ItemMetadataView` lists every descriptive field, empty ones too (`view[field]`). A `FieldMetadataView` holds the effective value and each layer: the user value (origin Edited or Accepted, the run it was accepted from), the latest AI value (run, model, confidence) even when a user value wins, the file's value, and the flagged new AI suggestion. The view also holds the effective tags, each with its source and suggestion kind (Added or Removed), and the tombstones (rejected tags). It reuses the domain records, so no mapping code is needed. A rich item (two analyzers with two runs each, every field, 80 AI tags) loads in about 2 ms.

## Writing

`EditMetadataCommand(ItemIds, Edits)` applies a list of `MetadataEdit`s, in order, to every item:

| Edit | Effect |
|------|--------|
| `SetField(field, value)` | A user value with origin Edited; clears its suggestion. An empty value hides the AI value. |
| `RevertField(field)` | Removes the user value; the AI or file value is effective again. |
| `ConfirmField(field)` | Copies the effective AI value into the user layer as Accepted, with its other-language form. |
| `AcceptFieldSuggestion(field)` | Replaces the user value with the new AI suggestion, as Accepted. |
| `DismissFieldSuggestion(field)` | Keeps the user value and clears the flag. |
| `AddTag(text)` | Adds a user tag as typed (cleaned, see below) and removes its tombstone. |
| `RemoveTag(tag)` | Removes the user tag with no tombstone; an AI or file tag of the same name shows again. |
| `RejectTag(tag)` | Removes the user tag and keeps a tombstone, so runs and the file don't add it again. Only items that show the tag get the tombstone, so a bulk reject doesn't block it on selected items that never had it. |
| `ConfirmTag(tag)` | Copies an effective AI tag into the user layer as Accepted. |
| `AcceptTagSuggestion(tag)` / `DismissTagSuggestion(tag)` | Follow or clear a tag's new AI suggestion. |

An edit that doesn't apply to an item (confirming a field with no AI value, removing a tag it doesn't have) leaves that item as it is, so one list works for a whole selection. A dismissed suggestion stays dismissed until a run gives a different result (a field's value differs from the user value and from the analyzer's previous run; an accepted tag the AI dropped is flagged again only if the previous run still gave it).

All the edits of one item are applied in one writer transaction together with its keyword index row, so the item is never half saved and the change is searchable on the next query. Many items are written in batches of 100 (`IItemMetadataRepository.UpdateManyAsync`), each batch its own short write so other writes get their turn (D072). Items that don't exist are skipped, and items an edit didn't change aren't rewritten. If a batch fails or is cancelled, the batches before it stay written.

The result is `EditMetadataResult(ChangedItemIds, Error)`.

## Notification

A command publishes one `ItemsMetadataChanged` with every changed item, `Change` flags (`Favorite`, `Fields`, `Tags`) and the changed fields, also when a later batch fails (the batches already written are published). The fields and tags named are those that really changed on at least one written item, not every edit in the list. A command that changes nothing publishes nothing. If a handler throws while the change is published after a failed write, the write's own error is the one that reaches the caller. `Ui/Browser/MetadataChangeHandler` refreshes the grid and reloads the details panel when its item is among them.

## Validation (E07-S01-T04)

`MetadataValidator` checks the edits before anything is written. The first problem is returned as `EditMetadataResult.Error`, a `MetadataValidationError` with a code, the field or tag, and the limit; nothing is written and nothing is published. The UI turns the code into a message. The limits are constants in `MetadataLimits`:

| What | Limit |
|------|-------|
| Title | 200 characters |
| Description, summary, notes | 10,000 characters |
| OCR text, transcript | 100,000 characters |
| List field (objects, scenes, actions, emotions, people) | 100 entries of up to 100 characters |
| Tag | 100 characters |
| User tags of one item | 200 |

A text value for a list field, or a list for a text field, is refused too. AI values and values read from files are not limited here.

A tag the user types is cleaned with `TagNormalizer.CleanUserTag`: punctuation around it, underscores and extra white space are handled as for AI tags, and nothing else changes: the case, the word form, synonyms and generic words stay as typed (D047). A tag is refused when it is empty after cleaning, holds a control character, a comma or a semicolon, or is too long.

The 200-tag limit depends on what an item already has, so when the edits add tags (`AddTag`, `ConfirmTag`, `AcceptTagSuggestion`) the command first tries them on the loaded metadata, without storing it. If any item would pass 200 user tags, the whole command is refused and no batch is written. An item that already has more than 200 isn't blocked from edits that add no tag. The check is a read before the writes, so a concurrent write in between could leave an item at 201, and `AddTagAsync` and `AcceptAll` don't check the limit; this is accepted for a single-user app.

## Tag autocomplete

The metadata editor's tag input (E07-S02-T02) asks `SuggestTagsQuery(Prefix, Limit)` for tags as the user types. The handler wraps `GetEffectiveTagsAsync`, so the tags match by either language form (D382) and come most used first, with their item counts, and the editor leaves out the tags the item shows already. There is no schema change.

## Unsaved changes and concurrent changes (E07-S12)

Staged editors register with `UnsavedChangesGuard` (`IStagedEditor`: `HasUnsavedChanges`, `Description`, `SaveAsync`, `Discard`). Anything that leaves the edited item calls `ConfirmLeaveAsync()` first: `SelectionModel.ConfirmChange` for selection gestures, the details panel for collapsing or hiding it, `LibraryOpener` for switching library and `QuitGuard` for closing the window. It shows Save / Discard / Stay for each editor with changes, and answers false on Stay or a failed save. Bulk editing (E07-S03) registers its own `IStagedEditor`.

If the selection moves without a gesture while changes are staged (a filter removes the item), the details panel keeps the item (`IsPinned`) until the changes are saved or dropped.

When `ItemsMetadataChanged` arrives for the shown item and the editor has staged changes, `MetadataEditorViewModel.MergeChangesAsync` refreshes the fields and tags with nothing staged and keeps the staged ones. If a staged field is among the changed `Fields`, or a staged tag change no longer fits the stored tags, the Save bar names them in a notice. It does nothing while a save runs, because the save reloads the editor itself.

## Provenance and AI-value actions (E07-S11)

`GetItemMetadataQuery` adds `ModelNames` (registration to name and version) so the editor can say which model wrote a value. A field's mark comes from its effective value: AI, a user value with origin Edited, or one with origin Accepted; a value read from the file has none. A user value with a new AI suggestion also gets the New suggestion mark. The marks are `Border.Provenance` styles (icon and word, so colour is never the only cue). Per field the editor stages Confirm AI value (`ConfirmField`), Use the AI or file value (`RevertField`), Accept suggestion (`AcceptFieldSuggestion`) and Dismiss (`DismissFieldSuggestion`); typing replaces a staged confirm, accept or dismiss, and Undo or Cancel drops it. A tag chip with a suggestion stages `AcceptTagSuggestion` or `DismissTagSuggestion`, unless a removal or rejection is staged on it. A field the describer can't produce (`GetDescriberFieldsQuery`) and that has no value shows "Not available with the current model".
