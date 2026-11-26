# PawsNClaws PowerShell command guide

Run each block in PowerShell. For a normal code change, use steps 1, 3 and 4. Step 2 is only for preparing the SQL Server test instance. Commands that use `--no-build` or a test DLL run the last compiled code, so rebuild after editing source files.

## 1. Open the repository and check your environment

```powershell
Set-Location 'C:\Users\Machcreator\source\repos\PawsNClaws'
dotnet --version
git branch --show-current
git status --short --branch
```

Use these at the start of a session. They show the selected SDK, current branch, and local changes. The branch checked when this guide was written was `testing_branch_11-25-26`.

## 2. Prepare the isolated SQL Server instance

```powershell
sqllocaldb info
```

Look for the exact name `PawsNClawsPhase2RTests`. If it is missing, create and start it once under the Windows account that will run the tests:

```powershell
sqllocaldb create PawsNClawsPhase2RTests -s
```

On later runs, start the existing instance and check its state:

```powershell
sqllocaldb start PawsNClawsPhase2RTests
sqllocaldb info PawsNClawsPhase2RTests
```

The SQL suite uses this fixed instance and creates a disposable database per test. If creation reports a registry error, retry in a regular PowerShell terminal under your Windows account. Keep the shop database out of automated tests.

## 3. Build the current source

```powershell
dotnet build PawsNClaws.slnx -c Release
```

Use this after code changes or switching branches. It restores dependencies as needed and compiles the solution, including both test runners. Continue after **Build succeeded**. If it fails, fix the reported errors and rebuild before running a test DLL.

## 4. Run the tests

**Daily regression run.** Run both projects and get separate pass, fail and skip counts. The first line clears the SQL opt-in for this terminal, so the dedicated SQL tests will be reported as skipped.

```powershell
Remove-Item Env:PAWSNCLAWS_RUN_SQLSERVER_TESTS -ErrorAction SilentlyContinue
dotnet test PawsNClaws.slnx -c Release --no-build --logger "console;verbosity=normal"
```

**SQL Server checks only.** After step 2, use this for changes to migrations, database constraints or privileged employee operations. The flag lasts for the current PowerShell session. The class filter selects the three SQL Server regression tests.

```powershell
$env:PAWSNCLAWS_RUN_SQLSERVER_TESTS = '1'
dotnet build PawsNClaws.slnx -c Release
if ($LASTEXITCODE -eq 0) {
    dotnet .\PawsNClaws.IntegrationTests\bin\Release\net10.0\PawsNClaws.IntegrationTests.dll -class '*SqlServerRegressionTests'
}
```

Check the xUnit summary for **Total: 3, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0** before recording that all three passed. To run the **whole suite including SQL**, leave the flag enabled and run:

```powershell
dotnet test PawsNClaws.slnx -c Release --no-build
```

**One module only.** Use this for a quick check while changing customer or pet records. Replace the class name with `InventoryWorkflowTests` or `TechnicalAuthenticationTests` to test those modules instead.

```powershell
dotnet .\PawsNClaws.IntegrationTests\bin\Release\net10.0\PawsNClaws.IntegrationTests.dll -class '*CustomerRecordTests'
```

## 5. Collect code coverage

```powershell
dotnet test PawsNClaws.slnx -c Release --no-build --collect:"XPlat Code Coverage" --results-directory .\TestResults
Get-ChildItem .\TestResults -Recurse -Filter coverage.cobertura.xml |
    Sort-Object LastWriteTime -Descending |
    Select-Object LastWriteTime, FullName
```

Use this before a phase report. Coverlet writes coverage XML for each test project. Use the timestamps to identify the latest run. Record the test pass and skip counts alongside coverage.

## 6. Open the Local app for manual checks

```powershell
dotnet run --project .\PawsNClaws.Local --launch-profile https
```

Use this to check the screens after automated tests. Open [Employee sign-in](https://localhost:7000/Account/Login) or [SuperAdmin sign-in](https://localhost:7000/system-admin/login). This starts the real Local app with its configured development database. Press **Ctrl+C** in that terminal to stop it.

## 7. Review and share changes with Git

| Command | When to use it and what it does |
|---|---|
| `git status --short --branch` | Before and after editing. Shows your branch, modified files, new files and staged changes. |
| `git diff` | Before staging. Reviews unstaged edits to tracked files. Open new files separately to review them. |
| `git diff --check` | Before staging. Flags whitespace errors in unstaged changes. |
| `git fetch origin` | When checking GitHub for updates. Refreshes the remote branch references. |
| `git branch -vv` | After fetching. Shows the upstream branch and ahead/behind counts. |
| `git log -5 --oneline --decorate` | When checking what was recently committed on your branch. |
| `git pull --ff-only` | When the working tree is clean and the current branch is behind its upstream. Updates it if a fast-forward is possible. |

**After approval to commit**, stage only the files for the change. This example stages the pet form and its regression tests. Adjust the file list for the work being committed.

```powershell
git add -- PawsNClaws.Local/Views/Customers/Pet.cshtml PawsNClaws.IntegrationTests/CustomerRecordTests.cs
git diff --cached --stat
git diff --cached
```

If you staged a file by mistake, `git restore --staged -- path/to/file` removes it from staging and keeps your edits. After reviewing the entire staged diff, commit it locally:

```powershell
git commit -m "Add vaccination status options to pet records"
```

**After approval to push**, confirm the branch and upstream, then publish the commit. Plain `git push` uses the configured upstream. For the first push of a new branch, use `git push -u origin HEAD`.

```powershell
git status --short --branch
git branch -vv
git push
```

## 8. Finish a SQL testing session

```powershell
Remove-Item Env:PAWSNCLAWS_RUN_SQLSERVER_TESTS -ErrorAction SilentlyContinue
sqllocaldb stop PawsNClawsPhase2RTests
```

Use these after the tests finish. The first command disables SQL tests for future runs in this terminal. The second stops the dedicated instance so you can start it again next time.
