# Database design: Amazon transaction processing

## Purpose

This document identifies the actions the application must support before choosing DynamoDB or committing to a DynamoDB table design.

Working assumptions:

- A signed-in user can connect one or more Amazon accounts or sources.
- The application imports Amazon transactions from those sources.
- A transaction may have lifecycle updates, adjustments, refunds, and application-created annotations.
- Users browse, search, and act on their own data without seeing another user's data.

The repository is a Lambda hosting package, not the application, so these assumptions must be confirmed with the product team.

## Questions to settle

1. What exactly is an Amazon transaction: an order, charge, payout, shipment, refund, or a record representing several of these?
2. Which Amazon integration supplies it, and can a source resend or revise historical records?
3. Can a user belong to an organization, share access, or use multiple Amazon accounts?
4. Which user-entered data is required: categories, notes, tags, review state, rules, alerts, or documents?
5. Which searches are essential: date range, status, merchant, amount, category, external ID, text, or combinations?
6. What reporting is needed, and can it be eventually consistent or delayed?
7. How long must source payloads, import history, and deleted-user data be retained?

## Actions and data views

Confirm the expected volume, latency, pagination, and consistency requirement for every row.

| Area | Action | Data needed | Read/write pattern |
| --- | --- | --- | --- |
| Identity | Create or update profile | Identity, preferences, timestamps | Point read and conditional write by user ID |
| Identity | Load current user | Profile and settings | Point read by user ID |
| Connections | Add, verify, disable, or remove a source | Connection state, secret reference, cursor, last error | Point read/write; list sources for a user |
| Import | Begin or resume an import | Run, source, cursor/window, status, errors | Conditional create and status update; list recent runs |
| Import | Record an imported transaction | External ID, normalized fields, source payload reference, import time | Idempotent upsert by source plus external ID |
| Transactions | Browse transactions | Summary rows sorted by effective date | List a user's transactions, newest first, with pagination |
| Transactions | Review matching work | Summary rows with match state | List unmatched, suggested, confirmed, or rejected transactions |
| Transactions | Open a transaction | Full record, linked source data, annotations, history | Point read by transaction ID |
| Transactions | Find by Amazon ID | External order, charge, or payout ID | Exact lookup, scoped to user or source |
| Transactions | Apply a user action | Category, note, tag, review state, rule result | Conditional update and history entry |
| Transactions | Process a source change | Revision, refund, cancellation, settlement, correction | Idempotent update and history entry |
| Operations | Support an import | Runs, errors, transaction history | Privileged lookup by user, source, external ID, or import ID |
| Privacy | Export or delete a user | Every user-owned record and external data reference | Enumerate by user; delete or tombstone deterministically |

## Initial entity model

| Entity | Ownership | Important fields |
| --- | --- | --- |
| User profile | User | User ID, identity-provider subject, name, preferences, timestamps |
| Amazon source | User | Source ID, provider/account identifiers, connection state, secret reference, sync cursor, last sync/error |
| Import run | Source | Run ID, requested window, status, counts, errors, started/completed timestamps |
| Email message | User and source | Provider message ID, received timestamp, S3 key, body hash, parse state, parsed order ID |
| Amazon order | User and source | Internal ID, Amazon order number, ordered timestamp, totals, currency, source revision, normalized details |
| Amazon order item | Amazon order | Title, SKU/ASIN, quantity, line total, suggested/chosen category |
| Monarch transaction | User and source | Internal ID, external ID, effective date, status, currency/amount, merchant, source revision, normalized details |
| Category allocation | Monarch transaction | Category/bucket ID, allocated amount, source, synchronization state, linked order/item IDs |
| Order/transaction match | Amazon order and Monarch transaction | Match state, method, confidence, matched amount, timestamps |
| Transaction event | Transaction | Event ID, type, timestamps, change summary |

Do not put access tokens or other secrets in transaction records. Store a reference to a dedicated secrets system.

Rules, alerts, precomputed reporting aggregates, and browser filters beyond date and match state are deferred until a real workflow needs them.

## Decision: DynamoDB for version one

Version one uses DynamoDB. The application is small, user-scoped, and focused on matching Amazon orders to Monarch transactions; it is not a general-purpose search product.

The version-one browser supports only these interactive reads:

- A user's transactions in a date range, newest first.
- A user's matching queue: unmatched, suggested, confirmed, or rejected orders and transactions.
- A transaction's details, allocations, events, and linked Amazon order.
- An Amazon order's details, items, and linked Monarch transaction.

The application does not initially support free-text merchant search, arbitrary combinations of filters, amount-range search, ad hoc reporting, or table-wide aggregation. A new browser filter requires a concrete screen and a dedicated access record; it is not added speculatively.

## DynamoDB version-one design

`ApplicationData` is a single table with `PK` and `SK`. The small user partition holds profile and connection metadata. High-volume browser records are partitioned by user and month, so no user has one unbounded collection of transactions.

| Record | PK | SK | Purpose |
| --- | --- | --- | --- |
| User profile | `USER#{userId}` | `PROFILE` | Current profile and account status |
| User settings | `USER#{userId}` | `SETTINGS` | Preferences and automation settings |
| Connection/source | `USER#{userId}` | `{provider}_CONNECTION#{sourceId}` | Source state, cursor, and secret reference |
| Data month registry | `USER#{userId}` | `DATA_MONTH#{month}` | Enumerate monthly records for export and account deletion |
| Transaction browser row | `USER#{userId}#TXNS#{month}` | `TXN#{postedAt}#{transactionId}` | Browse transactions in date order |
| Matching-queue row | `USER#{userId}#MATCH#{matchState}#{month}` | `TXN#{postedAt}#{transactionId}` or `ORDER#{orderedAt}#{orderId}` | Review orders and transactions that need matching work |
| Transaction details | `USER#{userId}#TXN#{transactionId}` | `DETAIL` | Local Monarch mirror and synchronization metadata |
| Transaction allocation/event | `USER#{userId}#TXN#{transactionId}` | `ALLOCATION#{id}` or `EVENT#{timestamp}#{id}` | Category splits and audit history |
| Amazon order/details | `USER#{userId}#ORDER#{orderId}` | `DETAIL` or `ITEM#{id}` | Parsed order and its items |
| Order/transaction match | `USER#{userId}#ORDER#{orderId}` | `MATCH#{transactionId}` | Match state, method, confidence, and amount |

The browser row contains only the fields needed to render a list: amount, merchant, account summary, category/allocation summary, Amazon order summary, and match state. Detail records contain the full normalized data. This duplication is deliberate and limited.

No local secondary index or global secondary index is needed for version one. The matching queue uses an explicit access record because it is a core product workflow. Add a new access record only after a confirmed screen needs it.

### Import correctness

Imports must be safe to retry. Use a stable source ID plus the provider's stable external transaction, order, or email ID as an idempotency key. Compare a provider revision, update timestamp, or payload hash before changing an existing record, use a concurrency token where competing writers matter, and append a transaction event when source data changed.

Raw email and provider payloads stay in S3. DynamoDB holds the S3 key, content hash, parsed result, and synchronization state.

### Security and migration boundaries

Every request derives `userId` from the authenticated session or token. It never accepts a user ID as an untrusted browser parameter, and every DynamoDB key is built from that derived value.

Application code accesses DynamoDB through domain methods such as `ListTransactions`, `GetTransaction`, `SaveOrder`, and `SaveMatch`. It does not expose `PK` or `SK` outside the storage layer. Each record has a stable internal ID and schema version. These boundaries make a later export to a relational database a data migration rather than an application rewrite.

## Next step

Define the request and response contracts for the chronological transaction browser and matching queue, then implement one complete import-to-match flow. Use targeted partition-key queries and opaque continuation tokens. Start in on-demand capacity mode with a budget and throughput guardrail; optimize for provisioned capacity only after usage is known.
