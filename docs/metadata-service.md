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
| `RejectTag(tag)` | Removes the user tag and keeps a tombstone, so runs and the file don't add it again. |
| `ConfirmTag(tag)` | Copies an effective AI tag into the user layer as Accepted. |
| `AcceptTagSuggestion(tag)` / `DismissTagSuggestion(tag)` | Follow or clear a tag's new AI suggestion. |

An edit that doesn't apply to an item (confirming a field with no AI value, removing a tag it doesn't have) leaves that item as it is, so one list works for a whole selection. A dismissed suggestion stays dismissed until a run gives a different result (a field's value differs from the user value and from the analyzer's previous run; an accepted tag the AI dropped is flagged again only if the previous run still gave it).

All the edits of one item are applied in one writer transaction together with its keyword index row, so the item is never half saved and the change is searchable on the next query. Many items are written in batches of 100 (`IItemMetadataRepository.UpdateManyAsync`), each batch its own short write so other writes get their turn (D072). Items that don't exist are skipped, and items an edit didn't change aren't rewritten. If a batch fails or is cancelled, the batches before it stay written.

The result is `EditMetadataResult(ChangedItemIds, Error)`.

## Notification

A command publishes one `ItemsMetadataChanged` with every changed item, `Change` flags (`Favorite`, `Fields`, `Tags`) and the changed fields, also when a later batch fails (the batches already written are published). A command that changes nothing publishes nothing. `Ui/Browser/MetadataChangeHandler` refreshes the grid and reloads the details panel when its item is among them.

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

The 200-tag limit depends on what an item already has, so when the edits add tags (`AddTag`, `ConfirmTag`, `AcceptTagSuggestion`) the command first tries them on the loaded metadata, without storing it. If any item would pass 200 user tags, the whole command is refused and no batch is written. An item that already has more than 200 isn't blocked from edits that add no tag.

## Tag autocomplete

Not part of this service: autocomplete from existing tags, ranked by usage, is planned in MFC-431 and MFC-112 and uses `GetUserTagsAsync` and `GetEffectiveTagsAsync`.
