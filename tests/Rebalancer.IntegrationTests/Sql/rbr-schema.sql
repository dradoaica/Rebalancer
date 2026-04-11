CREATE SCHEMA RBR;
GO

CREATE TABLE RBR.ResourceGroups
(
    ResourceGroup          VARCHAR(100) NOT NULL PRIMARY KEY,
    CoordinatorId          UNIQUEIDENTIFIER NULL,
    LastCoordinatorRenewal DATETIME2 NULL,
    CoordinatorServer      NVARCHAR(500)   NULL,
    LockedByClient         UNIQUEIDENTIFIER NULL,
    FencingToken           INT          NOT NULL CONSTRAINT DF_RG_Fencing DEFAULT (0),
    LeaseExpirySeconds     INT          NOT NULL CONSTRAINT DF_RG_Lease DEFAULT (300),
    HeartbeatSeconds       INT          NOT NULL CONSTRAINT DF_RG_Heartbeat DEFAULT (25)
);
GO

CREATE TABLE RBR.Clients
(
    ClientId          UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    ResourceGroup     VARCHAR(100)     NOT NULL,
    LastKeepAlive     DATETIME2        NOT NULL,
    ClientStatus      TINYINT          NOT NULL,
    CoordinatorStatus TINYINT          NOT NULL,
    Resources         NVARCHAR(MAX)    NOT NULL CONSTRAINT DF_Clients_Res DEFAULT (''),
    FencingToken      INT              NOT NULL CONSTRAINT DF_Clients_Fenc DEFAULT (1)
);
GO

CREATE TABLE RBR.Resources
(
    ResourceGroup VARCHAR(100) NOT NULL,
    ResourceName  VARCHAR(255) NOT NULL,
    CONSTRAINT PK_Resources PRIMARY KEY (ResourceGroup, ResourceName)
);
GO
