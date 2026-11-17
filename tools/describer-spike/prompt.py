"""The draft describer prompt template for vision-language models (input for E06-S04-T04, MFC-311).

Version describe-2-draft. It differs from the app's describe-1 (ImageDescriptionContract) in:
- English and Russian forms of the title and of each tag in one response (D035, D348);
- rules for tags that make them useful as keywords (singular, lower case, no generic words), for T05;
- what to put in each list field, so the lists stop repeating each other;
- the kind of media as context (a video or GIF frame), which the app already passes as ImageDescriptionOptions.Context.
Limits stay as in describe-1 (D344, D350): 15 tags, 30 items per other list, 4,096 output tokens.
"""

VERSION = "describe-2-draft"

MAX_TITLE = 120
MAX_SUMMARY = 300
MAX_DESCRIPTION = 2000
MAX_TAGS = 15
MAX_LIST = 30
MAX_ITEM = 60
MAX_OUTPUT_TOKENS = 4096
TEMPERATURE = 0.2
PRESENCE_PENALTY = 1.5

SYSTEM = (
    "You describe photos, screenshots, documents and video frames for a personal media library, so they can be found "
    "by searching. Describe only what is visible; don't guess names, places or dates unless they are written in the "
    "image or are a well-known landmark. Answer with a single JSON object that matches the schema, and nothing else."
)

USER = """Describe this image. {context}Write every field in English, except the fields that end in _ru, which are in Russian.

- title: a short name for the image, 3 to 8 words, no full stop. title_ru: the same title in natural Russian.
- description: 2 to 4 sentences: the main subject, the setting, notable details, and any readable text quoted exactly.
- summary: one sentence.
- tags: at most {max_tags} keywords someone would type to find this image, most important first. Each has "en" and "ru": a lower-case noun or short noun phrase in the singular, the Russian one a natural Russian word in the nominative case (not a transliteration). No generic words like "image", "photo" or "picture", and no near-duplicates.
- objects: visible things. scenes: the place or setting. actions: what is happening. emotions: the mood. Short English phrases, at most {max_list} each; an empty list if nothing fits."""


# Variant B (describe-2-draft-b): the lists come before the tags, in the text and in the schema's property order (a
# grammar-constrained server writes them in that order), and an empty list is only for nothing of that kind visible.
# With variant A, qwen3-vl-4b filled 15 tag pairs first and then left half the lists empty: the presence penalty
# (D350) discourages repeating words the tags already used.
USER_B = """Describe this image. {context}Write every field in English, except the fields that end in _ru, which are in Russian.

- title: a short name for the image, 3 to 8 words, no full stop. title_ru: the same title in natural Russian.
- description: 2 to 4 sentences: the main subject, the setting, notable details, and any readable text quoted exactly.
- summary: one sentence.
- objects: the visible things. scenes: the place or setting. actions: what is happening. emotions: the mood. Short English phrases, at most {max_list} each. Leave a list empty only if nothing of that kind is visible.
- tags: last, at most {max_tags} keywords someone would type to find this image, most important first. Each has "en" and "ru": a lower-case noun or short noun phrase in the singular, the Russian one a natural Russian word in the nominative case (not a transliteration). No generic words like "image", "photo" or "picture", and no near-duplicates."""

VARIANTS = {"a": "describe-2-draft", "b": "describe-2-draft-b"}


def user_prompt(context, variant="a"):
    template = USER_B if variant == "b" else USER
    return template.format(context=f"{context} " if context else "", max_tags=MAX_TAGS, max_list=MAX_LIST)


def schema(variant="a"):
    if variant != "b":
        return SCHEMA
    order = ["title", "title_ru", "description", "summary", "objects", "scenes", "actions", "emotions", "tags"]
    return {**SCHEMA, "properties": {key: SCHEMA["properties"][key] for key in order}, "required": order}


def _text(max_length):
    return {"type": "string", "maxLength": max_length}


def _list(max_items):
    return {"type": "array", "items": _text(MAX_ITEM), "maxItems": max_items}


SCHEMA = {
    "type": "object",
    "properties": {
        "title": _text(MAX_TITLE),
        "title_ru": _text(MAX_TITLE),
        "description": _text(MAX_DESCRIPTION),
        "summary": _text(MAX_SUMMARY),
        "tags": {
            "type": "array",
            "items": {
                "type": "object",
                "properties": {"en": _text(MAX_ITEM), "ru": _text(MAX_ITEM)},
                "required": ["en", "ru"],
                "additionalProperties": False,
            },
            "maxItems": MAX_TAGS,
        },
        "objects": _list(MAX_LIST),
        "scenes": _list(MAX_LIST),
        "actions": _list(MAX_LIST),
        "emotions": _list(MAX_LIST),
    },
    "required": ["title", "title_ru", "description", "summary", "tags", "objects", "scenes", "actions", "emotions"],
    "additionalProperties": False,
}

FIELDS = ["title", "title_ru", "description", "summary", "tags", "objects", "scenes", "actions", "emotions"]


def validate(raw):
    """Returns (parsed, error). Parsed is None when the answer can't be used, like ImageDescriptionContract.Parse."""
    import json

    if not raw or not raw.strip():
        return None, "empty"
    start, end = raw.find("{"), raw.rfind("}")
    if start < 0 or end <= start:
        return None, "no JSON object"
    try:
        answer = json.loads(raw[start:end + 1])
    except json.JSONDecodeError as error:
        return None, f"invalid JSON: {error.msg}"
    if not isinstance(answer, dict):
        return None, "not an object"
    missing = [field for field in FIELDS if field not in answer]
    empty = [field for field in ("title", "title_ru", "description", "summary") if not str(answer.get(field) or "").strip()]
    if empty:
        return None, f"empty: {', '.join(empty)}"
    tags = answer.get("tags") or []
    if not isinstance(tags, list) or any(not isinstance(tag, dict) or not tag.get("en") or not tag.get("ru") for tag in tags):
        return None, "tags are not en/ru pairs"
    if missing:
        return answer, f"missing: {', '.join(missing)}"
    return answer, None
