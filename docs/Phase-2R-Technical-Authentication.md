# Phase 2R technical authentication

PawsNClaws Local separates business sign-in at `/Account/Login` from developer sign-in at `/system-admin/login`. Only an active named SuperAdmin with an enrolled authenticator can sign in to the technical dashboard. Business users keep their existing Identity sign-in. A SuperAdmin needs a separate business identity for operational access.

## Enroll or rotate an authenticator

Run the Local app command from a private interactive terminal on the authorized shop host. Confirm the app environment and target Local database before running it. For a development database configured through .NET user secrets, set `ASPNETCORE_ENVIRONMENT=Development` for that terminal first. Use a named account that already has the SuperAdmin role.

```powershell
dotnet run --project PawsNClaws.Local --no-launch-profile -- --enroll-technical-mfa "developer@example.test"
```

1. Enter the displayed setup key in an authenticator app as a time-based account.
2. Store the displayed single-use recovery codes offline in an approved vault. Keep the terminal output private and clear its scrollback after enrollment.
3. Open `/system-admin/login` over HTTPS. Enter the account email, password, and current six-digit code.

Re-enrollment replaces the authenticator key and recovery codes and revokes all existing technical sessions. The CLI logs the operation and account ID. Keys, passwords, and recovery codes are intentionally omitted from application logs. Enable the app's normal secure Data Protection key storage before deployment so HTTPS cookie protection remains available across restarts.

## Recover access

- **Lost authenticator:** Open `/system-admin/login`, choose **Use a recovery code**, and supply the password and one stored code. The code is consumed and earlier technical sessions are revoked. Re-enroll promptly to replace the missing authenticator and rotate any remaining codes.
- **Forgotten password:** After verifying the named developer's identity through the agreed support process, an authorized operator can reset the password from a private terminal. The password is entered twice with input hidden, and the account's prior sessions are revoked.

```powershell
dotnet run --project PawsNClaws.Local --no-launch-profile -- --reset-technical-password "developer@example.test"
```

- **Lost authenticator and recovery codes:** After the same identity verification, run the enrollment command again. If the password was also lost, run the password reset command first. Record the request and operator in the shop's support record.

These commands require access to the Local host and its configured SQL Server database. They are operator tools for technical accounts. Keep shell and database access restricted. Neither command exposes a public recovery endpoint or provisions another technical user.

## Session behavior and verification

- The technical account uses its own HTTPS-only, HttpOnly, strict SameSite cookie with a 30-minute absolute lifetime. The business cookie does not authenticate the technical dashboard.
- The app rechecks the Identity security stamp, enrollment, active status, role, and permission claims on technical requests. Password reset, MFA rotation, and successful recovery invalidate earlier technical sessions.
- The login and logout posts require antiforgery validation. Identity lockout applies to failed password and second-factor attempts. Accepted time-based codes are checked against recent use, and recovery codes are stored and redeemed through Identity.
- Automated tests use generated accounts and an isolated in-memory database. Run the solution build and the xUnit suite from the repository root.

```powershell
dotnet build PawsNClaws.slnx
dotnet test PawsNClaws.slnx
```

For a pre-release smoke test, use a dedicated staging SQL Server database and test account. Sign in from two browser sessions using different time-based codes, sign in with a recovery code, and confirm both earlier sessions are challenged. Verify that the code cannot be reused, that a normal business session cannot access the console, and that rotating MFA invalidates the recovery session. Exercise the operator password reset and verify that the old password fails. Never point automated tests at the shop's business database.

## Public website design

Use the [Paws and Claws Website Figma Make file](https://www.figma.com/make/GpNK6m42ptaXeWaAiTWKOt/Paws-and-Claws-Website?p=f&t=M1W3akmfRjmFVvHJ-0) as the design reference for `PawsNClaws.Web`. Figma exposed the prototype source, including the home, booking, shop, and account pages, the shared header and footer, styles, and assets. The prototype uses Next.js and Payload. In the website phase, translate its public layouts and interactions into the existing ASP.NET Core MVC architecture and use the approved PawsNClaws Identity, booking, and ordering services.

## Next milestone

Return to Phase 3.2.4 requirements alignment with the team. Review these decisions before implementing the Phase 3.2.5 schema.

The current inventory model uses whole-number quantities and stores evidence on inventory movements. It has no batch or Stock Out request entity. Confirm the requirements below before changing the schema or defining migrations for existing rows.

| Decision | What to confirm |
| --- | --- |
| Product units | Which items are sold by count, which consumables use fractional quantities, and which units and conversions the shop uses. |
| Batch receiving | How accepted and rejected quantities are recorded, which items require batch and expiry information, and when accepted stock becomes available. |
| Quarantine | When suspect stock is blocked from sale, who can release it, and what happens when a disposal request is rejected. |
| Disposal | Which roles can submit, approve, or execute directly. Confirm reason category, written explanation, photo evidence, and the single stock deduction after an authorized execution. |
