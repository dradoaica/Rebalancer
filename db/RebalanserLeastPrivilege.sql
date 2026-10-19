-- Least-privilege role for Rebalancer nodes (SQL Server backend; TM-0002 follow-up).
-- Grants exactly what Rebalancer.SqlServer 3.x executes: it reads resources, renews leases in RBR.ResourceGroups and
-- registers, updates and deletes client records in RBR.Clients (deletes since 3.0, ADR-0014).
-- Creating groups and resources (INSERT into RBR.ResourceGroups / RBR.Resources) stays with an operator principal.
--
-- Run in the Rebalancer database after RebalanserSchema.sql and RebalanserTables.sql, then add each node's database
-- user to the role, e.g.:
--   CREATE USER [rebalancer_orders] FOR LOGIN [rebalancer_orders];
--   ALTER ROLE [rebalancer_node] ADD MEMBER [rebalancer_orders];
-- Use a login only Rebalancer nodes use, and give no other principal write access to the RBR schema.

CREATE ROLE [rebalancer_node];
GO
GRANT SELECT, INSERT, UPDATE, DELETE ON [RBR].[Clients] TO [rebalancer_node];
GRANT SELECT, UPDATE ON [RBR].[ResourceGroups] TO [rebalancer_node];
GRANT SELECT ON [RBR].[Resources] TO [rebalancer_node];
GO
