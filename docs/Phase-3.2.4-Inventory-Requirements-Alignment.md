# Phase 3.2.4 Inventory Requirements Alignment

**Status**  Ready for team and client review. Phase 3.2.5 implementation follows recorded sign-off.

**Repository baseline**  `testing_branch_11-25-26` at `11f3aef`, checked on November 26, 2026. The phase requirements come from the [PawsNClaws Project Specification v2](https://chatgpt.com/library?file_id=libfile_6364d36d1cec8191af1053bb48a4a0e3&account_id=personal), especially INV-01–09, T06–07, D05, and D11.

## Current implementation

| Area | Baseline on the pushed branch | Gap for the inventory workflow |
| --- | --- | --- |
| Catalog | `Product` and `ProductService` provide SKU, name, selling price, active status, and create/update validation. The local model has unique SKU and name indexes. | Add product purpose, unit, and expiry rules while preserving existing catalog data and uniqueness. |
| Stock | `InventoryStock` has one balance per product, integer quantities and reorder level, nonnegative checks, and a SQL Server row version. | Add batch-level quantities and availability. Define precision and legacy opening balances before changing quantity columns. |
| Movements | `InventoryMovement` tracks integer before/change/after quantities, actor, unique operation ID, and a disposal reason for Stock Out. The migration enforces movement direction and balances. | Add batch allocation and clear movement semantics for receiving, grooming use, corrections, and quarantine. |
| Evidence | `InventoryMovementEvidence` attaches metadata to completed movements and constrains type to JPEG/PNG and size to 5 MiB. | A pending request needs evidence before a movement exists. Private file storage, authorized access, and request-to-movement provenance still need implementation. |
| Authorization | The current seeder grants `InventoryAdjust` to Manager and InventoryStaff. Technical SuperAdmin is isolated from business permissions. | Design dedicated privileges for submitting, approving, executing directly, adjusting, and releasing quarantine. Reconcile obsolete role grants and preserve the technical boundary. |
| Layers | Application references Domain and already has product contracts. The inventory foundation is in Domain and Infastructure. | The branch has no inventory Application DTOs or service contracts, no inventory MVC workflow, and no tracked batch or Stock Out request entities. |

Baseline references in the repository include `PawsNClaws.Domain/Entities`, `PawsNClaws.Infastructure/Persistence/Local/LocalDbContext.cs`, the `20261125031706_AddInventoryFoundation` migration, and `PawsNClaws.Infastructure/Identity/Local/LocalRoleSeeder.cs`.

## Disposal rules carried forward

- Every disposal requires a reason category, written explanation, positive quantity, and valid photographic evidence.
- Manager and business Admin can execute disposal directly when granted the explicit permission. The specification allows an explicitly authorized SuperAdmin path only through separately approved support access. The standing technical session remains isolated from operations.
- Lower-level staff with submit permission create a request for management review. Submission leaves on-hand quantity unchanged. Approval requires a qualifying management role and permission, and the reviewer must be different from the requester.
- Recheck stock, batch eligibility, and evidence when approving. Commit the approval, recorded movement, and stock update together. Repeated approval or request retries must never deduct stock twice. Rejection preserves on-hand stock and records the reason.
- Keep rejected receiving goods, retail sales, grooming use, and disposal as distinguishable events. Restrict adjustments so they cannot bypass the disposal workflow.

## Proposed decisions for sign-off

| Decision | Proposed behavior | Confirmation needed |
| --- | --- | --- |
| Product purpose and units | Give each SKU a base unit and explicit sellable and consumable flags. Validate whole quantities for discrete SKUs. Propose `decimal(18,3)` for stock and movement quantities. Accept only configured unit conversions. Reject converted quantities that cannot be represented exactly at the approved scale. | Confirm the shop's base units and package conversions from actual items and grooming usage. Approve the precision and reject-or-round rule. |
| Batch receiving | Record delivered, accepted, and rejected quantities in the same base unit and require their totals to reconcile. Post only accepted quantities. Keep supplier reference, batch identifier, receiving date, and required expiry and cost information. | Confirm who can receive stock, which SKUs require a lot and expiry, and what supplier and rejection evidence is required. |
| Availability and quarantine | Allocate expiring stock by earliest expiry and other stock by oldest receipt. Block expired and quarantined quantities from sale and grooming use. Keep quarantined goods in physical on-hand stock until a disposal or release is recorded. Rejection or cancellation leaves existing quarantine in place until a separate authorized review. | Confirm who may quarantine or release goods, whether a pending damaged-stock request automatically quarantines its batch allocation, and when expiry takes effect in shop local time. |
| Approval and evidence | Require a separate request, a stable submission operation ID, attributable reviewer and outcome, and immutable private evidence. Proposed limits are 1–5 JPEG/PNG photos at 5 MiB each. Protect evidence reads by ownership or assigned permission. Require a reason for rejection. | Confirm role grants, cancellation rules, photo policy and retention, and whether scoped SuperAdmin support access is in the first release. |

Before finalizing units, capture a few representative retail and consumable SKUs with their stock counts, supplier packaging, batch labels, and actual grooming measures. This is the input for deciding whether the proposed scale fits the shop.

## Application and persistence handoff

| Capability | Proposed Application contract |
| --- | --- |
| Receive stock | Product, received unit and quantity, accepted and rejected amounts, supplier/lot details, expiry, evidence references, and operation ID. Return receipt ID, posted quantity, and resulting balance. |
| Query stock | On-hand, usable, quarantined, and expired quantities with batch details and reorder level. Apply authorization to inventory and evidence reads. |
| Submit and review disposal | Separate submit, approve, reject, cancel, and direct-execution commands. Include product/batch allocation, quantity, reason, evidence references, operation ID, and an expected concurrency version for review. Resolve the actor on the server. |
| Grooming usage and corrections | Record estimated usage independently of actual usage. Post actual consumption once per operation. Link a correction to the original movement and preserve the audit history. |

Keep Application contracts independent of MVC and EF types. Use an application storage interface for evidence and keep the bytes in private storage. An approval links the request and its evidence to one movement without duplicating the stored photos. Use a unique request-to-movement link and concurrency checks as database safeguards. Preserve retry safety across database and file operations by staging uploads, finalizing references after validation, and reconciling orphaned staging files.

For Phase 3.2.5, first inspect legacy stock and movements on an isolated copy. Preserve row IDs, numerical quantities, and historical movement evidence. Convert legacy quantities only after their base units are known. Represent opening batch balances without inventing suppliers, lot numbers, dates, costs, or receipts. If known lot or expiry data is required but missing, route the quantity for review before making it available. Review additive migration SQL and reconcile product totals against batch totals on the test copy before any deployment.

## Release checks for the resulting workflow

- Receiving 10 units with 2 rejected increases usable stock by exactly 8. A fractional quantity is accepted for a configured consumable and rejected for a discrete SKU.
- Among usable expiring batches, the batch with the earliest expiry is allocated first. Expired and quarantined quantities cannot be used. Releasing a valid quarantine changes availability without changing on-hand quantity.
- A staff submission preserves on-hand quantity. A reviewer cannot approve their own request. Rejection preserves stock and leaves quarantined goods blocked until reviewed.
- Two concurrent attempts to approve the same request or a retry of that approval create a single movement and a single deduction. An intervening sale that leaves insufficient stock prevents approval.
- Direct disposal requires the same evidence as submitted disposal. An InventoryStaff account cannot bypass the workflow through negative adjustment. Technical credentials alone cannot execute business actions.
- Legacy catalog rows and stock totals reconcile after migration on a test copy. Evidence remains readable only by authorized users and retains its original attribution.

## Sign-off record

| Decision | Approved choice or explicitly deferred scope | Approver and date |
| --- | --- | --- |
| Units, precision, and conversion | Pending | Pending |
| Receiving, batch, and expiry rules | Pending | Pending |
| Quarantine and release rules | Pending | Pending |
| Disposal roles, evidence, and retention | Pending | Pending |

Record any deferred item's owner and target milestone before proceeding to Phase 3.2.5.
