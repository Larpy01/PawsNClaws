# Phase 5 service packages and CRM workflow

## Philippine industry references

| Reference | Observed workflow | Design implication for Paws N’ Claws |
| --- | --- | --- |
| [Pet Express Philippines](https://www.petexpress.com.ph/pages/pet-grooming) | Online booking lets owners select a size bracket, a package, and optional services. | Keep the included services visible and record optional add-ons separately in the estimate. |
| [Bam Paws Veterinary Clinic, Pateros](https://bampaws.setmore.com/) | Its Setmore booking page lists grooming by species, weight bracket, and coat type, with duration and peso prices. | Use pet details when selecting a package and keep shop-approved pricing and duration configurable. |
| [Pet Matters](https://petmatters.ph/register) | The Philippine platform offers pet profiles and appointments for owners and workspaces for care businesses. | Make the pet profile the starting point for viewing appointments and previous package visits. |
| [Pettr Mobile Grooming](https://shop.pettr.app/products/pettr-mobile-grooming-bundle) | Its Metro Manila Full Groom Trio sells three sessions in a single purchase. | Treat prepaid visits as a separate feature with redemption and balance tracking if the shop adopts them. |

## Reference repository

The separate Larpy01 PAWS-CLAWS repository records customer retention notes, a next follow-up date, and the last contact time and staff member. It calculates follow-up status and has a separate customer service flow for inquiries, complaints, and feedback. In this project, link a follow-up to a specific pet and completed appointment so staff can see which package prompted the contact. [Retention DTO](https://github.com/Larpy01/PAWS-CLAWS/blob/main/PAWS.api/Dtos/CustomerRetentionDtos.cs) · [Customer service DTO](https://github.com/Larpy01/PAWS-CLAWS/blob/main/PAWS.api/Dtos/CustomerServiceDtos.cs)

## Example from the shop floor

A customer calls to book grooming for Mochi. Staff open the customer record, select Mochi, and choose **Basic Grooming**. The package includes Bath & Blow Dry, Nail Trim, and Ear Cleaning. Staff review eligibility, confirm the visit, and record any optional add-on on the estimate. The groomer sees the included services on the visit checklist. After completion, staff can see the visit from Mochi’s profile and schedule a rebooking follow-up if the customer wants one. When the customer returns, staff can review what Mochi received and estimate the current price. The prior visit retains its original package contents and quoted amount.

## Implementation order

| Increment | Behavior |
| --- | --- |
| Service Packages | Admins and Managers assemble services, see their eligibility and status, and activate or deactivate the package. An active package is available for new selections only while its required components are active and eligible for the selected pet. |
| Pricing and estimates | Select a package for a specific pet and use shop-approved peso pricing. Snapshot package name, included services, separate add-ons, and quoted amounts. |
| Booking and check-in | Link the estimate and booking to the customer and pet. Show included services on the visit checklist and add completed visits to pet history. |
| Follow-up | Record a due date, responsible employee, contact outcome, and last contact. Use the customer's communication preferences before sending reminders. |

For acceptance, create a package and estimate it for an eligible pet. Change or deactivate a component and verify that new selections reflect the change while the earlier estimate and visit still display the original package. Confirm that a follow-up for one pet is visible on that pet’s visit history even when its owner has multiple pets.

## Phase 5 acceptance walkthrough

1. After the Local schema is deployed, sign in as an Admin or Manager. Open **Grooming Services → Packages, prices, price estimates and supplies**.
2. Add an active package with existing active services. Add a price rule for the package using the shop's approved rate. Leave criteria blank for a default rule. A more specific match wins by the number of filled criteria. If two matching rules tie, resolve the overlap before saving a estimate.
3. Add a recipe for an included service and an active inventory consumable. Enter the estimate in the displayed stock unit. The system combines estimates for the same product across the estimate.
4. Open **Grooming price estimates** on a pet card. Choose a package or service and optional add-ons. Enter the assessed coat condition when it affects pricing. An Admin or Manager can enter a custom estimate total and a reason for changing it.
5. Save the estimate, then change a price and create a revision. Check that the earlier version still shows the original amounts, package contents, and supplies. Deactivate a package or one of its services and check that it is unavailable for new estimates.

The additive Local migration `20261127002828_CompleteGroomingPlanning` follows `20261126141728_AddGroomingCatalog`. Deploy both in order if the first is still pending. The migration adds the planning tables without rewriting pet or stock records.

Quotes store estimates for later job handling. A estimate does not post inventory movements. Booking, groomer checklists, completed-visit history, and customer follow-ups remain part of later workflows.

## Quote user-test checklist

Use a test database with one dog, one cat, a service with a base price, a coat-based price, and two optional services. Sign in as a Manager or Admin.

| Try this | Expected result |
| --- | --- |
| Open **Create price estimate** from Grooming Planning and from a pet’s profile. | The workspace form starts blank. Opening it from a pet profile selects that pet. |
| Save with no customer and pet or no service or package. | The missing field gets a clear message and the estimate stays unsaved. |
| Check two add-ons, uncheck one, and save. Repeat with both unchecked. | Only checked services appear in the estimate. With none checked, the estimate shows only the main selection. |
| Leave coat condition and custom total blank, then save. | The estimate uses the base price and the reason field stays disabled. |
| Enter a coat condition that has a price rule. | The matching price is used. |
| Enter a custom total, including zero. Try saving without a reason, then enter one. | The reason is required. Saving with a reason uses the custom amount. |
| Clear the custom total after typing a reason, then save. | The reason is disabled and the estimate uses the calculated total. |
| Open estimate history, update the price rule, and save a new estimate version. | The earlier version keeps its original price and included services. |
| Open an update in two tabs and save from both. | The second save asks you to reopen the estimate. |
| Click **Cancel** before saving. | The app returns to the grooming workspace and creates no estimate. |
| Sign in as a Cashier and try to open the estimate workspace. | Access is denied. |

From the repository root, build and run the estimate tests:

```powershell
dotnet build PawsNClaws.slnx -c Release
if ($LASTEXITCODE -eq 0) {
    dotnet .\PawsNClaws.IntegrationTests\bin\Release\net10.0\PawsNClaws.IntegrationTests.dll -class '*GroomingQuoteUserTests' -class '*GroomingPlanningTests'
}
```
