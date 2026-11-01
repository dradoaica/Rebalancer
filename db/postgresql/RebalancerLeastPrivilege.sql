-- Least-privilege role for Rebalancer nodes (PostgreSQL backend; TM-0004). Grants exactly what Rebalancer.PostgreSql
-- executes: it reads resources, locks and updates the lease rows in rbr.resource_groups, and registers, updates and
-- deletes client rows in rbr.clients. Creating groups and resources (INSERT into rbr.resource_groups /
-- rbr.resources) stays with an operator role.
--
-- Run in the Rebalancer database after RebalancerSchema.sql and RebalancerTables.sql, then make each node's login
-- a member, e.g.:
--   CREATE ROLE rebalancer_orders LOGIN PASSWORD '...' IN ROLE rebalancer_node;
-- Use logins only Rebalancer nodes use, and give no other role write access to the rbr schema.
--
-- Timeouts (TM-0004 D2) need no role setting: the backend sets lock_timeout and idle_in_transaction_session_timeout
-- in every lease transaction, to half the group's lease expiry. (ALTER ROLE ... SET on this group role would not
-- reach its members: role settings are not inherited.)

CREATE ROLE rebalancer_node NOLOGIN;

GRANT
USAGE
ON
SCHEMA
rbr TO rebalancer_node;
GRANT SELECT, INSERT, UPDATE, DELETE ON rbr.clients TO rebalancer_node;
-- UPDATE also covers SELECT ... FOR UPDATE on the lease row
GRANT SELECT, UPDATE ON rbr.resource_groups TO rebalancer_node;
GRANT SELECT ON rbr.resources TO rebalancer_node;

-- TM-0004 T3: nodes need to create nothing. Every statement names rbr, and built-ins resolve from pg_catalog first.
-- Before PostgreSQL 15 every role may create objects in public; to close that for all roles, an operator runs
--   REVOKE CREATE ON SCHEMA public FROM PUBLIC;
REVOKE CREATE ON SCHEMA public FROM rebalancer_node;
