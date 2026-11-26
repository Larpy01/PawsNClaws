# Phase 3 product and inventory

The Local app workspace is available at `/Inventory`. Sign in as a business Admin or Manager to see the full workflow. InventoryStaff can receive stock and submit or cancel their own pending disposal requests. Groomers can record estimated and actual consumption.

## Verified status

The Release build completed with zero warnings and errors. The xUnit runs reported 48 passing tests and three skipped opt-in SQL Server tests. The MVC receiving test posted a receipt as InventoryStaff, verified the receipt in the workspace, and confirmed that the same account could not reduce stock through an adjustment.

The final migration was applied to `PawsNClaws_LocalDB` and verified in SSMS. The upgrade checked existing stock totals and movement and evidence row counts before committing. SSMS showed the migration history entry, the new inventory tables, and precision 18 with scale 3 for `InventoryStocks.QuantityOnHand`. The migration verification used the real schema and did not insert sample business transactions.

| Part | What to check |
| --- | --- |
| 3.1 Product catalog | Open **Products** for SKU, name, selling price and product status. From **Inventory**, open **Edit inventory settings** for category, barcode, purpose, base unit, cost, expiry, and online eligibility. |
| 3.2.1–5 Inventory and approval data | On-hand totals agree with batch balances. The schema keeps product, request, movement, and evidence relationships and links at most one disposal movement to a request. |
| 3.2.6 Stock operations | Receive a batch, split accepted and rejected quantities, adjust existing stock, or record grooming usage. Repeating the same operation ID avoids repeat posting. Consumption allocates earliest expiring usable stock first, then oldest received stock. |
| 3.2.7 Private evidence | Attach one to five valid PNG or JPEG photos when evidence is required. The Local app checks format, full image decoding, file size, and dimensions. The files live under the Windows Local app account's `PawsNClaws/InventoryEvidence` directory and download through an authorized endpoint. |
| 3.2.8 Permissions | InventoryStaff can submit requests but cannot approve, dispose directly, or adjust. Manager and business Admin can approve or execute directly if granted the inventory permissions. Technical SuperAdmin remains at `/system-admin` with separate sign-in. |
| 3.2.9–10 MVC workflows | The workspace shows on-hand, usable and quarantined quantities, batches, Stock Out requests, the stock ledger, and receiving history. Approval deducts stock once. Rejection or cancellation keeps the stock quarantined until management releases it. |
| 3.2.11 Verification | The automated suite covers inventory balances, photos, permissions, request retries, approval, grooming usage, MVC antiforgery, and SQL migration generation. |

## Working defaults

- Base units are `Each`, `mL`, and `g`. Retail quantities use whole items. Receiving and stock operations accept exact conversions from litres or kilograms. Quantities use up to three decimal places, and a product with inventory history keeps its base unit.
- Each receiving entry records one product and one lot. Use the same supplier reference to group entries from one delivery. Previously recorded on-hand stock becomes an opening batch without a fabricated lot, expiry, receipt date, or supplier.
- A disposal request applies to one batch. A pending request reserves that quantity from sale and grooming use while leaving on-hand stock unchanged. Pending legacy requests are assigned to opening batches only when their quantities reconcile.
- Estimates use a grooming job reference and never deduct stock. Posting actual usage against an estimate deducts once. A management correction reverses the original allocated quantities.
- POS sales and reversals have inventory service operations for the later POS integration. Customer checkout and online stock allocation belong to later phases.

## Check progress in the app

1. Run or restart **PawsNClaws.Local** from Visual Studio and open **Inventory** after signing in.
2. For hands-on verification, use a test database. For example, receive 10 units, accept 8 and reject 2 with a photo. Submit a separate disposal request for 3 of the accepted units. Confirm 8 on hand and 5 usable while the review is pending.
3. Sign in as a different Manager or business Admin and approve. Confirm 5 on hand, one disposal movement, and a link to the original request and photos.

SQL Server migration `20261126055603_CompleteInventoryWorkflow` widens whole-number quantities to decimal and creates opening batches. For a deployment rollback, restore a verified backup. The migration deliberately rejects an automatic downgrade that could lose inventory history or fractional quantities.
