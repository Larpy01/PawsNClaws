# Technical authentication

Active SuperAdmins sign in at `/system-admin/login` over HTTPS with their account email and password. Previously enrolled accounts use the same form.

## Reset a forgotten password

After verifying the account owner's identity, run the following command from a private terminal on the authorized shop host. Select the environment for the intended Local database and enter the new password twice at the hidden prompts.

```powershell
dotnet run --project PawsNClaws.Local --no-launch-profile -- --reset-technical-password "developer@example.test"
```

A password reset revokes existing technical sessions. The separate HTTPS-only technical cookie expires after 30 minutes. Failed passwords count toward Identity lockout. Each technical request rechecks the account security stamp, active status and role.

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
