# MMSI Sales and Accounting Program (MSAP)

Maritime towage operations and commercial billing context for planning vessel calls, dispatching tugboats, recording completed assists, and invoicing customers.

## Language

**Vessel Schedule**:
A planned vessel call for a customer at a port and terminal within a scheduled time window, specifying its assigned tugboats and **Planned Tug Services**.
_Avoid_: Booking, plan, job

**Planned Tug Service**:
A specific maritime assist (such as docking, undocking, or watering) that an assigned tugboat is scheduled to perform on a **Vessel Schedule**.
_Avoid_: Service entry, expected row, tugboat booking

**Schedule Overlap**:
A conflict where two active **Vessel Schedules** share the same vessel, terminal, or assigned tugboat during overlapping planned time windows.
_Avoid_: Collision, double-booking

**Customer Order Slip**:
The authorized commercial and operational order for a **Vessel Schedule**, under which actual **Dispatch Tickets** and customer billings are recorded. Also referred to as **COS**.
_Avoid_: Booking, job, work order

**Dispatch Ticket**:
The operational record of an actual assist completed by a tugboat under a **Customer Order Slip**, capturing actual service times or quantities for billing.
_Avoid_: Service log, slip
