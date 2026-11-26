# Phase 4 customer and pet records

## Staff walkthrough

The Local employee dashboard links to **Customers & Pets** at `/Customers`. Business Admin, Manager and Cashier have access. The Local role seeder grants the customer-record permission on startup. Inventory Staff and Groomer cannot open or submit the forms. The technical SuperAdmin uses a separate sign-in.

1. In a development database with the Phase 4 schema, search for the owner by name, contact number, email or pet name.
2. For a walk-in, select **Add customer**. Enter first and last name and any available contact information. Record the owner's consent status. A local profile can exist without an email address or a website account.
3. Open the owner profile, check the contact information, and select **Add pet**. Enter the pet's name and species. The remaining details, including vaccination status, weight, allergies and special instructions, are optional.
4. Add a second pet and search for the owner using that pet's name. Both pets appear under the same owner.
5. Edit a pet from two sessions. After saving in the first session, saving the older form returns a conflict and asks the employee to reopen the profile.

The module checks the owner ID when updating a pet. Each successful write records the employee, operation, record version, owner, optional pet and UTC timestamp in the same save. Local profile changes have stable identifiers for future synchronization. The audit entry identifies a change but does not retain snapshots of sensitive text. Website linking, visit histories and sync delivery belong to later workflows.

## Database and verification

The Local migration `20261126134144_AddLocalCustomerPetRecords` creates CustomerRecords, PetRecords and ProfileChanges. Applying the migration to the intended Local database is a separate deployment step. The implementation run generated the SQL and checked the EF snapshot. It did not apply the migration to a business database.

The release build completed with zero compiler warnings and errors. Eleven unit tests and 52 integration tests passed. Six integration cases cover Phase 4, including HTTP access for five employee roles, two pets for a walk-in, search, corrections, audit entries, stale edits and cross-owner rejection.

## Running the opt-in SQL Server tests

These three tests create and remove randomly named disposable databases on one fixed Windows LocalDB instance. They exercise product uniqueness, privileged employee restrictions and the inventory migration against SQL Server. They never load application connection strings. In this execution environment, creating the dedicated instance failed while accessing the LocalDB registry configuration. The three tests were skipped.

In a regular PowerShell terminal under the Windows account that will run the tests, provision the dedicated instance once:

```powershell
sqllocaldb create PawsNClawsPhase2RTests -s
```

On later runs, use `sqllocaldb start PawsNClawsPhase2RTests`. From the repository root:

```powershell
$env:PAWSNCLAWS_RUN_SQLSERVER_TESTS = "1"
dotnet build PawsNClaws.slnx -c Release
if ($LASTEXITCODE -eq 0) {
    dotnet PawsNClaws.IntegrationTests/bin/Release/net10.0/PawsNClaws.IntegrationTests.dll -class '*SqlServerRegressionTests'
}
```

Check the execution summary for three executed cases and their actual results.
