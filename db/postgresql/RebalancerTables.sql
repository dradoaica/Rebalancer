-- Rebalancer tables (PostgreSQL backend, ADR-0030). Run in the Rebalancer database after RebalancerSchema.sql.
-- Column for column the RBR tables of the SQL Server backend (db/sqlserver/RebalancerTables.sql); times are
-- timestamptz, read and written only by the database clock (clock_timestamp()).

CREATE TABLE rbr.resource_groups
(
    resource_group           varchar(100) NOT NULL PRIMARY KEY,
    coordinator_id           uuid         NULL,
    last_coordinator_renewal timestamptz  NULL,
    coordinator_server       varchar(500) NULL,
    locked_by_client         uuid         NULL,
    fencing_token            integer      NOT NULL DEFAULT 0,
    lease_expiry_seconds     integer      NOT NULL,
    heartbeat_seconds        integer      NOT NULL
);

CREATE TABLE rbr.clients
(
    client_id          uuid         NOT NULL PRIMARY KEY,
    resource_group     varchar(100) NOT NULL,
    last_keep_alive    timestamptz  NOT NULL,
    client_status      smallint     NOT NULL,
    coordinator_status smallint     NOT NULL,
    resources          text         NOT NULL DEFAULT '',
    fencing_token      integer      NOT NULL DEFAULT 1
);

-- No foreign key to rbr.resource_groups, unlike rbr.resources: a client of a missing group must reach its lease
-- query, which reports the group missing, so the client reports Aborted (US-0021) instead of failing to register; and
-- the key check would wait for any lease transaction holding the group row.
CREATE INDEX clients_resource_group ON rbr.clients (resource_group);

CREATE TABLE rbr.resources
(
    resource_group varchar(100)  NOT NULL REFERENCES rbr.resource_groups (resource_group),
    resource_name  varchar(1000) NOT NULL,
    PRIMARY KEY (resource_group, resource_name)
);
