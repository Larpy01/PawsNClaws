# Phase 5 grooming service catalog

The first Phase 5 increment adds service setup in the Local app. After deploying the Local migration, sign in as an Admin or Manager and open **Grooming Services** from the employee dashboard.

1. Select **Add service** and enter the shop's service name. Add a description if helpful.
2. Enter the duration in minutes once the shop confirms it. A service can be saved inactive while its duration is pending.
3. Choose **All**, **Cat**, or **Dog** under Species. Breed choices follow the selected species. Choose **All** under Breed or Size to leave that field unrestricted.
4. Make the service active when ready. Use **Edit** to update or deactivate an entry. If another employee saved changes while your form was open, reload the entry before making corrections.

The catalog starts empty. The specification lists Bath & Blow Dry, Haircut & Styling, Deshedding, Nail Trim, Ear Cleaning, Tick & Flea Removal, and Special Care as advertised services. Enter the shop's approved service names and durations. Open **Packages, prices, price estimates and supplies** from this page to complete grooming setup. [Service packages and CRM workflow](Phase-5-Service-Packages-CRM.md)

## Local database

Migration `20261126141728_AddGroomingCatalog` creates `GroomingServices` on top of `20261126134144_AddLocalCustomerPetRecords`. The upgrade adds a table and does not revise existing customer or inventory records. Review the migration before applying it to the intended Local database. The implementation run left the shop database at its previously applied migration.
