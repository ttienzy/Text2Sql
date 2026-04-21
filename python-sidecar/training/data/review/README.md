# Intent Dataset Review Workflow

This folder is the enterprise review lane for intent-classification data.

## Goals
- Keep the canonical training set stable.
- Require human review before risky-intent samples are promoted.
- Preserve traceability for who reviewed what, when, and why.

## Directory Conventions
- `candidates/`: new or updated samples waiting for review.
- `approved/`: optional archival copies of approved review batches.
- `rejected/`: optional archival copies of rejected review batches.
- `generated/`: machine-produced outputs from validation or promotion scripts.

## Record Schema
Each JSONL record in `candidates/` should follow this shape:

```json
{
  "query": "drop the Orders table",
  "intent": "DDL",
  "language": "en",
  "review_status": "pending_review",
  "source": "enterprise_seed",
  "batch_id": "2026q2-risky-intents-v1",
  "risk_class": "critical",
  "notes": "Destructive schema change"
}
```

Optional context-aware fields are also supported for multi-turn training samples:

```json
{
  "conversation_context": "user: list VIP customers\nassistant: found 15 VIP customers",
  "previous_intent": "SELECT",
  "context_turn": 2,
  "database_context": "customers, orders"
}
```

## Allowed Review Status
- `pending_review`
- `approved`
- `rejected`

## Recommended Review Process
1. Add new candidate samples to a batch file under `candidates/`.
2. Run `python training/validate_review_dataset.py`.
3. Review samples and update `review_status`.
4. Run `python training/promote_reviewed_samples.py`.
5. Inspect the generated merged dataset before deciding whether to replace the canonical labeled dataset.

## Promotion Rules
- Only records marked `approved` are eligible for promotion.
- Promotion is de-duplicated against the canonical dataset by normalized query plus optional context fields (`conversation_context`, `previous_intent`, `context_turn`, `database_context`) and intent.
- Approved records that reuse the same normalized query + context signature with a different intent are treated as conflicts and must be resolved before promotion.
- Promotion writes a generated output file instead of mutating the canonical dataset in place.

## Enterprise Guidance
- Prioritize review for `DDL`, `WRITE_DELETE`, `WRITE_UPDATE`, and `AMBIGUOUS`.
- Reviewers should prefer conservative labels for dangerous or unclear prompts.
- If a sample can plausibly map to more than one intent, capture the ambiguity explicitly rather than forcing a risky label.
