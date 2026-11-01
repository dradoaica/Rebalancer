-- Rebalancer tables (SQL Server backend). Run in the Rebalancer database after RebalanserSchema.sql.
-- Identifiers are not bracketed: none needs quoting, and formatters that do not know T-SQL split bracketed names.

CREATE TABLE RBR.Clients
(
    ClientId          UNIQUEIDENTIFIER NOT NULL,
    ResourceGroup     VARCHAR(100)     NOT NULL,
    LastKeepAlive     DATETIME         NOT NULL,
    ClientStatus      TINYINT          NOT NULL,
    CoordinatorStatus TINYINT          NOT NULL,
    Resources         VARCHAR(MAX
) NOT NULL,
    FencingToken      INT              NOT NULL,
    CONSTRAINT ConsumersPK PRIMARY KEY CLUSTERED (ClientId ASC)
);

CREATE TABLE RBR.ResourceGroups
(
    ResourceGroup          VARCHAR(100) NOT NULL,
    CoordinatorId          UNIQUEIDENTIFIER NULL,
    LastCoordinatorRenewal DATETIME NULL,
    CoordinatorServer      NCHAR(500) NULL,
    LockedByClient         UNIQUEIDENTIFIER NULL,
    FencingToken           INT          NOT NULL,
    LeaseExpirySeconds     INT          NOT NULL,
    HeartbeatSeconds       INT          NOT NULL,
    CONSTRAINT ResourceGroupsPK PRIMARY KEY CLUSTERED (ResourceGroup ASC)
);

CREATE TABLE RBR.Resources
(
    ResourceGroup VARCHAR(100)  NOT NULL,
    ResourceName  VARCHAR(1000) NOT NULL,
    CONSTRAINT QueuesPK PRIMARY KEY CLUSTERED (ResourceGroup ASC, ResourceName ASC)
);

ALTER TABLE RBR.Clients
    ADD DEFAULT (1) FOR FencingToken;
-- No foreign key from RBR.Clients to RBR.ResourceGroups (ADR-0032): a client of a missing group must reach its lease
-- request, which reports the group missing, so it reports Aborted (US-0021) instead of failing to register.
ALTER TABLE RBR.Resources WITH CHECK ADD CONSTRAINT FK_Resources_ResourceGroups FOREIGN KEY (ResourceGroup)
    REFERENCES RBR.ResourceGroups (ResourceGroup);
ALTER TABLE RBR.Resources CHECK CONSTRAINT FK_Resources_ResourceGroups;
