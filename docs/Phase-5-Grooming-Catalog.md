# Phase 5 grooming service catalog

The first Phase 5 increment adds service setup in the Local app. After deploying the Local migration, sign in as an Admin or Manager and open **Grooming Services** from the employee dashboard.

1. Select **Add service** and enter the shop's service name. Add a description if helpful.
2. Enter the duration in minutes once the shop confirms it. A service can be saved inactive while its duration is pending.
3. Fill in species, breed, or size to describe which pets the service applies to. A blank field allows any value for that field. A breed restriction requires a species.
4. Make the service active when ready. Use **Edit** to update or deactivate an entry. If another employee saved changes while your form was open, reload the entry before making corrections.

The catalog starts empty. The specification lists Bath & Blow Dry, Haircut & Styling, Deshedding, Nail Trim, Ear Cleaning, Tick & Flea Removal, and Special Care as advertised services. Enter the shop's approved service names and durations. Package composition, pricing rules, versioned quotations, and consumable recipes are the remaining Phase 5 increments. Booking and check-in will use the catalog in their respective phases.

## Local database

Migration `20261126141728_AddGroomingCatalog` creates `GroomingServices` on top of `20261126134144_AddLocalCustomerPetRecords`. The upgrade adds a table and does not revise existing customer or inventory records. Review the migration before applying it to the intended Local database. The implementation run left the shop database at its previously applied migration.
