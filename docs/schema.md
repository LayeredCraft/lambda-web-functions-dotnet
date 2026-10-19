# DynamoDB schema

`ApplicationData` is a single DynamoDB table. This is the version-one schema for a small, matching-focused application. All records are user-scoped. High-volume browser records are bucketed by month instead of using one unbounded user partition.

Raw email content and raw provider payloads belong in S3. DynamoDB records store their S3 object key, hash, and parsed result.

## Key conventions

| Value | Meaning |
| --- | --- |
| `{userId}` | Internal user ID from the identity system |
| `{month}` | `YYYY-MM`, based on the relevant posted, ordered, or received timestamp |
| `{transactionId}` | Monarch transaction ID |
| `{orderId}` | Amazon order ID |
| `{sourceId}` | Email, Monarch, or Amazon connection ID |
| `{timestamp}` | UTC ISO-8601 timestamp, fixed-width and sortable |

Every item includes `recordType`, `createdAt`, and `updatedAt`. User-owned records also include `userId` as an attribute, even though it appears in the key.

## User and connection records

| PK | SK | Record type | Required fields | Purpose |
| --- | --- | --- | --- | --- |
| `USER#{userId}` | `PROFILE` | `UserProfile` | Identity-provider subject, display name, email, status | Identify the user and support account lifecycle actions. |
| `USER#{userId}` | `SETTINGS` | `UserSettings` | Time zone, currency, browser preferences, automation preferences | Store user-level behavior settings. |
| `USER#{userId}` | `MONARCH_CONNECTION#{sourceId}` | `MonarchConnection` | Provider account ID, secret reference, state, last sync timestamp, cursor, last error | Connect and synchronize Monarch data. |
| `USER#{userId}` | `EMAIL_SOURCE#{sourceId}` | `EmailSource` | Provider, address, secret reference, state, last sync timestamp, cursor, last error | Connect and synchronize order-confirmation email. |
| `USER#{userId}` | `AMAZON_ACCOUNT#{sourceId}` | `AmazonAccount` | Display name, linked email source IDs, state, last import timestamp | Group Amazon orders belonging to the same user account. |
| `USER#{userId}` | `DATA_MONTH#{month}` | `UserDataMonth` | Month, first/last record timestamps | Enumerate date buckets for export and account deletion. |

Secrets are references to a secrets manager entry. They are never stored in this table.

## Import and email records

| PK | SK | Record type | Required fields | Purpose |
| --- | --- | --- | --- | --- |
| `USER#{userId}#SOURCE#{sourceId}#IMPORTS#{month}` | `IMPORT#{timestamp}#{importId}` | `ImportRun` | Status, requested window, cursor, counts, error summary, started/completed timestamps | Resume, troubleshoot, and show recent imports. |
| `USER#{userId}#EMAILS#{month}` | `EMAIL#{receivedAt}#{sourceId}#{messageId}` | `EmailMessage` | Source ID, provider message ID, received timestamp, S3 key, body hash, parse state, parsed order ID | Deduplicate and track each source email. |
| `USER#{userId}#EMAILS#{month}` | `PARSE_ERROR#{receivedAt}#{sourceId}#{messageId}` | `EmailParseError` | Source ID, message ID, error code, error detail, retry state | Find emails that require a parser retry or support review. |

The provider message ID is used with the source ID as the email idempotency key. If the provider does not guarantee stable message IDs, use a content hash as a second guard.

## Amazon order records

| PK | SK | Record type | Required fields | Purpose |
| --- | --- | --- | --- | --- |
| `USER#{userId}#ORDER#{orderId}` | `DETAIL` | `AmazonOrder` | Source ID, order number, ordered timestamp, order status, subtotal, shipping, tax, discount, total, currency, S3 email key, parse version | Load the complete order and preserve its parsed financial data. |
| `USER#{userId}#ORDER#{orderId}` | `ITEM#{itemId}` | `AmazonOrderItem` | Title, SKU/ASIN when present, quantity, unit price, line total, suggested category, chosen category | Categorize each purchased item. |
| `USER#{userId}#ORDER#{orderId}` | `MATCH#{transactionId}` | `OrderTransactionMatch` | Monarch transaction ID, state, method, confidence, matched amount, timestamps | Link an order to one or more Monarch transactions when needed. |

## Monarch transaction and allocation records

| PK | SK | Record type | Required fields | Purpose |
| --- | --- | --- | --- | --- |
| `USER#{userId}#TXN#{transactionId}` | `DETAIL` | `MonarchTransaction` | Monarch account ID, date, amount, currency, merchant, pending/posted state, current category, provider revision/hash, last synced timestamp | Local normalized mirror of Monarch's transaction. |
| `USER#{userId}#TXN#{transactionId}` | `ALLOCATION#{allocationId}` | `CategoryAllocation` | Category/bucket ID, amount, source, state, order ID/item IDs when applicable, Monarch split ID when present | Represent one full-category transaction or one part of a split. |
| `USER#{userId}#TXN#{transactionId}` | `EVENT#{timestamp}#{eventId}` | `TransactionEvent` | Event type, actor, before/after summary, provider revision, error when applicable | Explain matching, categorization, and synchronization changes. |
| `USER#{userId}#TXN#{transactionId}` | `SYNC#{timestamp}#{operationId}` | `MonarchSyncAttempt` | Requested change, request idempotency key, result, provider response summary, error, timestamps | Retry safely and audit writes back to Monarch. |

`CategoryAllocation` is always used, including for the common one-category transaction. The allocation total must equal the Monarch transaction amount after the chosen treatment of tax, shipping, discounts, and credits.

## Transaction browser records

These rows duplicate the compact fields needed to draw a transaction list: transaction ID, posted date, amount, currency, merchant, account summary, category/allocation summary, Amazon order summary, match state, and synchronization state. They avoid live Monarch calls and extra reads per row.

For a posted transaction or Amazon order in October 2026, write the chronological browser record when applicable and a matching-queue record while matching work remains:

| PK | SK | Record type | Query supported |
| --- | --- | --- | --- |
| `USER#{userId}#TXNS#2026-10` | `TXN#{postedAt}#{transactionId}` | `TransactionBrowser` | All transactions in a month or a date range within it. |
| `USER#{userId}#MATCH#{matchState}#2026-10` | `TXN#{postedAt}#{transactionId}` | `TransactionMatchQueue` | Transactions that are unmatched, suggested, confirmed, or rejected. |
| `USER#{userId}#MATCH#{matchState}#2026-10` | `ORDER#{orderedAt}#{orderId}` | `OrderMatchQueue` | Amazon orders that are unmatched, suggested, confirmed, or rejected. |

The browser record includes a compact category/allocation summary, but version one does not support browsing by category, account, pending/posted state, or arbitrary filter combinations. Add an access record for one of those only after a real screen requires it.

The API cursor contains the active month, sort direction, selected filter, and an opaque continuation token from the data-access layer. For a date range spanning months, it continues through the current month before querying the prior month.

## Category mappings and future automation

| PK | SK | Record type | Required fields | Purpose |
| --- | --- | --- | --- | --- |
| `USER#{userId}` | `CATEGORY_MAPPING#{mappingId}` | `CategoryMapping` | Match criterion, suggested category/bucket, priority, enabled state | Suggest a category from item title, ASIN, merchant, or another rule input. |

Rules and alerts are deferred. The matching queue is the initial review workflow.

## Write rules

1. An imported email, order, or Monarch transaction must be idempotent. Use its stable provider ID and a conditional write or stored provider revision/hash.
2. Update related records atomically when the data-access layer supports a DynamoDB transaction and the changed item count permits it. Otherwise, record durable work, retry safely, and make each write idempotent.
3. For large imports, write durable import/work records first, process transactions independently, and retry failed work. Do not rely on one large transaction.
4. Delete or replace the stale matching-queue row when match state changes. Replace the chronological browser row when its display fields change.
5. Keep raw email and provider payloads outside DynamoDB. Apply a retention policy to the S3 objects and delete them as part of account deletion.
6. Do not add a local secondary index, global secondary index, or speculative access record in version one. Add a new access record only for a confirmed screen.

## Initial browser queries

| User action | DynamoDB query |
| --- | --- |
| Browse newest transactions | `PK = USER#{userId}#TXNS#{month}`, descending `SK` |
| Browse a date range | Query each month in the range with an `SK BETWEEN` condition; continue with the API cursor |
| Browse matched or unmatched orders and transactions | `PK = USER#{userId}#MATCH#{matchState}#{month}` |
| Open a transaction | `PK = USER#{userId}#TXN#{transactionId}` |
| Open an Amazon order | `PK = USER#{userId}#ORDER#{orderId}` |
