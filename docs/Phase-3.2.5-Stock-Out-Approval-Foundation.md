# Phase 3.2.5 Stock Out approval foundation

This phase gives the Local app a place to record Stock Out requests, decisions, and proof photos. The [Phase 3.2.4 requirements record](Phase-3.2.4-Inventory-Requirements-Alignment.md) tracks the business decisions across the inventory module.

## What you can check

| Area | Progress |
| --- | --- |
| Requests | `StockOutRequests` records the product, positive quantity, reason, requester, decision, operation ID, and concurrency version. SQL checks reject self-approval and rejection without a comment. |
| Evidence | `StockOutRequestEvidence` stores the private storage key, file metadata, and uploader. A movement can trace back through its request to the original evidence without duplicating file records. |
| Movements | The nullable request link allows direct and historical movements to retain their existing shape. A foreign key requires the same product and a filtered unique index limits the request to one linked Stock Out movement. |
| Migration | `20261126053025_AddStockOutApprovalFoundation` adds two tables and a nullable movement link with constraints and indexes. Existing quantities and movement evidence require no backfill. |
| Local UI | SuperAdmin sign-in remains at `/system-admin/login` and the existing technical dashboard at `/system-admin`. Further health, diagnostics, maintenance, backup and restore, and sync modules belong to the technical-console milestone. The Stock Out approval UI comes in a later inventory phase. |

## Verify the schema

The database-independent test generates the SQL Server migration script and checks the model snapshot, unique link, foreign key, and additive operations. A separate test upgrades a disposable SQL Server database from the previous inventory migration after seeding sample history. It checks that the legacy quantity and evidence survive and verifies the new constraints in SQL Server.

The migration SQL passed the database-independent test. Live verification remains pending because the dedicated LocalDB instance was inaccessible from this session. No migration was applied to the business database.

From the repository root, run the schema test without a SQL Server instance.

```powershell
dotnet test PawsNClaws.IntegrationTests/PawsNClaws.IntegrationTests.csproj --filter FullyQualifiedName~StockOutMigrationTests
```

For the SQL Server verification, use the dedicated `PawsNClawsPhase2RTests` LocalDB instance and allow the test to create its own temporary catalog.

```powershell
$env:PAWSNCLAWS_RUN_SQLSERVER_TESTS = '1'
dotnet test PawsNClaws.IntegrationTests/PawsNClaws.IntegrationTests.csproj --filter FullyQualifiedName~Stock_out_upgrade_preserves_history
Remove-Item Env:PAWSNCLAWS_RUN_SQLSERVER_TESTS
```

## Handoff

Phase 3.2.5 keeps integer quantities consistent with the current ledger. Fractional units and batch allocation remain tied to the Phase 3.2.4 decisions. The application service will still need to authorize each operation, require valid uploaded photos, verify current stock, and commit request approval, movement, and stock deduction in one transaction. The schema limits duplicate movements but cannot by itself establish that a movement was created at the time of approval.
