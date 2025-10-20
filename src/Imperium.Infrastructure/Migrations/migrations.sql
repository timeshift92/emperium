CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);

BEGIN TRANSACTION;
CREATE TABLE "Characters" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Characters" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "Age" INTEGER NOT NULL,
    "Status" TEXT NOT NULL,
    "SkillsJson" TEXT NULL
);

CREATE TABLE "CrimeRecords" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_CrimeRecords" PRIMARY KEY,
    "PerpetratorId" TEXT NOT NULL,
    "CrimeType" TEXT NOT NULL,
    "Outcome" TEXT NOT NULL
);

CREATE TABLE "EconomySnapshots" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_EconomySnapshots" PRIMARY KEY,
    "Timestamp" TEXT NOT NULL,
    "PricesJson" TEXT NOT NULL,
    "Treasury" TEXT NOT NULL
);

CREATE TABLE "Families" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Families" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "MemberIds" TEXT NOT NULL,
    "Wealth" TEXT NOT NULL
);

CREATE TABLE "GameActions" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_GameActions" PRIMARY KEY,
    "ActorId" TEXT NOT NULL,
    "ActionType" TEXT NOT NULL,
    "DetailsJson" TEXT NOT NULL
);

CREATE TABLE "GameEvents" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_GameEvents" PRIMARY KEY,
    "Timestamp" TEXT NOT NULL,
    "Type" TEXT NOT NULL,
    "Location" TEXT NOT NULL,
    "PayloadJson" TEXT NOT NULL
);

CREATE TABLE "Locations" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Locations" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "Population" INTEGER NOT NULL
);

CREATE TABLE "WeatherSnapshots" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_WeatherSnapshots" PRIMARY KEY,
    "Timestamp" TEXT NOT NULL,
    "Condition" TEXT NOT NULL,
    "TemperatureC" INTEGER NOT NULL,
    "WindKph" INTEGER NOT NULL,
    "PrecipitationMm" REAL NOT NULL
);

CREATE TABLE "WorldTimes" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_WorldTimes" PRIMARY KEY,
    "Tick" INTEGER NOT NULL,
    "Hour" INTEGER NOT NULL,
    "Day" INTEGER NOT NULL,
    "Year" INTEGER NOT NULL,
    "IsDaytime" INTEGER NOT NULL,
    "LastUpdated" TEXT NOT NULL
);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20251019160555_InitialCreate', '9.0.10');

CREATE TABLE "SeasonStates" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_SeasonStates" PRIMARY KEY,
    "CurrentSeason" TEXT NOT NULL,
    "AverageTemperatureC" REAL NOT NULL,
    "AveragePrecipitationMm" REAL NOT NULL,
    "StartedAt" TEXT NOT NULL,
    "DurationTicks" INTEGER NOT NULL
);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20251019165343_AddSeasonState', '9.0.10');

ALTER TABLE "Characters" ADD "EssenceJson" TEXT NULL;

ALTER TABLE "Characters" ADD "History" TEXT NULL;

ALTER TABLE "Characters" ADD "LocationId" TEXT NULL;

ALTER TABLE "Characters" ADD "LocationName" TEXT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20251019191111_AddCharacterFields', '9.0.10');

CREATE TABLE "Relationships" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Relationships" PRIMARY KEY,
    "SourceId" TEXT NOT NULL,
    "TargetId" TEXT NOT NULL,
    "Type" TEXT NOT NULL,
    "Trust" INTEGER NOT NULL,
    "Love" INTEGER NOT NULL,
    "Hostility" INTEGER NOT NULL,
    "LastUpdated" TEXT NOT NULL
);

CREATE UNIQUE INDEX "IX_Relationships_SourceId_TargetId" ON "Relationships" ("SourceId", "TargetId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20251019205655_AddRelationships', '9.0.10');

CREATE TABLE "GenealogyRecords" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_GenealogyRecords" PRIMARY KEY,
    "CharacterId" TEXT NOT NULL,
    "FatherId" TEXT NULL,
    "MotherId" TEXT NULL,
    "SpouseIdsJson" TEXT NOT NULL,
    "ChildrenIdsJson" TEXT NOT NULL
);

CREATE TABLE "Households" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Households" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "LocationId" TEXT NULL,
    "HeadId" TEXT NULL,
    "MemberIdsJson" TEXT NOT NULL,
    "Wealth" TEXT NOT NULL
);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20251020035139_AddHouseholdAndGenealogy', '9.0.10');

CREATE TABLE "NpcMemories" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_NpcMemories" PRIMARY KEY,
    "CharacterId" TEXT NOT NULL,
    "KnownAssets" TEXT NOT NULL,
    "LostAssets" TEXT NOT NULL,
    "Greed" REAL NOT NULL,
    "Attachment" REAL NOT NULL,
    "LastUpdated" TEXT NOT NULL
);

CREATE TABLE "Ownerships" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Ownerships" PRIMARY KEY,
    "OwnerId" TEXT NOT NULL,
    "AssetId" TEXT NOT NULL,
    "OwnerType" TEXT NOT NULL,
    "AssetType" TEXT NOT NULL,
    "Confidence" REAL NOT NULL,
    "IsRecognized" INTEGER NOT NULL,
    "AcquiredAt" TEXT NOT NULL,
    "AcquisitionType" TEXT NOT NULL
);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20251020041739_AddOwnershipAndNpcMemory', '9.0.10');

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20251020082335_AddInheritanceRecords', '9.0.10');

ALTER TABLE "Characters" ADD "Gender" TEXT NULL;

CREATE TABLE "Inventories" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Inventories" PRIMARY KEY,
    "OwnerId" TEXT NOT NULL,
    "OwnerType" TEXT NOT NULL,
    "LocationId" TEXT NULL,
    "Item" TEXT NOT NULL,
    "Quantity" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL
);

CREATE TABLE "MarketOrders" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_MarketOrders" PRIMARY KEY,
    "OwnerId" TEXT NOT NULL,
    "OwnerType" TEXT NOT NULL,
    "LocationId" TEXT NULL,
    "Item" TEXT NOT NULL,
    "Side" TEXT NOT NULL,
    "Price" TEXT NOT NULL,
    "Quantity" TEXT NOT NULL,
    "Remaining" TEXT NOT NULL,
    "Status" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL
);

CREATE TABLE "Trades" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Trades" PRIMARY KEY,
    "Timestamp" TEXT NOT NULL,
    "LocationId" TEXT NULL,
    "Item" TEXT NOT NULL,
    "Price" TEXT NOT NULL,
    "Quantity" TEXT NOT NULL,
    "BuyOrderId" TEXT NOT NULL,
    "SellOrderId" TEXT NOT NULL,
    "BuyerId" TEXT NOT NULL,
    "SellerId" TEXT NOT NULL
);

CREATE INDEX "IX_MarketOrders_LocationId_Item_Side_Price" ON "MarketOrders" ("LocationId", "Item", "Side", "Price");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20251020085741_AddMarketModels', '9.0.10');

ALTER TABLE "Characters" ADD "Money" TEXT NOT NULL DEFAULT '0.0';

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20251020092008_MarketAndWallets', '9.0.10');

ALTER TABLE "MarketOrders" ADD "ReservedFunds" TEXT NOT NULL DEFAULT '0.0';

ALTER TABLE "MarketOrders" ADD "ReservedQty" TEXT NOT NULL DEFAULT '0.0';

ALTER TABLE "Locations" ADD "Treasury" TEXT NOT NULL DEFAULT '0.0';

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20251020092751_MarketWalletsAndTreasury', '9.0.10');

ALTER TABLE "MarketOrders" ADD "ExpiresAt" TEXT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20251020093815_ReservationsWalletsTreasury', '9.0.10');

COMMIT;

