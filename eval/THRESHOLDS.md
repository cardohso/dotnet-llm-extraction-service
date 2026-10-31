# Evaluation thresholds and scoring rules

Written on 2026-10-31, **before the first run against a real provider**. They are not changed after
seeing results; a missed threshold is reported as a miss.

## Thresholds (`thresholds.json`)

| Metric | Threshold | Why |
|---|---|---|
| Valid output after repair | ≥ 95 % | With strict JSON schema output, invalid replies should be rare; at most 1 of 18 may fail. |
| Overall field accuracy | ≥ 85 % | Mean of date, location, equipment and severity scores across all cases. |
| Severity accuracy | ≥ 80 % | Severity is stated in 15 of 18 texts; misses mostly mean "unknown" was not respected. |
| p95 latency | ≤ 5,000 ms | Per extraction call including any repair, sequential, measured from the harness. |

## Golden set

`golden/incident-reports.jsonl`: 18 synthetic rail incident texts written for this repository.
Place names are real; the events are fictional. Each case has a `trap` note saying what it tests.
Coverage: all five severities (4/4/4/3/3), 5 null dates (2 relative, 1 without year, 2 explicitly unknown),
2 null locations, 1 case with no equipment, 11 with two or more items, 3 European Portuguese texts,
2 long texts (1,300–1,500 characters) with distracting dates and equipment.

## What counts as correct

Strings are normalised before comparison: trimmed, inner whitespace collapsed, case-insensitive.

- **date**: exact match. Null only matches null.
- **severity**: exact match.
- **location**: match if the normalised strings are equal or one contains the other on word boundaries
  ("York" matches "York station"; "Porto" would match "Porto Campanhã"). Null only matches null.
- **equipment**: set F1. A predicted item matches an expected item under the same equal-or-contains rule,
  one-to-one, exact matches first. Both empty scores 1.0.
- **summary**: not scored for content; it must pass schema validation.
- An invalid result (no valid JSON after repair) scores 0 on every field.

The contains rule is deliberately lenient about wording ("damaged balises" vs "balise") and strict
about substance (a missing, invented or wrong item still costs points). It can over-credit a vaguer
answer, such as "Lisboa" for "Lisboa Oriente"; this limitation is noted in the README.
