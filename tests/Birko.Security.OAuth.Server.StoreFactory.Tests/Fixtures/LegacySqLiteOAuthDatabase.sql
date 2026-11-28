-- A database written by Birko.Security.OAuth.Server.SqLite (TASK-518), dumped with `sqlite3 legacy.db .dump` on
-- 2026-11-28, just before that package was deleted (TASK-1013 step 6). One record per store, written through the
-- package's own stores. Kept so the factory stores keep reading and writing the databases it left behind (DraCode's
-- server, for one). Never regenerate it: the package that wrote it is gone, and the point is that it does not change.
PRAGMA foreign_keys=OFF;
BEGIN TRANSACTION;
CREATE TABLE IF NOT EXISTS "oauth_clients" (ClientId TEXT NOT NULL, ClientSecretHash TEXT, ClientType INTEGER NOT NULL, Name TEXT, IsEnabled INTEGER NOT NULL, ClientCreatedAt INTEGER NOT NULL, CollectionsJson TEXT, Guid TEXT PRIMARY KEY UNIQUE, CreatedAt INTEGER NOT NULL, UpdatedAt INTEGER NOT NULL, PrevUpdatedAt INTEGER);
INSERT INTO oauth_clients VALUES('web',NULL,0,'Web app',1,'2026-11-28 12:24:26.936482','{"redirectUris":["https://app.test/cb"],"allowedGrantTypes":["authorization_code"],"allowedScopes":["read"]}','2b477f0e-534d-4e76-9e20-583b2ae75877','2026-11-28 12:24:26.9419007','2026-11-28 12:24:26.9419018',NULL);
CREATE TABLE IF NOT EXISTS "oauth_refresh_tokens" (Guid TEXT PRIMARY KEY UNIQUE, TokenHash TEXT NOT NULL, ClientId TEXT, UserId TEXT, Scope TEXT, ExpiresAt INTEGER NOT NULL, Revoked INTEGER NOT NULL, ClaimsJson TEXT);
INSERT INTO oauth_refresh_tokens VALUES('aefdfb2a-5256-4adf-80cb-5f21d85bfc09','H','web','u','read','2026-12-01 12:00:00',0,'{"name":"octocat"}');
CREATE TABLE IF NOT EXISTS "oauth_auth_codes" (Code TEXT, ClientId TEXT, UserId TEXT, RedirectUri TEXT, Scope TEXT, CodeChallenge TEXT, CodeChallengeMethod TEXT, ExpiresAt INTEGER NOT NULL, Used INTEGER NOT NULL, Guid TEXT PRIMARY KEY);
INSERT INTO oauth_auth_codes VALUES('c1','web','u','','',NULL,NULL,'2026-12-01 12:00:00',0,'ff05d5e7-a179-4859-9f98-1ff23051d97b');
CREATE TABLE IF NOT EXISTS "oauth_device_codes" (DeviceCode TEXT, UserCode TEXT, ClientId TEXT, Scope TEXT, ExpiresAt INTEGER NOT NULL, Status INTEGER NOT NULL, UserId TEXT, LastPolledAt INTEGER, Guid TEXT PRIMARY KEY);
INSERT INTO oauth_device_codes VALUES('d1','AAAA-BBBB','web','','2026-12-01 12:00:00',0,NULL,NULL,'cf6b2e5d-9f24-485c-8361-8c158de999c8');
CREATE TABLE IF NOT EXISTS "oauth_consents" (UserId TEXT, ClientId TEXT, Scope TEXT, GrantedAt INTEGER NOT NULL, Guid TEXT PRIMARY KEY);
INSERT INTO oauth_consents VALUES('u','web','read','2026-11-28 12:24:27.4705811','e921f849-8efd-4037-960c-1bf7918fc940');
CREATE INDEX "ix_oauth_clients_client_id" ON "oauth_clients" (ClientId);
CREATE INDEX "ix_oauth_refresh_tokens_hash" ON "oauth_refresh_tokens" (TokenHash);
CREATE INDEX "ix_oauth_refresh_tokens_family" ON "oauth_refresh_tokens" (ClientId, UserId);
COMMIT;
