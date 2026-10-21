# 7. Administration (Roles & Access)

Role information and procedure permissions are available in the **Admin** area. Requires the MSAP **Admin** or **SuperAdmin** role.

## User Access Permissions

User Access records control which MSAP procedures each user can perform (e.g., CreateJobOrder, BillDispatchTickets, ViewMaritimeReport).

Managed via: **MSAP > User Access** (Admin role required)

## Role Management

![Role Management list](/msap/docs-images/admin/role-management.png)

**Path:** GENERAL > Role

- **Table columns:** Name
- The list is fixed: **Admin**, **User**, **SuperAdmin**. Additional roles cannot be created.
- An explicit MSAP role takes precedence over the Filpride role.
- Without an explicit MSAP role, Filpride Admin defaults to MSAP Admin; other active IBSWeb accounts default to User.
- SuperAdmin always requires an explicit MSAP assignment.
- Admin and SuperAdmin have all procedure permissions. User access is assigned per procedure.
- SuperAdmin can access both MSAP administration areas.

## Shared Accounts

Create and edit users, activate or deactivate accounts, and reset passwords through IBSWeb/Filpride user management. MSAP uses the same login and does not provide a separate user-management module.

## User Access (MSAP Permissions)

**Path:** MSAP > User Access (Admin role)

- Manages fine-grained access to MSAP procedures per user
- **Table columns:** User, Permissions
- Create and Edit assign which procedures a user can access

## Tips

- **Roles** are coarse (Admin/User/SuperAdmin); **User Access** handles per-procedure permissions
- Account details, passwords and activation are shared with IBSWeb.
- Deactivating a user prevents login and MSAP access but preserves their audit trail
- Password reset does NOT require the old password
